<#
  Rigenera le immagini del marchio a partire dal logo vettoriale definito qui sotto:
    src\OpSecBar\Assets\opsecbar.ico     icona dell'app (16-256 px)
    installer\images\logo-*.png          logo nell'angolo delle pagine del programma di installazione
    installer\images\wizard-*.png        banner delle pagine di benvenuto e di fine installazione
  Uso:  .\scripts\make-branding.ps1
#>
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$images = Join-Path $root "installer\images"
$AppName = "Open TaskBar"

function Color([string]$hex, [int]$a = 255) {
    $c = [System.Drawing.ColorTranslator]::FromHtml($hex)
    [System.Drawing.Color]::FromArgb($a, $c.R, $c.G, $c.B)
}
$Tile = Color "#1C1D22"
$White = Color "#F4F5F7"

function NewCanvas([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)
    @{ Bmp = $bmp; G = $g }
}

function RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($r * 2, [Math]::Min($w, $h))
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

# Il logo: una staffa a sinistra e un riquadro in prospettiva a destra. Coordinate in unità proprie
# (larghezza 341, altezza 504, origine al centro), y verso il basso.
$LogoW = 341.0; $LogoH = 504.0
function Poly($path, [double[]]$pts, $cx, $cy, $k) {
    $list = New-Object 'System.Drawing.PointF[]' ($pts.Length / 2)
    for ($i = 0; $i -lt $list.Length; $i++) {
        $list[$i] = New-Object System.Drawing.PointF ([single]($cx + ($pts[2 * $i] - $LogoW / 2) * $k)), ([single]($cy + $pts[2 * $i + 1] * $k))
    }
    $path.AddPolygon($list)
}
function DrawLogo($g, [double]$cx, [double]$cy, [double]$height, $color) {
    $k = $height / $LogoH
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath ([System.Drawing.Drawing2D.FillMode]::Alternate)
    Poly $p @(93,-178, 0,-130, 0,130, 93,178, 93,124, 55,106, 55,-106, 93,-124) $cx $cy $k   # staffa
    Poly $p @(131,-252, 341,-140, 341,140, 131,252) $cx $cy $k                              # riquadro
    Poly $p @(191,-157, 285,-110, 285,110, 191,157) $cx $cy $k                              # il suo foro
    $b = New-Object System.Drawing.SolidBrush $color
    $g.FillPath($b, $p)
    $b.Dispose(); $p.Dispose()
}

# Tessera scura con il logo bianco: resta leggibile su sfondi chiari e scuri.
function DrawTile($g, [double]$x, [double]$y, [double]$size, [double]$logoScale = 0.6) {
    $path = RoundRect $x $y $size $size ($size * 0.235)
    $b = New-Object System.Drawing.SolidBrush $Tile
    $g.FillPath($b, $path)
    $b.Dispose(); $path.Dispose()
    DrawLogo $g ($x + $size / 2) ($y + $size / 2) ($size * $logoScale) $White
}

function PngBytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    , $ms.ToArray()
}

# ---- Icona dell'app --------------------------------------------------------------------------
$frames = foreach ($size in 16, 24, 32, 48, 64, 256) {
    $c = NewCanvas $size $size
    # Alle misure piccole la tessera riempie tutto il riquadro e il logo è più grande, per restare riconoscibile.
    if ($size -le 24) { DrawTile $c.G 0 0 $size 0.72 }
    else { $m = $size * 0.04; DrawTile $c.G $m $m ($size - 2 * $m) 0.62 }
    $c.G.Dispose()
    @{ Size = $size; Data = (PngBytes $c.Bmp) }
    $c.Bmp.Dispose()
}
$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ico
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $w.Write([byte]($f.Size % 256)); $w.Write([byte]($f.Size % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$f.Data.Length); $w.Write([uint32]$offset)
    $offset += $f.Data.Length
}
foreach ($f in $frames) { $w.Write([byte[]]$f.Data) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $root "src\OpSecBar\Assets\opsecbar.ico"), $ico.ToArray())

