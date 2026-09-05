param(
    [string]$Destination = (Join-Path $PSScriptRoot '../vendor/TabularEditor2-2.28.0'),
    [switch]$Offline
)
$ErrorActionPreference = 'Stop'
$commit = '75f10e331b8de0dda5c213180b9b8867b4a38191'
$target = [IO.Path]::GetFullPath($Destination)
$offlineMarker = Join-Path $target '.pbibench-bundled-source'
if ($Offline -and -not (Test-Path -LiteralPath $target)) {
    $bundled = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../vendor/TabularEditor2-bundled'))
    if (-not (Test-Path -LiteralPath (Join-Path $bundled 'TabularEditor.sln'))) { throw 'Bundled offline source is missing.' }
    New-Item -ItemType Directory -Path $target | Out-Null
    # Enumerate only source files. Do not duplicate restored packages or previous build output.
    Get-ChildItem -LiteralPath $bundled -Recurse -File -Force | Where-Object {
        $_.FullName.Substring($bundled.Length + 1) -notmatch '(^|[\\/])(bin|obj|packages|\.git|TestResults)([\\/]|$)'
    } | ForEach-Object {
        $relative = $_.FullName.Substring($bundled.Length + 1)
        $copyTarget = Join-Path $target $relative
        New-Item -ItemType Directory -Path (Split-Path $copyTarget) -Force | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $copyTarget
    }
    'Bundled source from V6 handoff; not the verified official 2.28.0 Git commit.' | Set-Content -LiteralPath $offlineMarker
}
if (Test-Path -LiteralPath $target) {
    if (Test-Path -LiteralPath $offlineMarker) {
        Write-Warning 'Using supplied bundled snapshot offline; official Git pin has not been verified for this copy.'
        # A local repository root makes git apply resolve paths within this copy, not the parent workspace.
        if (-not (Test-Path -LiteralPath (Join-Path $target '.git'))) {
            & git init --quiet $target
            if ($LASTEXITCODE -ne 0) { throw 'Could not prepare isolated offline patch root.' }
        }
    }
    else {
        if (-not (Test-Path -LiteralPath (Join-Path $target '.git'))) { throw "Destination exists without Git provenance: $target. Nothing was overwritten." }
        $actual = & git -C $target rev-parse HEAD
        if ($LASTEXITCODE -ne 0 -or $actual -ne $commit) { throw 'Existing checkout differs from pinned commit. Nothing was overwritten.' }
        Write-Host 'Pinned TE2 2.28.0 already present; preserving all local integration changes.'
    }
} else {
    & git clone --branch 2.28.0 --depth 1 https://github.com/TabularEditor/TabularEditor.git $target
    if ($LASTEXITCODE -ne 0) { throw 'TE2 fetch failed.' }
    $actual = & git -C $target rev-parse HEAD
    if ($actual -ne $commit) { throw "Upstream tag does not match pinned commit $commit. Stop." }
}
foreach ($notice in @('LICENSE', 'license-FastColoredTextbox.txt', 'license-FastWildcardMatching.txt', 'license-TreeViewAdv.txt', 'TabularEditor-license.rtf')) {
    if (-not (Test-Path -LiteralPath (Join-Path $target $notice))) { throw "Missing required notice: $notice" }
}
$patch = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../vendor/patches/te2-2.28.0-remote-write-review.patch'))
& git -C $target apply --reverse --check $patch 2>$null
if ($LASTEXITCODE -ne 0) {
    & git -C $target apply --check $patch
    if ($LASTEXITCODE -ne 0) { throw 'Integration patch conflicts with existing changes; nothing was overwritten.' }
    & git -C $target apply $patch
    if ($LASTEXITCODE -ne 0) { throw 'Integration patch failed.' }
    Write-Host 'Applied minimal PbiBench remote-write review patch.'
} else { Write-Host 'PbiBench integration patch already applied.' }
$functionPatch = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../vendor/patches/te2-2.28.0-function-undo-order.patch'))
& git -C $target apply --reverse --check $functionPatch 2>$null
if ($LASTEXITCODE -ne 0) {
    & git -C $target apply --check $functionPatch
    if ($LASTEXITCODE -ne 0) { throw 'Function Undo order patch conflicts with existing changes; nothing was overwritten.' }
    & git -C $target apply $functionPatch
    if ($LASTEXITCODE -ne 0) { throw 'Function Undo order patch failed.' }
    Write-Host 'Applied bounded PbiBench Function Undo order correction.'
} else { Write-Host 'PbiBench Function Undo order patch already applied.' }
Write-Host "TE2 source: $target"
