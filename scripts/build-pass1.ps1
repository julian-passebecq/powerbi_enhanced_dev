param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [switch]$SkipTests,
    [switch]$SkipUpstreamTests,
    [switch]$SkipSmoke,
    [switch]$InstallSdk,
    [switch]$Offline
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$toolsDir = Join-Path $repo '.tools'
$logs = Join-Path $repo 'artifacts/build'
New-Item -ItemType Directory -Path $toolsDir, $logs -Force | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio 2022 Build Tools with .NET desktop build tools are required.' }
$vsCandidates = @(& $vswhere -all -products '*' -requires Microsoft.Component.MSBuild -property installationPath)
# Upstream TabularEditorTest still references the legacy Visual Studio test framework.
$vsPath = $vsCandidates | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'Common7/IDE/PublicAssemblies/Microsoft.VisualStudio.QualityTools.UnitTestFramework.dll') } | Select-Object -First 1
if (-not $vsPath) { $vsPath = $vsCandidates | Select-Object -First 1 }
if (-not $vsPath) { throw 'MSBuild installation not found.' }
$msbuild = Join-Path $vsPath 'MSBuild/Current/Bin/MSBuild.exe'
$dotnet = Join-Path $env:LOCALAPPDATA 'PbiBench/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $installed = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($installed -and (& $installed.Source --list-sdks | Select-String '^10\.')) { $dotnet = $installed.Source }
    elseif ($InstallSdk) {
        $installer = Join-Path $toolsDir 'dotnet-install.ps1'
        Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
        & $installer -Version 10.0.400 -InstallDir (Split-Path $dotnet) -NoPath
        if (-not (Test-Path -LiteralPath $dotnet)) { throw '.NET 10 SDK installation failed.' }
    } else { throw 'Install .NET 10 SDK, or run this script with -InstallSdk for a user-local installation.' }
}
$env:DOTNET_ROOT = Split-Path $dotnet
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$nuget = Join-Path $toolsDir 'nuget.exe'
if (-not (Test-Path -LiteralPath $nuget)) { Invoke-WebRequest 'https://dist.nuget.org/win-x86-commandline/v6.14.0/nuget.exe' -OutFile $nuget }
$vendor = Join-Path $repo 'vendor/TabularEditor2-2.28.0'
& (Join-Path $PSScriptRoot 'update-te2-2.28.0.ps1') -Destination $vendor -Offline:$Offline
Push-Location $repo
try {
    & $nuget restore (Join-Path $vendor 'TabularEditor.sln') -NonInteractive -Verbosity quiet -MSBuildPath (Split-Path $msbuild)
    if ($LASTEXITCODE -ne 0) { throw 'TE2 NuGet restore failed.' }
    # Upstream consumes a hardcoded obj/Debug/DAXLexer.cs even in Release builds.
    & $msbuild (Join-Path $vendor 'AntlrGrammars/AntlrGrammars.csproj') /t:Build /p:Configuration=Debug /p:ImportDirectoryBuildProps=false /p:ImportDirectoryBuildTargets=false /nologo /v:quiet
    if ($LASTEXITCODE -ne 0) { throw 'TE2 grammar generation failed.' }
    & $msbuild (Join-Path $vendor 'TabularEditor.sln') /t:Build "/p:Configuration=$Configuration" /p:ImportDirectoryBuildProps=false /p:ImportDirectoryBuildTargets=false /nologo /v:quiet "/flp:logfile=$logs/te2-build.log;verbosity=normal"
    if ($LASTEXITCODE -ne 0) { throw "TE2 build failed. See $logs/te2-build.log" }
    & $dotnet build (Join-Path $repo 'PbiBench.slnx') -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw 'PbiBench build failed.' }
    if (-not $SkipTests) {
        Get-ChildItem (Join-Path $repo 'tests') -Recurse -Filter '*.csproj' | ForEach-Object {
            & $dotnet test $_.FullName -c $Configuration --nologo --logger trx --results-directory $logs
            if ($LASTEXITCODE -ne 0) { throw "PbiBench tests failed: $($_.FullName)" }
        }
        if (-not $SkipUpstreamTests) {
            $vstest = Join-Path $vsPath 'Common7/IDE/Extensions/TestPlatform/vstest.console.exe'
            if (-not (Test-Path -LiteralPath $vstest)) { $vstest = Join-Path $env:DOTNET_ROOT 'sdk/10.0.400/vstest.console.dll' }
            $cases = @(
                @{ Project = 'TOMWrapperTest'; Filter = 'FullyQualifiedName~TabularEditor.TOMWrapper.GeneratedTests|FullyQualifiedName~RemoteWriteReviewTests' },
                @{ Project = 'TabularEditorTest'; Filter = 'FullyQualifiedName~ScriptEngineTests|FullyQualifiedName~ScriptParserTests|FullyQualifiedName~ScriptHelperTests|FullyQualifiedName~GetPathTests' }
            )
            foreach ($case in $cases) {
                $testArgs = @((Join-Path $vendor "$($case.Project)/bin/$Configuration/$($case.Project).dll"), "/TestCaseFilter:$($case.Filter)", "/Logger:trx;LogFileName=$($case.Project).trx", "/ResultsDirectory:$logs")
                if ($vstest.EndsWith('.dll')) { & $dotnet $vstest @testArgs } else { & $vstest @testArgs }
                if ($LASTEXITCODE -ne 0) { throw "Offline upstream tests failed: $($case.Project)" }
            }
        }
    }
    $app = Join-Path $repo "src/PbiBench.App/bin/$Configuration/net48/PbiBench.exe"
    if (-not $SkipSmoke) {
        $smokeOutput = Join-Path $logs ("smoke-$Configuration-" + [Guid]::NewGuid().ToString('N'))
        & (Join-Path $PSScriptRoot 'invoke-smoke-pass1.ps1') -Executable $app -OutputDirectory $smokeOutput
        $cliSmokeOutput = Join-Path $logs ("cli-smoke-$Configuration-" + [Guid]::NewGuid().ToString('N'))
        & (Join-Path $PSScriptRoot 'invoke-cli-smoke.ps1') -Executable (Join-Path $repo "src/PbiBench.Cli/bin/$Configuration/net48/pbibench.exe") -OutputDirectory $cliSmokeOutput
    }
    Write-Host "PbiBench: $app"
} finally { Pop-Location }
