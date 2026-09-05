param(
    [Parameter(Mandatory)][string]$ModelPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Executable = (Join-Path $PSScriptRoot '../src/PbiBench.Cli/bin/Release/net48/pbibench.exe'),
    [string]$BaselinePath,
    [ValidateSet('Error', 'Warning', 'Information', 'None')][string]$FailOn = 'Error',
    [string]$TestsPath,
    [string]$Server,
    [string]$Database,
    [string]$ConnectionEnvironmentVariable
)
$ErrorActionPreference = 'Stop'
if ($TestsPath -and (-not $Server -or -not $Database)) { throw 'Semantic assertions require an explicit accessible server and database.' }
if (-not $TestsPath -and ($Server -or $Database -or $ConnectionEnvironmentVariable)) { throw 'Connection options require an explicit semantic test artifact.' }
$root = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $root) { throw 'Choose a fresh CI evidence directory; previous reports are not overwritten.' }
New-Item -ItemType Directory -Path $root | Out-Null
$model = (Resolve-Path -LiteralPath $ModelPath).Path
$reports = [Collections.Generic.List[object]]::new()
function Invoke-CiStep([string]$Name, [string[]]$CliArguments) {
    $run = & (Join-Path $PSScriptRoot 'invoke-cli-command.ps1') -Executable $Executable -Arguments ($CliArguments + @('--json', '--non-interactive')) -OutputBase (Join-Path $root $Name) -TimeoutSeconds 3600
    $reports.Add([pscustomobject]@{ Step = $Name; ExitCode = $run.ExitCode; ElapsedMilliseconds = $run.ElapsedMilliseconds; Report = "$Name.json" })
}
Invoke-CiStep 'inspect' @('inspect', '--model', $model)
Invoke-CiStep 'validate' @('validate', '--model', $model, '--fail-on', $FailOn)
Invoke-CiStep 'bpa' @('bpa', '--model', $model, '--fail-on', $FailOn)
if ($BaselinePath) { Invoke-CiStep 'semantic-diff' @('diff', '--model', (Resolve-Path -LiteralPath $BaselinePath).Path, '--against', $model) }
if ($TestsPath) {
    $testArguments = @('test', '--tests', (Resolve-Path -LiteralPath $TestsPath).Path, '--server', $Server, '--database', $Database)
    if ($ConnectionEnvironmentVariable) { $testArguments += @('--connection-env', $ConnectionEnvironmentVariable) }
    Invoke-CiStep 'semantic-tests' $testArguments
}
$failed = @($reports | Where-Object ExitCode -ne 0)
[pscustomobject]@{ success = $failed.Count -eq 0; steps = @($reports.ToArray()); assertionsRequested = [bool]$TestsPath; remoteMutation = $false } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'ci-result.json') -Encoding utf8
Write-Host "Semantic CI: $($reports.Count) steps, $($failed.Count) failed. Evidence: $root"
if ($failed.Count -gt 0) { exit 3 }
