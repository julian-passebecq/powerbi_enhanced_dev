param(
    [Parameter(Mandatory)][string]$Executable,
    [string]$ModelPath = (Join-Path $PSScriptRoot '../examples/pass1-demo.bim'),
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $root) { throw "CLI smoke output already exists: $root" }
New-Item -ItemType Directory -Path $root | Out-Null
$model = Join-Path $root 'source model.bim'
Copy-Item -LiteralPath $ModelPath -Destination $model
$original = (Get-FileHash -LiteralPath $model -Algorithm SHA256).Hash
$checks = [Collections.Generic.List[object]]::new()
function Run-Case([string]$Name, [string[]]$CliArguments, [int]$ExitCode = 0) {
    $run = & (Join-Path $PSScriptRoot 'invoke-cli-command.ps1') -Executable $Executable -Arguments ($CliArguments + @('--json', '--non-interactive')) -OutputBase (Join-Path $root $Name) -IsolatedProfile (Join-Path $root 'profile')
    if ($run.ExitCode -ne $ExitCode) { throw "$Name returned $($run.ExitCode), expected $ExitCode. See its captured JSON and stderr." }
    if ($Name -ne 'schema' -and $run.Result.exitCode -ne $ExitCode) { throw "$Name returned a mismatched JSON exit code." }
    if ($ExitCode -eq 0 -and $run.Stderr.Trim().Length -ne 0) { throw "$Name wrote unexpected stderr." }
    if ($ExitCode -ne 0 -and $run.Stderr.Trim().Length -eq 0) { throw "$Name omitted its failure diagnostic on stderr." }
    $checks.Add([pscustomobject]@{ Name = $Name; Passed = $true; ExitCode = $run.ExitCode; ElapsedMilliseconds = $run.ElapsedMilliseconds })
    return $run.Result
}
try {
    $inspect = Run-Case 'inspect' @('inspect', '--model', $model)
    if ($inspect.data.model -ne 'PbiBench Demo' -or $inspect.data.counts.Table -ne 2) { throw 'Inspect did not read the native demo metadata.' }
    $null = Run-Case 'list' @('list', '--model', $model, '--kind', 'Measure')
    $get = Run-Case 'get' @('get', '--model', $model, '--kind', 'Measure', '--name', 'Revenue', '--table', 'Sales', '--property', 'Expression')
    if ($get.data[0].value -ne 'SUM(Sales[Amount])') { throw 'Get returned the wrong expression.' }
    $null = Run-Case 'bpa' @('bpa', '--model', $model, '--fail-on', 'None')
    $null = Run-Case 'bpa-threshold' @('bpa', '--model', $model, '--fail-on', 'Information') 3
    $null = Run-Case 'validate' @('validate', '--model', $model, '--fail-on', 'None')
    $null = Run-Case 'diff-identical' @('diff', '--model', $model, '--against', $model)
    $profile = Join-Path $root 'ci-profile.json'
    @{ version = 1; modelPath = $model } | ConvertTo-Json | Set-Content -LiteralPath $profile -Encoding utf8
    $null = Run-Case 'profile-inspect' @('inspect', '--profile', $profile)
    $null = Run-Case 'schema' @('--schema')
    $output = Join-Path $root 'approved model.bim'
    $reviewFile = Join-Path $root 'property-review.json'
    $value = 'Reviewed "CLI" description with spaces and Unicode: Zürich'
    $preview = Run-Case 'set-preview' @('set', '--model', $model, '--kind', 'Measure', '--name', 'Revenue', '--table', 'Sales', '--property', 'Description', '--value', $value, '--output', $output, '--review-out', $reviewFile)
    if (Test-Path -LiteralPath $output) { throw 'Preview wrote the output before approval.' }
    $null = Run-Case 'forged-approval' @('apply', '--review', $reviewFile, '--approve', ('0' * 64)) 3
    $null = Run-Case 'set-apply' @('apply', '--review', $reviewFile, '--approve', $preview.review.hash)
    $saved = Run-Case 'get-saved' @('get', '--model', $output, '--kind', 'Measure', '--name', 'Revenue', '--table', 'Sales', '--property', 'Description')
    if ($saved.data[0].value -cne $value) { throw 'Separate-process approved output did not preserve exact Unicode/quote content.' }
    $null = Run-Case 'replay-rejected' @('apply', '--review', $reviewFile, '--approve', $preview.review.hash) 3
    $null = Run-Case 'diff-changed' @('diff', '--model', $model, '--against', $output)
    $staleReview = Join-Path $root 'stale-review.json'
    $stale = Run-Case 'stale-preview' @('set', '--model', $model, '--kind', 'Measure', '--name', 'Revenue', '--table', 'Sales', '--property', 'DisplayFolder', '--value', 'Stale review', '--output', (Join-Path $root 'stale-output.bim'), '--review-out', $staleReview)
    Add-Content -LiteralPath $model -Value ' '
    $null = Run-Case 'stale-apply-rejected' @('apply', '--review', $staleReview, '--approve', $stale.review.hash) 3
    Copy-Item -LiteralPath $ModelPath -Destination $model -Force
    $request = Join-Path $root 'create-request.json'
    @{ version = 1; kind = 'Script'; target = @{ modelPath = $model }; script = 'Model.Tables["Sales"].AddMeasure("CLI new measure", "SUM(Sales[Amount])");'; outputPath = (Join-Path $root 'new-measure.bim') } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $request -Encoding utf8
    $createReview = Join-Path $root 'create-review.json'
    $create = Run-Case 'script-create-preview' @('script', '--request', $request, '--review-out', $createReview)
    $null = Run-Case 'script-create-apply' @('apply', '--review', $createReview, '--approve', $create.review.hash)
    $actionReview = Join-Path $root 'action-review.json'
    $action = Run-Case 'action-preview' @('action', '--model', $model, '--action', 'OrganizeMeasures', '--kind', 'Measure', '--name', 'Revenue', '--table', 'Sales', '--output', (Join-Path $root 'organized.bim'), '--review-out', $actionReview)
    $null = Run-Case 'action-apply' @('apply', '--review', $actionReview, '--approve', $action.review.hash)
    $null = Run-Case 'query-missing-target' @('query', '--query', 'EVALUATE { 1 }') 2
    $null = Run-Case 'refresh-missing-target' @('refresh') 2
    $null = Run-Case 'deploy-missing-source' @('deploy', '--server', 'unused-local-fixture', '--database', 'unused-fixture') 2
    $null = Run-Case 'unknown-option' @('inspect', '--model', $model, '--invented') 2
    $journal = Join-Path $root 'profile/CommandApprovals'
    if (-not (Test-Path -LiteralPath $journal) -or @(Get-ChildItem -LiteralPath $journal -Filter '*.claimed.json').Count -lt 3) { throw 'CLI review claims did not stay in the explicit isolated profile.' }
    if ((Get-FileHash -LiteralPath $model -Algorithm SHA256).Hash -ne $original) { throw 'Smoke left its source model changed.' }
    [pscustomobject]@{ success = $true; checks = @($checks.ToArray()); liveQueryValidation = 'Not executed: use an accessible engine catalog. Injected public transport tests cover query command behavior.' } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'cli-smoke-result.json') -Encoding utf8
    Write-Host "CLI launch checks passed: $($checks.Count). Evidence: $root"
} catch {
    [pscustomobject]@{ success = $false; checks = @($checks.ToArray()); error = $_.Exception.Message } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'cli-smoke-result.json') -Encoding utf8
    throw
}
