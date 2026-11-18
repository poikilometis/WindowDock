param([string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
function New-RoundedPath([single]$x, [single]$y, [single]$width, [single]$height, [single]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = 2 * $radius
    $path.AddArc($x,$y,$diameter,$diameter,180,90)
    $path.AddArc(($x+$width-$diameter),$y,$diameter,$diameter,270,90)
    $path.AddArc(($x+$width-$diameter),($y+$height-$diameter),$diameter,$diameter,0,90)
    $path.AddArc($x,($y+$height-$diameter),$diameter,$diameter,90,90)
    $path.CloseFigure()
    return ,$path
}
# The mark: two brackets locked around one window. It is drawn on terminal cells, 10 x 20 units each,
# like the README banner; assets/windowdock.svg holds the same drawing.
$mask = @(
    '#########.##',
    '##........##',
    '##..####..##',
    '##..####..##',
    '##........##',
    '##.#########')
$cellWidth = 10.0; $cellHeight = 20.0
function Test-Block([int]$row, [int]$column) {
    return $row -ge 0 -and $row -lt $mask.Count -and $column -ge 0 -and $column -lt $mask[$row].Length -and $mask[$row][$column] -eq '#'
}
function New-MarkBrush([int]$alpha) {
    # The gradient of the notification outline, from the left edge of the mark to the right edge of its shadow.
    $span = [System.Drawing.RectangleF]::new(0, 0, ($mask[0].Length + 1) * $cellWidth, $mask.Count * $cellHeight)
    $colors = '#00CCB1', '#1CA0FB', '#7B61FF', '#B79CFF' | ForEach-Object { [System.Drawing.Color]::FromArgb($alpha, [System.Drawing.ColorTranslator]::FromHtml($_)) }
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $span, $colors[0], $colors[3], ([single]0)
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 4
    $blend.Colors = [System.Drawing.Color[]]$colors; $blend.Positions = [single[]](0, 0.4, 0.8, 1)
    $brush.InterpolationColors = $blend
    $brush.WrapMode = [System.Drawing.Drawing2D.WrapMode]::TileFlipX
    return ,$brush
}
function Add-ShadowLines([System.Drawing.Drawing2D.GraphicsPath]$path, [int]$row, [int]$column) {
    # The double-line shadow of the "ANSI Shadow" figlet font: an empty cell looks at the blocks
    # to its left, above it and diagonally up-left, and gets one of the six box-drawing shapes.
    if (Test-Block $row $column) { return }
    $left = Test-Block $row ($column - 1); $up = Test-Block ($row - 1) $column; $diagonal = Test-Block ($row - 1) ($column - 1)
    $x = $column * $cellWidth; $y = $row * $cellHeight
    $near = $cellWidth / 2 - 1.7; $far = $cellWidth / 2 + 1.7; $high = $cellHeight / 2 - 2.4; $low = $cellHeight / 2 + 2.4
    $lines = if ($left -and $up) { @(@($cellWidth,$high, $near,$high, $near,$cellHeight), @($cellWidth,$low, $far,$low, $far,$cellHeight)) }
        elseif ($left -and $diagonal) { @(@($near,0, $near,$cellHeight), @($far,0, $far,$cellHeight)) }
        elseif ($left) { @(@(0,$high, $far,$high, $far,$cellHeight), @(0,$low, $near,$low, $near,$cellHeight)) }
        elseif ($up -and $diagonal) { @(@(0,$high, $cellWidth,$high), @(0,$low, $cellWidth,$low)) }
        elseif ($up) { @(@($cellWidth,$low, $near,$low, $near,0), @($cellWidth,$high, $far,$high, $far,0)) }
        elseif ($diagonal) { @(@(0,$low, $far,$low, $far,0), @(0,$high, $near,$high, $near,0)) }
        else { @() }
    foreach ($line in $lines) {
        $points = for ($i = 0; $i -lt $line.Count; $i += 2) { [System.Drawing.PointF]::new($x + $line[$i], $y + $line[$i + 1]) }
        $path.StartFigure(); $path.AddLines([System.Drawing.PointF[]]$points)
    }
}
function New-LogoBitmap([int]$size) {
    $supersample = 4
    $canvas = New-Object System.Drawing.Bitmap ($size*$supersample),($size*$supersample)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $g.ScaleTransform(($size*$supersample/256.0),($size*$supersample/256.0))
    # Large icons show the mark with its shadow. Small ones drop the shadow and enlarge the mark,
    # so each cell covers whole pixels at 16, 32 and 48 px; the smallest also fill the whole tile.
    $shadow = $size -ge 128
    $markSize = if ($shadow) { 132 } elseif ($size -ge 64) { 160 } else { 192 }
    $inset = if ($size -le 24) { 0 } else { 12 }
    $outer = New-RoundedPath $inset $inset (256 - 2*$inset) (256 - 2*$inset) $(if ($inset) { 58 } else { 60 })
    $tile = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#24252A'))
    $paint = New-MarkBrush 255
    $blocks = New-Object System.Drawing.Drawing2D.GraphicsPath ([System.Drawing.Drawing2D.FillMode]::Winding)
    $lines = New-Object System.Drawing.Drawing2D.GraphicsPath
    $linePaint = New-MarkBrush 204
    $pen = New-Object System.Drawing.Pen $linePaint, ([single]1.3)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Square; $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Square
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Miter
    try {
        $g.FillPath($tile,$outer)
        # With its shadow the mark sits a little up and to the left, so the pair is centred on the tile.
        $origin = if ($shadow) { [System.Drawing.PointF]::new(59, 57) } else { [System.Drawing.PointF]::new((256 - $markSize) / 2, (256 - $markSize) / 2) }
        $g.TranslateTransform($origin.X, $origin.Y)
        $g.ScaleTransform(($markSize / ($mask[0].Length * $cellWidth)), ($markSize / ($mask.Count * $cellHeight)))
        for ($row = 0; $row -le $mask.Count; $row++) {
            for ($column = 0; $column -le $mask[0].Length; $column++) {
                if (Test-Block $row $column) { $blocks.AddRectangle([System.Drawing.RectangleF]::new($column * $cellWidth, $row * $cellHeight, $cellWidth, $cellHeight)) }
                elseif ($shadow) { Add-ShadowLines $lines $row $column }
            }
        }
        $g.FillPath($paint,$blocks)
        if ($shadow) { $g.DrawPath($pen,$lines) }
    } finally { $pen.Dispose(); $linePaint.Dispose(); $lines.Dispose(); $blocks.Dispose(); $paint.Dispose(); $tile.Dispose(); $outer.Dispose(); $g.Dispose() }
    $result = New-Object System.Drawing.Bitmap $size,$size
    $scaled = [System.Drawing.Graphics]::FromImage($result)
    try {
        $scaled.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $scaled.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $scaled.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $scaled.DrawImage($canvas,0,0,$size,$size)
    } finally { $scaled.Dispose(); $canvas.Dispose() }
    return ,$result
}
$sizes = @(16,20,24,32,40,48,64,128,256)
$frames = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($size in $sizes) {
    $bitmap = New-LogoBitmap $size
    $memory = New-Object IO.MemoryStream
    try {
        $bitmap.Save($memory,[System.Drawing.Imaging.ImageFormat]::Png)
        $frames.Add($memory.ToArray())
        if ($size -eq 256) { $bitmap.Save((Join-Path $ProjectRoot 'assets\windowdock.png'),[System.Drawing.Imaging.ImageFormat]::Png) }
    } finally { $memory.Dispose(); $bitmap.Dispose() }
}
$stream = [IO.File]::Create((Join-Path $ProjectRoot 'app.ico'))
$writer = New-Object IO.BinaryWriter $stream
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + $sizes.Count*16
    for ($i=0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose(); $stream.Dispose() }
Copy-Item -LiteralPath (Join-Path $ProjectRoot 'assets\windowdock.png') -Destination (Join-Path $ProjectRoot 'src\WindowDock.Desktop\Assets\windowdock.png') -Force
& (Join-Path $PSScriptRoot 'Refresh-Icons.ps1') -Paths @((Join-Path $ProjectRoot 'app.ico')) -InvalidateCache
Write-Host 'Built WindowDock icon: 16, 20, 24, 32, 40, 48, 64, 128, 256 px.'
