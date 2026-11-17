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
function New-LogoBitmap([int]$size) {
    $supersample = 4
    $canvas = New-Object System.Drawing.Bitmap ($size*$supersample),($size*$supersample)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform(($size*$supersample/256.0),($size*$supersample/256.0))
    $outer = New-RoundedPath 12 12 232 232 58
    $tile = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#24252A'))
    $weight = if ($size -le 24) { 23 } else { 21 }
    $ink = New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml('#F3F1EB')),$weight
    $joinFill = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#F3F1EB'))
    $ink.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $ink.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $ink.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    try {
        $g.FillPath($tile,$outer)
        for ($turn = 0; $turn -lt 4; $turn++) {
            $curve = New-Object System.Drawing.Drawing2D.GraphicsPath
            $curve.AddLine(82,128,82,94)
            $curve.AddBezier(82,94,82,74,98,58,118,58)
            $curve.AddLine(118,58,140,58)
            $curve.AddBezier(140,58,160,58,176,74,176,94)
            $curve.AddLine(176,94,176,110)
            $rotation = New-Object System.Drawing.Drawing2D.Matrix
            try { $rotation.RotateAt((90*$turn),[System.Drawing.PointF]::new(128,128)); $curve.Transform($rotation); $g.DrawPath($ink,$curve) }
            finally { $rotation.Dispose(); $curve.Dispose() }
        }
        for ($turn = 0; $turn -lt 4; $turn++) {
            $state = $g.Save(); $g.TranslateTransform(128,128); $g.RotateTransform(90*$turn); $g.TranslateTransform(-128,-128)
            $join = New-RoundedPath 100 58 56 28 12
            try { $g.FillPath($joinFill,$join) } finally { $join.Dispose(); $g.Restore($state) }
        }
    } finally { $joinFill.Dispose(); $ink.Dispose(); $tile.Dispose(); $outer.Dispose(); $g.Dispose() }
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