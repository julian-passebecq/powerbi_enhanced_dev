param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/PbiBench.App/Assets'))
[System.IO.Directory]::CreateDirectory($assetDirectory) | Out-Null

function New-RoundedRectanglePath([single]$x, [single]$y, [single]$width, [single]$height, [single]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc(($x + $width - $diameter), $y, $diameter, $diameter, 270, 90)
    $path.AddArc(($x + $width - $diameter), ($y + $height - $diameter), $diameter, $diameter, 0, 90)
    $path.AddArc($x, ($y + $height - $diameter), $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

# These original vector coordinates match Assets/PbiBench.svg. The rendering uses only
# framework System.Drawing; no downloaded logos, icon fonts, assets, or build dependency.
$frames = @()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $large = New-Object System.Drawing.Bitmap(($size * 4), ($size * 4))
    $graphics = [System.Drawing.Graphics]::FromImage($large)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.ScaleTransform(($size * 4 / 256.0), ($size * 4 / 256.0))
    $navy = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#173C52'))
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $gold = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#E7BE24'))
    $background = New-RoundedRectanglePath 4 4 248 248 44
    $graphics.FillPath($navy, $background)
    $edge = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 15)
    $edge.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $points = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF(76,70)),
        (New-Object System.Drawing.PointF(180,128)),
        (New-Object System.Drawing.PointF(76,186))
    )
    $graphics.DrawPolygon($edge, $points)
    foreach ($node in @(@(53,47,$gold), @(157,105,$white), @(53,163,$white))) {
        $path = New-RoundedRectanglePath $node[0] $node[1] 46 46 9
        $graphics.FillPath($node[2], $path)
        $path.Dispose()
    }
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $scaled = [System.Drawing.Graphics]::FromImage($bitmap)
    $scaled.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $scaled.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $scaled.DrawImage($large, 0, 0, $size, $size)
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,$stream.ToArray()
    if ($size -eq 256) { $bitmap.Save((Join-Path $assetDirectory 'PbiBench.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $scaled.Dispose(); $bitmap.Dispose(); $edge.Dispose()
    $background.Dispose(); $navy.Dispose(); $white.Dispose(); $gold.Dispose(); $graphics.Dispose(); $large.Dispose()
}

$iconPath = Join-Path $assetDirectory 'PbiBench.ico'
$output = [System.IO.File]::Create($iconPath)
$writer = New-Object System.IO.BinaryWriter($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]7)
    $offset = 6 + 7 * 16
    $sizes = @(16, 24, 32, 48, 64, 128, 256)
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose(); $output.Dispose() }
Write-Output "Generated original PbiBench icon: 16, 24, 32, 48, 64, 128 and 256 pixels."
