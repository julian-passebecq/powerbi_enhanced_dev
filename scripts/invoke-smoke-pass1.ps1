param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(1, 60)][int]$TimeoutSeconds = 60
)
$ErrorActionPreference = 'Stop'
$appPath = [IO.Path]::GetFullPath($Executable)
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $appPath -PathType Leaf)) { throw "PbiBench executable not found: $appPath" }
if (Test-Path -LiteralPath $outputPath) { throw "Smoke output must be a fresh directory so stale evidence cannot pass: $outputPath" }
New-Item -ItemType Directory -Path $outputPath | Out-Null
# Arguments contain only a fixed switch and a validated filesystem path. Quote the path for spaces.
if ($outputPath.Contains('"')) { throw 'Smoke output directory cannot contain a quote.' }
$arguments = '--smoke-test "' + $outputPath + '"'
$process = Start-Process -FilePath $appPath -ArgumentList $arguments -WorkingDirectory (Split-Path $appPath) -WindowStyle Hidden -PassThru
try {
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        # Stop only the child process started for this smoke test, never an interactive PbiBench instance.
        $process.Kill()
        throw "PbiBench smoke test exceeded $TimeoutSeconds seconds. Evidence: $outputPath"
    }
    foreach ($failure in @('startup-error.txt', 'smoke-error.txt')) {
        $failurePath = Join-Path $outputPath $failure
        if (Test-Path -LiteralPath $failurePath) { throw "PbiBench smoke test failed; inspect $failurePath" }
    }
    $resultFile = Join-Path $outputPath 'smoke-result.json'
    if (-not (Test-Path -LiteralPath $resultFile)) { throw "PbiBench exited without smoke-result.json. Evidence: $outputPath" }
    $result = Get-Content -LiteralPath $resultFile -Raw | ConvertFrom-Json
    if ($result.success -ne $true -or $process.ExitCode -ne 0) { throw "PbiBench smoke test reported failure (exit $($process.ExitCode)). Evidence: $outputPath" }
    Write-Host "PbiBench smoke test passed: $(@($result.checks).Count) checks. Evidence: $outputPath"
} finally { $process.Dispose() }
