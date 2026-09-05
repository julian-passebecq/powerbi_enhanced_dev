param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
foreach ($app in @('ReportStudio', 'FabricToolbox')) {
    $output = Join-Path $repo "src/PbiBench.$app/bin/$Configuration/net10.0-windows"
    if (-not (Test-Path -LiteralPath (Join-Path $output "PbiBench.$app.dll"))) { throw "Missing $app build output." }
    $pattern = '^(TabularEditor|TOMWrapper|PbiBench\.(App|ModelEditor|Semantic|DaxStudio))(\.|$)'
    if ($app -eq 'ReportStudio') { $pattern = '^(TabularEditor|TOMWrapper|Microsoft\.Identity|PbiBench\.(App|ModelEditor|Semantic|Fabric|FabricToolbox|DaxStudio))(\.|$)' }
    if (Get-ChildItem -LiteralPath $output -File -Recurse | Where-Object { $_.Name -match $pattern }) { throw "$app violates process isolation." }
    Write-Host "$app output isolation passed."
}
