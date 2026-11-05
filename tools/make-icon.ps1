# Genera src\Velox\Assets\Velox.ico (16-256 px) dibujando el logotipo con System.Drawing.
# Uso: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\src\Velox\Assets\Velox.ico'
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null

function New-LogoPng([int]$size) {
    $bmp = New-Object Drawing.Bitmap $size, $size
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([Drawing.Color]::Transparent)
    $s = [single]$size
    # Fondo: cuadrado redondeado con degradado azul -> violeta
    $r = $s * 0.22
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $r * 2, $r * 2, 180, 90)
    $path.AddArc($s - $r * 2 - 1, 0, $r * 2, $r * 2, 270, 90)
    $path.AddArc($s - $r * 2 - 1, $s - $r * 2 - 1, $r * 2, $r * 2, 0, 90)
    $path.AddArc(0, $s - $r * 2 - 1, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()
    $brush = New-Object Drawing.Drawing2D.LinearGradientBrush ([Drawing.PointF]::new(0, 0)), ([Drawing.PointF]::new($s, $s)), ([Drawing.Color]::FromArgb(255, 61, 127, 255)), ([Drawing.Color]::FromArgb(255, 139, 92, 246))
    $g.FillPath($brush, $path)
    # «V» con forma de rayo
    $v = New-Object Drawing.Drawing2D.GraphicsPath
    $pts = @(
        [Drawing.PointF]::new($s * 0.20, $s * 0.24), [Drawing.PointF]::new($s * 0.38, $s * 0.24),
        [Drawing.PointF]::new($s * 0.52, $s * 0.60), [Drawing.PointF]::new($s * 0.66, $s * 0.24),
        [Drawing.PointF]::new($s * 0.82, $s * 0.24), [Drawing.PointF]::new($s * 0.60, $s * 0.78),
        [Drawing.PointF]::new($s * 0.44, $s * 0.78)
    )
    $v.AddPolygon($pts)
    $g.FillPath([Drawing.Brushes]::White, $v)
    # Líneas de velocidad
    if ($size -ge 32) {
        $pen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(150, 255, 255, 255)), ([single]([math]::Max(1, $s * 0.045)))
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
        $g.DrawLine($pen, $s * 0.12, $s * 0.40, $s * 0.27, $s * 0.40)
        $g.DrawLine($pen, $s * 0.16, $s * 0.52, $s * 0.31, $s * 0.52)
    }
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($sz in $sizes) { , (New-LogoPng $sz) }
$fs = [IO.File]::Create($out)
$bw = New-Object IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $data = $images[$i]
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $images) { $bw.Write([byte[]]$data) }
$bw.Close()
"Icono creado: $out"
# Vista previa en PNG para el README
$png = Join-Path $PSScriptRoot '..\docs\logo.png'
New-Item -ItemType Directory -Force -Path (Split-Path $png) | Out-Null
[IO.File]::WriteAllBytes($png, (New-LogoPng 256))
