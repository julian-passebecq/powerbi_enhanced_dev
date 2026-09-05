param([switch]$SkipPackaging)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dotnet = Join-Path $env:LOCALAPPDATA 'PbiBench/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet).Source }
$env:DOTNET_ROOT = Split-Path $dotnet
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$logs = Join-Path $repo ('artifacts/v2-gate-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $logs | Out-Null
Start-Transcript -LiteralPath (Join-Path $logs 'gate.log') | Out-Null
function Invoke-Dotnet([string[]]$Arguments) {
    & $dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Gen-2 Release gate failed: dotnet $($Arguments -join ' ')" }
}
function Invoke-Test([string]$Project, [string]$Framework, [string]$Filter = '') {
    $arguments = @('test', $Project, '-c', 'Release', '-f', $Framework, '--no-build', '--nologo', '--logger', 'trx', '--results-directory', $logs)
    if ($Filter) { $arguments += @('--filter', $Filter) }
    Invoke-Dotnet -Arguments $arguments
}
function Invoke-Smoke([string]$Executable, [string]$Arguments, [string]$ResultPath) {
    $process = Start-Process -FilePath $Executable -ArgumentList $Arguments -WorkingDirectory (Split-Path $Executable) -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit(60000)) { $process.Kill(); throw "Smoke exceeded 60 seconds: $Executable" }
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $ResultPath)) { throw "Smoke failed. Evidence: $ResultPath" }
        $result = Get-Content -LiteralPath $ResultPath -Raw | ConvertFrom-Json
        if ($result.success -ne $true) { throw "Smoke did not report success: $ResultPath" }
    } finally { $process.Dispose() }
}
Push-Location $repo
try {
    Invoke-Dotnet -Arguments @('build', 'PbiBench.slnx', '-c', 'Release', '--nologo', '--verbosity', 'quiet')
    Invoke-Test 'tests/PbiBench.V2.Tests/PbiBench.V2.Tests.csproj' 'net10.0'
    foreach ($framework in @('net10.0', 'net48')) {
        Invoke-Test 'tests/PbiBench.V11.Tests/PbiBench.V11.Tests.csproj' $framework
    }
    Invoke-Test 'tests/PbiBench.App.Tests/PbiBench.App.Tests.csproj' 'net48' 'FullyQualifiedName~DaxWorkspaceTests|FullyQualifiedName~CSharpAutomationTests|FullyQualifiedName~FeatureMapTests|FullyQualifiedName~V11WorkspaceTests|FullyQualifiedName~ConnectedWriteGuardTests'
    Invoke-Test 'tests/PbiBench.Semantic.Tests/PbiBench.Semantic.Tests.csproj' 'net48' 'FullyQualifiedName~DiagramAuthoringTests|FullyQualifiedName~ScriptPreviewTests|FullyQualifiedName~SemanticAndAutomationTests|FullyQualifiedName~SelectionInspectorTests|FullyQualifiedName~SemanticImpactTests'
    Invoke-Test 'tests/PbiBench.FabricToolbox.Tests/PbiBench.FabricToolbox.Tests.csproj' 'net10.0-windows'
    & (Join-Path $repo 'scripts/test-process-isolation.ps1') -Configuration Release
    Invoke-Test 'tests/PbiBench.Adapters.Tests/PbiBench.Adapters.Tests.csproj' 'net48' 'FullyQualifiedName~SafeScriptTests|FullyQualifiedName~ModelEditorBoundaryTests|FullyQualifiedName~DaxScratchEditorBoundaryTests|FullyQualifiedName~DaxStudio'
    Invoke-Test 'tests/PbiBench.Dax.LanguageService.Tests/PbiBench.Dax.LanguageService.Tests.csproj' 'net48'
    $app = Join-Path $repo 'src/PbiBench.App/bin/Release/net48/PbiBench.exe'
    $report = Join-Path $repo 'src/PbiBench.ReportStudio/bin/Release/net10.0-windows/PbiBench.ReportStudio.exe'
    $toolbox = Join-Path $repo 'src/PbiBench.FabricToolbox/bin/Release/net10.0-windows/PbiBench.FabricToolbox.exe'
    if (-not $SkipPackaging) {
        $package = Join-Path $logs 'package'
        & (Join-Path $repo 'scripts/package-pass1.ps1') -Configuration Release -Destination $package -SkipSmoke
        $app = Join-Path $package 'PbiBench.exe'; $report = Join-Path $package 'report-studio/PbiBench.ReportStudio.exe'
        $toolbox = Join-Path $package 'fabric-toolbox/PbiBench.FabricToolbox.exe'
        if (-not (Test-Path -LiteralPath (Join-Path $package 'licenses/Microsoft-PBIR-schemas/LICENSE'))) { throw 'Schema attribution is missing from the package.' }
    }
    $forbidden = Get-ChildItem -LiteralPath (Split-Path $report) -File -Recurse | Where-Object { $_.Name -match '^(TabularEditor|TOMWrapper|PbiBench\.(App|ModelEditor|Semantic|Fabric))(\.|$)' }
    if ($forbidden) { throw 'Report Studio package violates the process/dependency boundary.' }
    $forbidden = Get-ChildItem -LiteralPath (Split-Path $toolbox) -File -Recurse | Where-Object { $_.Name -match '^(TabularEditor|TOMWrapper|PbiBench\.(App|ModelEditor|Semantic))(\.|$)' }
    if ($forbidden) { throw 'Fabric Toolbox package violates the process/dependency boundary.' }
    $semanticSmoke = Join-Path $logs 'semantic-smoke'
    Invoke-Smoke $app ('--smoke-test "' + $semanticSmoke + '" --v2') (Join-Path $semanticSmoke 'smoke-result.json')
    $reportSmoke = Join-Path $logs 'report-smoke.json'
    Invoke-Smoke $report ('--smoke-test "' + $reportSmoke + '"') $reportSmoke
    $toolboxSmoke = Join-Path $logs 'toolbox-smoke.txt'
    $process = Start-Process -FilePath $toolbox -ArgumentList ('--smoke-test "' + $toolboxSmoke + '"') -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'Toolbox smoke exceeded 60 seconds.' }
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $toolboxSmoke) -or (Get-Content -LiteralPath $toolboxSmoke -Raw) -notmatch '^Toolbox WPF launch:') { throw 'Toolbox smoke failed.' }
    } finally { $process.Dispose() }
    [pscustomobject]@{ success = $true; configuration = 'Release'; scope = 'Gen-2 impacted'; packaged = -not $SkipPackaging; semanticExecutable = $app; reportExecutable = $report; liveIntegration = $false } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $logs 'gate-result.json') -Encoding utf8
    Write-Host "Gen-2 impacted Release gate passed. Evidence: $logs"
} finally { Pop-Location; Stop-Transcript | Out-Null }
