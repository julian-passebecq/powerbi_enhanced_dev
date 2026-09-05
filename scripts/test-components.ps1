param([Parameter(Mandatory = $true)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$packageRoot = [IO.Path]::GetFullPath($PackageDirectory)
$manifest = Get-Content -LiteralPath (Join-Path $packageRoot 'components.json') -Raw | ConvertFrom-Json
if ($manifest.contractVersion -ne 1 -or $manifest.externalToolsContractVersion -ne 1 -or $manifest.pbirContractVersion -ne 1 -or $manifest.components.Count -ne 3) { throw 'Invalid component contract.' }
foreach ($component in $manifest.components) {
    $path = [IO.Path]::GetFullPath((Join-Path $packageRoot $component.path))
    if (-not $path.StartsWith($packageRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $path)) { throw 'Missing component or invalid component path.' }
    # Modern .NET apphosts carry host versions; the managed module carries the product component version.
    $assembly = if ($component.id -eq 'semantic-ide') { $path } else { [IO.Path]::ChangeExtension($path, '.dll') }
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($assembly).ProductVersion.Split('+')[0]
    if ($version -ne $component.version) { throw "Component version mismatch: $($component.id), manifest $($component.version), binary $version" }
}
foreach ($notice in @('Microsoft-ReportTheme/LICENSE', 'Microsoft-ReportTheme/source-manifest.json', 'PbiBench-DesignSystem/LICENSE', 'PbiBench-DesignSystem/NOTICE.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $packageRoot "licenses/$notice"))) { throw "Missing package attribution: $notice" }
}
Write-Host 'Component paths, independently versioned binaries, contracts and design/theme attribution passed.'
