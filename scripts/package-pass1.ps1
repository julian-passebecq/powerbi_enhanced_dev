param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$Destination = (Join-Path $PSScriptRoot '../artifacts/PbiBench'),
    [switch]$SkipSmoke
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = Join-Path $repo "src/PbiBench.App/bin/$Configuration/net48"
$cliSource = Join-Path $repo "src/PbiBench.Cli/bin/$Configuration/net48"
$toolboxSource = Join-Path $repo "src/PbiBench.FabricToolbox/bin/$Configuration/net10.0-windows"
$destinationPath = [IO.Path]::GetFullPath($Destination)
$artifacts = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))
if (-not $destinationPath.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Portable output must be inside this workspace artifacts directory.'
}
if (Test-Path -LiteralPath $destinationPath) { throw "Output already exists and will not be overwritten: $destinationPath. Use a new -Destination." }
foreach ($required in @('PbiBench.exe', 'PbiBench.exe.config', 'TOMWrapper.dll', 'TabularEditor.exe', 'FastColoredTextBox.dll', 'Microsoft.AnalysisServices.Tabular.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $required))) { throw "Missing Release runtime file: $required. Run build-pass1.ps1 -Configuration $Configuration first." }
}
foreach ($required in @('pbibench.exe', 'pbibench.exe.config', 'TOMWrapper.dll', 'Microsoft.AnalysisServices.Tabular.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $cliSource $required))) { throw "Missing CLI runtime file: $required. Build the complete solution before packaging." }
}
$staging = Join-Path $artifacts ('.PbiBench-stage-' + [Guid]::NewGuid().ToString('N'))
foreach ($required in @('PbiBench.FabricToolbox.exe', 'PbiBench.FabricToolbox.runtimeconfig.json', 'PbiBench.FabricToolbox.deps.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $toolboxSource $required))) { throw "Missing Fabric Toolbox runtime: $required. Build the solution first." }
}
New-Item -ItemType Directory -Path $staging | Out-Null
# Ship runtime binaries/config only; exclude build logs, private settings, scratch queries and test fixtures.
foreach ($runtime in @(@{ Source = $source; Output = $staging }, @{ Source = $cliSource; Output = (Join-Path $staging 'cli') }, @{ Source = $toolboxSource; Output = (Join-Path $staging 'fabric-toolbox') })) {
# Windows file names ignore case: the CLI lives in cli/ to preserve both entry points.
Get-ChildItem -LiteralPath $runtime.Source -File -Recurse | Where-Object {
    ($_.Extension -in @('.dll', '.exe', '.config') -or ($runtime.Source -eq $toolboxSource -and $_.Name -match '\.(runtimeconfig|deps)\.json$')) -and $_.FullName -notmatch '[\\/](TestResults|examples)[\\/]'
} | ForEach-Object {
    $relative = $_.FullName.Substring($runtime.Source.Length + 1)
    $target = Join-Path $runtime.Output $relative
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    $before = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    Copy-Item -LiteralPath $_.FullName -Destination $target
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $before -or
        (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash -ne $before) {
        throw "Build output changed while packaging: $relative. Finish the build and package again into a fresh destination."
    }
}
}
New-Item -ItemType Directory -Path (Join-Path $staging 'examples') | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'examples/pass1-demo.bim') -Destination (Join-Path $staging 'examples/pass1-demo.bim')
Copy-Item -LiteralPath (Join-Path $repo 'examples/prototypes') -Destination (Join-Path $staging 'examples/prototypes') -Recurse
New-Item -ItemType Directory -Path (Join-Path $staging 'docs') | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'docs/architecture') -Destination (Join-Path $staging 'docs/architecture') -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'docs/V11_IMPLEMENTATION.md') -Destination (Join-Path $staging 'docs/V11_IMPLEMENTATION.md')
foreach ($guide in @('V9_CLI_REFERENCE.md', 'V9_AGENT_REFERENCE.md', 'V9_PROTOTYPES_REFERENCE.md', 'V9_MODEL_AUTHORING_REFERENCE.md', 'V9_DAX_AUTHORING_REFERENCE.md', 'V9_FABRIC_REFERENCE.md', 'V9_FABRIC_AUTHORING_REFERENCE.md', 'V9_REFRESH_REFERENCE.md', 'V9_WORKSPACE_REFERENCE.md', 'V9_SCRIPT_AUTOMATION_REFERENCE.md', 'V9_SEMANTIC_TESTS_REFERENCE.md', 'V9_VERTIPAQ_REFERENCE.md', 'V9_BPA_RULE_PACKS.md')) {
    Copy-Item -LiteralPath (Join-Path $repo "docs/$guide") -Destination (Join-Path $staging "docs/$guide")
}
$notices = Join-Path $staging 'licenses'
$upstreamNotices = Join-Path $notices 'TabularEditor2'
New-Item -ItemType Directory -Path $upstreamNotices -Force | Out-Null
$vendor = Join-Path $repo 'vendor/TabularEditor2-2.28.0'
foreach ($notice in @('LICENSE', 'license-FastColoredTextbox.txt', 'license-FastWildcardMatching.txt', 'license-TreeViewAdv.txt', 'TabularEditor-license.rtf')) {
    Copy-Item -LiteralPath (Join-Path $vendor $notice) -Destination (Join-Path $upstreamNotices $notice)
}
foreach ($doc in @('TE2_LICENSE_INVENTORY_V6.md', 'TE2_NUGET_LICENSE_INVENTORY.json', 'TE2_NOTICE_HASHES.json', 'TE2_INTEGRATION_PATCH_V6.md')) {
    Copy-Item -LiteralPath (Join-Path $repo "docs/$doc") -Destination (Join-Path $notices $doc)
}
Copy-Item -LiteralPath (Join-Path $repo 'vendor/notices') -Destination (Join-Path $notices 'additional') -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'vendor/patches/te2-2.28.0-remote-write-review.patch') -Destination $upstreamNotices
Copy-Item -LiteralPath (Join-Path $repo 'vendor/patches/te2-2.28.0-function-undo-order.patch') -Destination $upstreamNotices
$packageFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
Get-ChildItem -LiteralPath (Join-Path $vendor 'packages') -Filter '*.nupkg' -Recurse | ForEach-Object { [void]$packageFiles.Add($_.FullName) }
foreach ($assetsRelativePath in @('src/PbiBench.App/obj/project.assets.json', 'src/PbiBench.Cli/obj/project.assets.json', 'src/PbiBench.FabricToolbox/obj/project.assets.json')) {
$assetsFile = Join-Path $repo $assetsRelativePath
if (-not (Test-Path -LiteralPath $assetsFile)) { throw 'Package assets missing; build the complete solution before packaging.' }
$assets = Get-Content -LiteralPath $assetsFile -Raw | ConvertFrom-Json
foreach ($library in $assets.libraries.PSObject.Properties) {
    if ($library.Value.type -ne 'package') { continue }
    foreach ($packageRoot in $assets.packageFolders.PSObject.Properties.Name) {
        $packageDirectory = Join-Path $packageRoot $library.Value.path
        if (Test-Path -LiteralPath $packageDirectory) {
            Get-ChildItem -LiteralPath $packageDirectory -Filter '*.nupkg' -File | ForEach-Object { [void]$packageFiles.Add($_.FullName) }
        }
    }
}
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$packageManifest = @()
$processedPackages = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($packageFile in $packageFiles) {
    $packageId = [IO.Path]::GetFileNameWithoutExtension($packageFile)
    if ($processedPackages.ContainsKey($packageId)) { continue }
    $processedPackages.Add($packageId, $packageFile)
    $archive = [IO.Compression.ZipFile]::OpenRead($packageFile)
    try {
        $packageOutput = [IO.Path]::GetFullPath((Join-Path $notices "packages/$packageId"))
        New-Item -ItemType Directory -Path $packageOutput -Force | Out-Null
        $included = @()
        foreach ($entry in $archive.Entries) {
            if (-not $entry.Name -or ($entry.FullName -notmatch '(?i)(license|notice|copying)' -and $entry.FullName -notlike '*.nuspec')) { continue }
            $entryTarget = [IO.Path]::GetFullPath((Join-Path $packageOutput $entry.FullName))
            if (-not $entryTarget.StartsWith($packageOutput + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid package notice path.' }
            New-Item -ItemType Directory -Path (Split-Path $entryTarget) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $entryTarget)
            $included += $entry.FullName
        }
        $packageManifest += [pscustomobject]@{ Package = $packageId; IncludedNotices = $included }
    } finally { $archive.Dispose() }
}
$packageManifest | Sort-Object Package | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $notices 'packaged-notices.json') -Encoding utf8
@'
PbiBench V11.1 portable build

Launch PbiBench.exe on Windows with .NET Framework 4.8 installed.
Apps / Tools launches fabric-toolbox/PbiBench.FabricToolbox.exe in its separate process.
Fabric Toolbox requires the .NET 10 Windows Desktop runtime. Its dependencies stay in its own folder.
Use Home > Open demo for the included synthetic example model.
DAX Studio and Power BI Desktop are separate optional installed applications.
Run cli/pbibench.exe --help for the shared semantic-engine CLI.
The CLI lives in its own folder because Windows file names are case-insensitive.
Model tools > Compiler / packages includes bounded local prototypes.
Their original sample YAML and MIT package are in examples/prototypes/.
Feature references and supported limits are in docs/.

TE2 2.28.0 source: https://github.com/TabularEditor/TabularEditor/tree/75f10e331b8de0dda5c213180b9b8867b4a38191
PbiBench modifications: licenses/TabularEditor2/te2-2.28.0-remote-write-review.patch
Function Undo ordering correction: licenses/TabularEditor2/te2-2.28.0-function-undo-order.patch
Original and dependency notices: licenses/
FastColoredTextBox remains a separate replaceable library. Preserve dependency notices when redistributing.

No account credentials, user settings or private model fixtures are included.
'@ | Set-Content -LiteralPath (Join-Path $staging 'README.txt') -Encoding utf8
$sourceKind = 'Official pinned TE2 2.28.0 with PbiBench integration patches'
$upstreamCommit = '75f10e331b8de0dda5c213180b9b8867b4a38191'
if (Test-Path -LiteralPath (Join-Path $vendor '.pbibench-bundled-source')) {
    $sourceKind = 'Supplied V6 bundled TE2 snapshot with PbiBench integration patch; not a verified Git pin'
    $upstreamCommit = $null
    Add-Content -LiteralPath (Join-Path $staging 'README.txt') -Value "`nBuild source: $sourceKind"
}
$checksums = Get-ChildItem -LiteralPath $staging -File -Recurse | ForEach-Object {
    [pscustomobject]@{ Path = $_.FullName.Substring($staging.Length + 1).Replace('\', '/'); Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
}
[pscustomobject]@{ Product = 'PbiBench'; Configuration = $Configuration; Source = $sourceKind; UpstreamCommit = $upstreamCommit; CreatedUtc = [DateTime]::UtcNow.ToString('o'); Files = @($checksums) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $staging 'package-manifest.json') -Encoding utf8
if (-not $SkipSmoke) {
    & (Join-Path $PSScriptRoot 'invoke-smoke-pass1.ps1') -Executable (Join-Path $staging 'PbiBench.exe') -OutputDirectory (Join-Path $artifacts ('package-smoke-' + [Guid]::NewGuid().ToString('N')))
    & (Join-Path $PSScriptRoot 'invoke-cli-smoke.ps1') -Executable (Join-Path $staging 'cli/pbibench.exe') -ModelPath (Join-Path $staging 'examples/pass1-demo.bim') -OutputDirectory (Join-Path $artifacts ('package-cli-smoke-' + [Guid]::NewGuid().ToString('N')))
}
# Both source and destination have been resolved and checked inside the workspace artifact directory.
if (-not $staging.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid staging path.' }
Move-Item -LiteralPath $staging -Destination $destinationPath
Write-Host "Portable PbiBench folder: $destinationPath"
