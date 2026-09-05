param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release', [switch]$SkipPackaging)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dotnet = Join-Path $env:LOCALAPPDATA 'PbiBench/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet).Source }
$env:DOTNET_ROOT = Split-Path $dotnet
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$logs = Join-Path $repo ('artifacts/v11-gate-' + $Configuration + '-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $logs | Out-Null
Start-Transcript -LiteralPath (Join-Path $logs 'gate.log') | Out-Null
function Invoke-Dotnet([string[]]$Arguments) {
    & $dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "V11 gate failed: dotnet $($Arguments -join ' ')" }
}
function Invoke-ChildSmoke([string]$Executable, [string]$Arguments, [string]$ResultPath) {
    $process = Start-Process -FilePath $Executable -ArgumentList $Arguments -WorkingDirectory (Split-Path $Executable) -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'V11 UI smoke exceeded 60 seconds.' }
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $ResultPath)) { throw "V11 UI smoke failed. Evidence: $ResultPath" }
    } finally { $process.Dispose() }
}
Push-Location $repo
try {
    Invoke-Dotnet -Arguments @('build', 'PbiBench.slnx', '-c', $Configuration, '--nologo', '--verbosity', 'quiet')
    Invoke-Dotnet -Arguments @('test', 'tests/PbiBench.V11.Tests/PbiBench.V11.Tests.csproj', '-c', $Configuration, '--no-build', '--nologo', '--logger', 'trx', '--results-directory', $logs)
    Invoke-Dotnet -Arguments @('test', 'tests/PbiBench.Adapters.Tests/PbiBench.Adapters.Tests.csproj', '-c', $Configuration, '-f', 'net48', '--no-build', '--nologo', '--filter', 'FullyQualifiedName~TrustedScriptBoundaryTests|FullyQualifiedName~SafeScriptTests|FullyQualifiedName~FabricTransportTests|FullyQualifiedName~FabricSqlTests|FullyQualifiedName~ModelEditorBoundaryTests', '--logger', 'trx', '--results-directory', $logs)
    Invoke-Dotnet -Arguments @('test', 'tests/PbiBench.Semantic.Tests/PbiBench.Semantic.Tests.csproj', '-c', $Configuration, '--no-build', '--nologo', '--filter', 'FullyQualifiedName~AIContextCaptureTests|FullyQualifiedName~ScriptPreviewTests', '--logger', 'trx', '--results-directory', $logs)
    Invoke-Dotnet -Arguments @('test', 'tests/PbiBench.App.Tests/PbiBench.App.Tests.csproj', '-c', $Configuration, '--no-build', '--nologo', '--filter', 'FullyQualifiedName~V11WorkspaceTests|FullyQualifiedName~FabricWorkspaceViewTests', '--logger', 'trx', '--results-directory', $logs)
    $app = Join-Path $repo "src/PbiBench.App/bin/$Configuration/net48/PbiBench.exe"
    $toolbox = Join-Path $repo "src/PbiBench.FabricToolbox/bin/$Configuration/net10.0-windows/PbiBench.FabricToolbox.exe"
    if (-not $SkipPackaging) {
        $package = Join-Path $logs 'package'
        & (Join-Path $repo 'scripts/package-pass1.ps1') -Configuration $Configuration -Destination $package -SkipSmoke
        $app = Join-Path $package 'PbiBench.exe'
        $toolbox = Join-Path $package 'fabric-toolbox/PbiBench.FabricToolbox.exe'
    }
    $forbidden = Get-ChildItem -LiteralPath (Split-Path $toolbox) -File -Recurse | Where-Object { $_.Name -match '^(TabularEditor|TOMWrapper|PbiBench\.(App|ModelEditor|Semantic))(\.|$)' }
    if ($forbidden) { throw 'Fabric Toolbox contains Semantic IDE / TE2 runtime dependencies.' }
    $smoke = Join-Path $logs 'semantic-smoke'
    Invoke-ChildSmoke $app ('--smoke-test "' + $smoke + '" --v11') (Join-Path $smoke 'smoke-result.json')
    $result = Get-Content -LiteralPath (Join-Path $smoke 'smoke-result.json') -Raw | ConvertFrom-Json
    if ($result.success -ne $true) { throw 'Semantic UI smoke did not pass.' }
    $toolboxResult = Join-Path $logs 'toolbox-smoke.txt'
    Invoke-ChildSmoke $toolbox ('--smoke-test "' + $toolboxResult + '"') $toolboxResult
    if ((Get-Content -LiteralPath $toolboxResult -Raw) -notmatch '^Toolbox WPF launch:') { throw 'Fabric Toolbox smoke did not report a successful WPF launch.' }
    [pscustomobject]@{ success = $true; configuration = $Configuration; packaged = -not $SkipPackaging; semanticExecutable = $app; toolboxExecutable = $toolbox; liveIntegration = $false } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $logs 'gate-result.json') -Encoding utf8
    Write-Host "V11 final impacted $Configuration gate passed. Packaged: $(-not $SkipPackaging). Evidence: $logs"
} finally { Pop-Location; Stop-Transcript | Out-Null }
