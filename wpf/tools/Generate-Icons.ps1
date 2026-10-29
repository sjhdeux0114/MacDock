# Reproducible code-drawn MacDock icons. No external graphics dependencies.
Add-Type -AssemblyName System.Drawing
$assetDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../Assets'))
function Rounded-Rect($graphics, $brush, [float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x,$y,$d,$d,180,90)
    $p.AddArc(($x+$w-$d),$y,$d,$d,270,90)
    $p.AddArc(($x+$w-$d),($y+$h-$d),$d,$d,0,90)
    $p.AddArc($x,($y+$h-$d),$d,$d,90,90)
    $p.CloseFigure()
    $graphics.FillPath($brush,$p)
    $p.Dispose()
}
function Draw-Icon([int]$size, [bool]$tray) {
    $factor = 4
    $large = New-Object Drawing.Bitmap ($size*$factor),($size*$factor)
    $g = [Drawing.Graphics]::FromImage($large)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform(($size*$factor/256.0),($size*$factor/256.0))
    $edge = New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#536E7C'))
    $navy = New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#182735'))
    $mint = New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#A5E8D7'))
    $side = New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#64A9B5'))
    $white = New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#ECFFF8'))
    Rounded-Rect $g $edge 8 8 240 240 54
    Rounded-Rect $g $navy 12 12 232 232 50
    if ($tray) {
        Rounded-Rect $g $mint 40 98 44 78 12
        Rounded-Rect $g $white 102 50 52 126 14
        Rounded-Rect $g $mint 172 98 44 78 12
        Rounded-Rect $g $white 40 194 176 18 9
    } else {
        Rounded-Rect $g $side 40 112 44 64 13
        Rounded-Rect $g $mint 98 60 60 116 17
        Rounded-Rect $g $side 172 112 44 64 13
        Rounded-Rect $g $white 40 194 176 12 6
        Rounded-Rect $g $white 112 76 32 6 3
    }
    $g.Dispose()
    foreach ($brush in @($edge,$navy,$mint,$side,$white)) { $brush.Dispose() }
    $bitmap = New-Object Drawing.Bitmap $size,$size
    $scaled = [Drawing.Graphics]::FromImage($bitmap)
    $scaled.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $scaled.DrawImage($large,0,0,$size,$size)
    $scaled.Dispose(); $large.Dispose()
    return $bitmap
}
function Write-Icon([string]$name, [int[]]$sizes, [bool]$tray) {
    $frames = @()
    foreach ($size in $sizes) {
        $bitmap = Draw-Icon $size $tray
        $stream = New-Object IO.MemoryStream
        $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
        $frames += ,($stream.ToArray())
        if ($size -eq 256 -or ($tray -and $size -eq 32)) { $bitmap.Save((Join-Path $assetDirectory ($name+'.png')),[Drawing.Imaging.ImageFormat]::Png) }
        $stream.Dispose(); $bitmap.Dispose()
    }
    $file = [IO.File]::Create((Join-Path $assetDirectory ($name+'.ico')))
    $writer = New-Object IO.BinaryWriter $file
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Length)
    $offset = 6 + 16*$sizes.Length
    for ($i=0; $i -lt $sizes.Length; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    $writer.Dispose()
}
Write-Icon 'macdock' @(16,20,24,32,40,48,64,128,256) $false
Write-Icon 'macdock-tray' @(16,20,24,32,40,48,64) $true
