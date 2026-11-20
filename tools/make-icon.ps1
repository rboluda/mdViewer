# Genera src/MdViewer.App/Assets/mdViewer.ico a partir de icon.png (raíz del repo).
# Recorta un cuadrado centrado en el logotipo y crea una imagen por tamaño (PNG dentro del ICO).
# Uso: pwsh tools/make-icon.ps1
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\icon.png'),
    [string]$Output = (Join-Path $PSScriptRoot '..\src\MdViewer.App\Assets\mdViewer.ico'),
    [int]$CropSize = 940,
    [int]$CenterX = 775,
    [int]$CenterY = 510
)

Add-Type -AssemblyName System.Drawing
$sizes = 16, 24, 32, 48, 64, 128, 256

$src = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Source))
try {
    $crop = [System.Drawing.Rectangle]::new($CenterX - $CropSize / 2, $CenterY - $CropSize / 2, $CropSize, $CropSize)
    $frames = foreach ($size in $sizes) {
        $bmp = [System.Drawing.Bitmap]::new($size, $size)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        # Fondo blanco con esquinas redondeadas (estilo icono de Windows 11)
        $radius = [Math]::Max(2, [int]($size * 0.18))
        $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $d = $radius * 2
        $path.AddArc(0, 0, $d, $d, 180, 90)
        $path.AddArc($size - $d - 1, 0, $d, $d, 270, 90)
        $path.AddArc($size - $d - 1, $size - $d - 1, $d, $d, 0, 90)
        $path.AddArc(0, $size - $d - 1, $d, $d, 90, 90)
        $path.CloseFigure()
        $g.FillPath([System.Drawing.Brushes]::White, $path)
        $path.Dispose()
        $g.DrawImage($src, [System.Drawing.Rectangle]::new(0, 0, $size, $size), $crop, [System.Drawing.GraphicsUnit]::Pixel)
        $g.Dispose()
        $ms = [System.IO.MemoryStream]::new()
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        [pscustomobject]@{ Size = $size; Bytes = $ms.ToArray() }
    }
}
finally {
    $src.Dispose()
}

New-Item -ItemType Directory -Force (Split-Path $Output) | Out-Null
$out = [System.IO.File]::Create($Output)
$w = [System.IO.BinaryWriter]::new($out)
try {
    # ICONDIR
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($f in $frames) {
        # ICONDIRENTRY (0 = 256 px)
        $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32)
        $w.Write([uint32]$f.Bytes.Length); $w.Write([uint32]$offset)
        $offset += $f.Bytes.Length
    }
    foreach ($f in $frames) { $w.Write($f.Bytes) }
}
finally {
    $w.Dispose()
}

Write-Host "Icono generado: $Output"
