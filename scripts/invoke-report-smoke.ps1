param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release', [string]$Evidence = 'artifacts/ci-release')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath((Join-Path $repo $Evidence))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$result = Join-Path $output 'report-smoke.json'
$exe = Join-Path $repo "src/PbiBench.ReportStudio/bin/$Configuration/net10.0-windows/PbiBench.ReportStudio.exe"
$process = Start-Process -FilePath $exe -ArgumentList ('--smoke-test "' + $result + '"') -WindowStyle Hidden -PassThru
try {
    if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'Offline Report Studio smoke exceeded 60 seconds.' }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $result)) { throw 'Report Studio smoke failed.' }
    if ((Get-Content -LiteralPath $result -Raw | ConvertFrom-Json).success -ne $true) { throw 'Report Studio smoke did not report success.' }
} finally { $process.Dispose() }