# ---- Logo del programma di installazione ------------------------------------------------------
foreach ($size in 58, 77, 97, 116, 124, 159) {
    $c = NewCanvas $size $size
    $m = $size * 0.06
    DrawTile $c.G $m $m ($size - 2 * $m)
    $c.G.Dispose()
    $c.Bmp.Save((Join-Path $images "logo-$size.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $c.Bmp.Dispose()
}

# ---- Banner del programma di installazione (disegnato su una base larga 403 px) ------------------
foreach ($size in @(@(202, 386), @(269, 515), @(336, 643), @(403, 772), @(430, 824), @(534, 1022))) {
    $W = $size[0]; $H = $size[1]; $s = $W / 403.0
    $c = NewCanvas $W $H
    $g = $c.G

    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point 0, $H), (Color "#1B1C22"), (Color "#121317")
    $g.FillRectangle($bg, 0, 0, $W, $H); $bg.Dispose()

    # Alone bluastro dietro il logo.
    $glowPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $glowPath.AddEllipse([single](-140 * $s), [single](-230 * $s), [single](640 * $s), [single](640 * $s))
    $glow = New-Object System.Drawing.Drawing2D.PathGradientBrush $glowPath
    $glow.CenterColor = Color "#33405F" 235
    $glow.SurroundColors = @((Color "#1B1C22" 0))
    $g.FillPath($glow, $glowPath); $glow.Dispose(); $glowPath.Dispose()

    DrawTile $g (48 * $s) (76 * $s) (128 * $s)

    # Titolo: il più grande che sta nella larghezza disponibile.
    $fmt = [System.Drawing.StringFormat]::GenericTypographic
    $titlePx = 46.0 * $s
    do {
        $font = New-Object System.Drawing.Font "Segoe UI Semibold", ([single]$titlePx), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
        $fits = $g.MeasureString($AppName, $font, 9999, $fmt).Width -le ($W - 86 * $s)
        if (-not $fits) { $font.Dispose(); $titlePx -= 0.5 }
    } until ($fits)
    $textBrush = New-Object System.Drawing.SolidBrush (Color "#FFFFFF")
    $g.DrawString($AppName, $font, $textBrush, [single](47 * $s), [single](246 * $s), $fmt); $font.Dispose()

    $font = New-Object System.Drawing.Font "Segoe UI", ([single](26 * $s)), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $dim = New-Object System.Drawing.SolidBrush (Color "#A3A7B3")
    $g.DrawString("Your taskbar,", $font, $dim, [single](49 * $s), [single](318 * $s), $fmt)
    $g.DrawString("your way.", $font, $dim, [single](49 * $s), [single](357 * $s), $fmt)
    $font.Dispose(); $dim.Dispose()

    # Una barra in miniatura in fondo.
    $bar = RoundRect (32 * $s) (636 * $s) (339 * $s) (59 * $s) (16 * $s)
    $b = New-Object System.Drawing.SolidBrush (Color "#262830"); $g.FillPath($b, $bar); $b.Dispose()
    $pen = New-Object System.Drawing.Pen (Color "#353842"), ([single][Math]::Max(1, 1.2 * $s)); $g.DrawPath($pen, $bar); $pen.Dispose(); $bar.Dispose()
    DrawLogo $g (65 * $s) (665.5 * $s) (27 * $s) $White
    $x = 100
    foreach ($hex in "#5B8DEF", "#F5A73B", "#4CC28E", "#EC6A8C") {
        $sq = RoundRect ($x * $s) (652 * $s) (28 * $s) (28 * $s) (7.5 * $s)
        $b = New-Object System.Drawing.SolidBrush (Color $hex); $g.FillPath($b, $sq); $b.Dispose(); $sq.Dispose()
        $x += 40
    }
    $dot = RoundRect (106 * $s) (686 * $s) (16 * $s) (4 * $s) (2 * $s)
    $b = New-Object System.Drawing.SolidBrush (Color "#7C9CFF"); $g.FillPath($b, $dot); $b.Dispose(); $dot.Dispose()
    $font = New-Object System.Drawing.Font "Segoe UI Semibold", ([single](19 * $s)), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $tw = $g.MeasureString("12:00", $font, 9999, $fmt).Width
    $g.DrawString("12:00", $font, $textBrush, [single](351 * $s - $tw), [single](653 * $s), $fmt)
    $font.Dispose(); $textBrush.Dispose()

    $g.Dispose()
    # Senza canale alfa, come si aspetta il programma di installazione per il banner.
    $flat = $c.Bmp.Clone((New-Object System.Drawing.Rectangle 0, 0, $W, $H), [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $flat.Save((Join-Path $images "wizard-$W.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $flat.Dispose(); $c.Bmp.Dispose()
}
Write-Host "Immagini rigenerate." -ForegroundColor Green
