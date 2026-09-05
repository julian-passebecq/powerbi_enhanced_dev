param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string[]]$Arguments,
    [Parameter(Mandatory)][string]$OutputBase,
    [int]$TimeoutSeconds = 60,
    [string]$IsolatedProfile
)
$ErrorActionPreference = 'Stop'
$executablePath = (Resolve-Path -LiteralPath $Executable).Path
$outputPath = [IO.Path]::GetFullPath($OutputBase)
New-Item -ItemType Directory -Path (Split-Path $outputPath) -Force | Out-Null
function ConvertTo-NativeArgument([string]$Value) {
    '"' + [regex]::Replace([regex]::Replace($Value, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1') + '"'
}
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = $executablePath
$start.Arguments = ($Arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' '
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.RedirectStandardInput = $true
$start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
$start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
if ($IsolatedProfile) {
    $start.EnvironmentVariables['PBIBENCH_CLI_STATE_DIRECTORY'] = [IO.Path]::GetFullPath($IsolatedProfile)
}
$process = [Diagnostics.Process]::new()
$process.StartInfo = $start
$timer = [Diagnostics.Stopwatch]::StartNew()
try {
    if (-not $process.Start()) { throw 'Could not start the CLI.' }
    $process.StandardInput.Close()
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill()
        $process.WaitForExit()
        throw "CLI exceeded its $TimeoutSeconds second process deadline."
    }
    $output = $stdout.GetAwaiter().GetResult()
    $errors = $stderr.GetAwaiter().GetResult()
    [IO.File]::WriteAllText($outputPath + '.json', $output, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($outputPath + '.stderr.txt', $errors, [Text.UTF8Encoding]::new($false))
    $document = $output | ConvertFrom-Json -ErrorAction Stop
    [pscustomobject]@{ ExitCode = $process.ExitCode; Result = $document; Stderr = $errors; ElapsedMilliseconds = $timer.ElapsedMilliseconds }
} finally { $process.Dispose() }
