Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot
$src = [Drawing.Bitmap]::FromFile((Join-Path $root 'assets\logo-source.png'))

$side = [int]($src.Width * 0.86)
$cropX = [int](($src.Width - $side) / 2)
$cropY = [int](($src.Height - $side) / 2)

function RoundedPath([int]$size) {
  $d = [Math]::Round($size * 0.44, 2)
  $path = New-Object Drawing.Drawing2D.GraphicsPath
  $max = $size - $d
  $path.AddArc(0, 0, $d, $d, 180, 90)
  $path.AddArc($max, 0, $d, $d, 270, 90)
  $path.AddArc($max, $max, $d, $d, 0, 90)
  $path.AddArc(0, $max, $d, $d, 90, 90)
  $path.CloseFigure()
  return ,$path
}

function RenderBitmap([int]$size, [bool]$gray) {
  $bmp = New-Object Drawing.Bitmap $size, $size
  $g = [Drawing.Graphics]::FromImage($bmp)
  $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.SmoothingMode = 'AntiAlias'; $g.CompositingQuality = 'HighQuality'
  $attr = New-Object Drawing.Imaging.ImageAttributes
  $attr.SetWrapMode('TileFlipXY')
  if ($gray) {
    $m = New-Object Drawing.Imaging.ColorMatrix
    $m.Matrix00 = 0.3; $m.Matrix01 = 0.3; $m.Matrix02 = 0.3
    $m.Matrix10 = 0.59; $m.Matrix11 = 0.59; $m.Matrix12 = 0.59
    $m.Matrix20 = 0.11; $m.Matrix21 = 0.11; $m.Matrix22 = 0.11
    $m.Matrix33 = 0.7; $m.Matrix44 = 1
    $attr.SetColorMatrix($m)
  }
  $tile = New-Object Drawing.Bitmap $size, $size
  $t = [Drawing.Graphics]::FromImage($tile)
  $t.InterpolationMode = 'HighQualityBicubic'; $t.PixelOffsetMode = 'HighQuality'; $t.CompositingQuality = 'HighQuality'
  $t.DrawImage($src, (New-Object Drawing.Rectangle 0, 0, $size, $size), $cropX, $cropY, $side, $side, 'Pixel', $attr)
  $t.Dispose()
  $brush = New-Object Drawing.TextureBrush $tile
  $path = RoundedPath $size
  $g.FillPath($brush, $path)
  $path.Dispose(); $brush.Dispose(); $tile.Dispose(); $g.Dispose()
  return ,$bmp
}

function Render([int]$size, [bool]$gray) {
  $bmp = RenderBitmap $size $gray
  $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  return ,$ms.ToArray()
}

function WriteIco([string]$path, [int[]]$sizes, [bool]$gray) {
  $pngs = @(); foreach ($s in $sizes) { $pngs += ,(Render $s $gray) }
  $w = New-Object IO.BinaryWriter ([IO.File]::Create($path))
  $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
  $offset = 6 + 16 * $sizes.Count
  for ($i = 0; $i -lt $sizes.Count; $i++) {
    $b = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
  }
  foreach ($p in $pngs) { $w.Write($p) }
  $w.Close()
}

WriteIco (Join-Path $root 'src\app.ico') @(16, 20, 24, 32, 40, 48, 64, 256) $false
WriteIco (Join-Path $root 'src\tray-off.ico') @(16, 20, 24, 32, 40, 48, 64) $true
[IO.File]::WriteAllBytes((Join-Path $root 'src\logo.png'), (Render 128 $false))
[IO.File]::WriteAllBytes((Join-Path $root 'assets\logo.png'), (Render 512 $false))
$src.Dispose()
'icons ok'
