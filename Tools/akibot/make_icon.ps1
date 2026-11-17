# Génère l'icône originale d'Akibot (aucune image du jeu) : carré arrondi dégradé espace, « A » blanc, étoile orange.
# Sorties : AstroHybrid.App\Resources\akibot.png (256 px) et akibot.ico (256/64/48/32/16).
# Usage : powershell -NoProfile -ExecutionPolicy Bypass -File Tools\akibot\make_icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$res = Join-Path $root 'AstroHybrid.App\Resources'

function Draw-Icon([int]$n) {
    $bmp = New-Object Drawing.Bitmap $n, $n
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.InterpolationMode = 'HighQualityBicubic'
    $r = [single]($n * 0.22); $d = 2 * $r
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($n - $d - 1, 0, $d, $d, 270, 90)
    $path.AddArc($n - $d - 1, $n - $d - 1, $d, $d, 0, 90); $path.AddArc(0, $n - $d - 1, $d, $d, 90, 90); $path.CloseFigure()
    $brush = New-Object Drawing.Drawing2D.LinearGradientBrush ([Drawing.Point]::new(0, 0)), ([Drawing.Point]::new($n, $n)),
        ([Drawing.Color]::FromArgb(255, 18, 24, 74)), ([Drawing.Color]::FromArgb(255, 92, 54, 214))
    $g.FillPath($brush, $path)
    # petites étoiles de fond
    $dots = @(@(0.18, 0.22, 0.020), @(0.30, 0.78, 0.014), @(0.82, 0.70, 0.018), @(0.12, 0.55, 0.012), @(0.62, 0.15, 0.012))
    foreach ($s in $dots) { $sz = [single]($n * $s[2]); $g.FillEllipse([Drawing.Brushes]::White, [single]($n * $s[0]), [single]($n * $s[1]), $sz, $sz) }
    # lettre A
    $font = New-Object Drawing.Font 'Segoe UI Black', ([single]($n * 0.58)), ([Drawing.FontStyle]::Bold), ([Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object Drawing.StringFormat; $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $g.DrawString('A', $font, [Drawing.Brushes]::White, (New-Object Drawing.RectangleF 0, ([single]($n * 0.04)), $n, $n), $fmt)
    # étoile orange à 5 branches en haut à droite
    $cx = $n * 0.76; $cy = $n * 0.25; $ro = $n * 0.13; $ri = $ro * 0.45
    $pts = for ($i = 0; $i -lt 10; $i++) { $rad = if ($i % 2) { $ri } else { $ro }; $a = [Math]::PI * (-0.5 + $i / 5)
        New-Object Drawing.PointF ([single]($cx + $rad * [Math]::Cos($a))), ([single]($cy + $rad * [Math]::Sin($a))) }
    $g.FillPolygon((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 255, 159, 28))), $pts)
    $g.Dispose()
    $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    return ,$ms.ToArray()
}

[IO.File]::WriteAllBytes((Join-Path $res 'akibot.png'), (Draw-Icon 256))
$sizes = @(256, 64, 48, 32, 16)
$pngs = @($sizes | ForEach-Object { ,(Draw-Icon $_) })
$ico = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $ico
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $n = $sizes[$i]; $b = if ($n -ge 256) { 0 } else { $n }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush(); [IO.File]::WriteAllBytes((Join-Path $res 'akibot.ico'), $ico.ToArray())
"icon written to $res"
