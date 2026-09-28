Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality= [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $scale = $S / 256.0
    $g.ScaleTransform($scale, $scale)  # ab hier immer im 256er-Koordinatenraum zeichnen

    # ---- Squircle-Hintergrund mit Verlauf ----
    $r = 56
    $rect = New-Object System.Drawing.RectangleF(8, 8, 240, 240)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc(8, 8, $d, $d, 180, 90)
    $path.AddArc(248 - $d, 8, $d, $d, 270, 90)
    $path.AddArc(248 - $d, 248 - $d, $d, $d, 0, 90)
    $path.AddArc(8, 248 - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(20, 20)),
        (New-Object System.Drawing.PointF(236, 236)),
        [System.Drawing.Color]::FromArgb(255, 18, 56, 95),
        [System.Drawing.Color]::FromArgb(255, 5, 9, 16))
    $g.FillPath($grad, $path)

    # ---- Radialer blauer Glow in der Mitte ----
    $glowPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $glowPath.AddEllipse(28, 28, 200, 200)
    $glow = New-Object System.Drawing.Drawing2D.PathGradientBrush($glowPath)
    $glow.CenterPoint    = New-Object System.Drawing.PointF(128, 118)
    $glow.CenterColor    = [System.Drawing.Color]::FromArgb(150, 10, 132, 255)
    $glow.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 10, 132, 255))
    $g.SetClip($path)
    $g.FillPath($glow, $glowPath)

    # ---- Netzwerk-Textur (dezente Knoten + Linien) ----
    $nodes = @(
        @(52,64), @(206,58), @(44,196), @(212,200), @(128,40), @(120,220)
    )
    $linePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(55, 90, 170, 255), 2.0)
    foreach ($n in $nodes) {
        $g.DrawLine($linePen, 128, 128, [float]$n[0], [float]$n[1])
    }
    $nodeBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(120, 120, 200, 255))
    foreach ($n in $nodes) {
        $g.FillEllipse($nodeBrush, [float]($n[0]-4), [float]($n[1]-4), 8, 8)
    }

    $g.ResetClip()
    $g.SetClip($path)

    # ---- Hero: bidirektionale Pfeile (⇄) mit Glow ----
    function New-ArrowPath([float]$sx, [float]$sy, [float]$tx, [float]$ty, [float]$dir) {
        # dir = +1 Spitze rechts, -1 Spitze links
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $p.StartFigure(); $p.AddLine($sx, $sy, $tx, $ty)
        $hw = 26.0   # Länge der Widerhaken
        $hh = 20.0
        $p.StartFigure()
        $p.AddLine($tx - $dir*$hw, $ty - $hh, $tx, $ty)
        $p.AddLine($tx, $ty, $tx - $dir*$hw, $ty + $hh)
        return $p
    }

    $top = New-ArrowPath 62 100 196 100 1
    $bot = New-ArrowPath 194 156 60 156 -1

    # Glow-Schichten (breit, halbtransparent, cyan)
    foreach ($w in @(46.0, 34.0)) {
        $gp = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(60, 34, 211, 238), $w)
        $gp.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $gp.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
        $gp.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $g.DrawPath($gp, $top); $g.DrawPath($gp, $bot)
    }

    # Hauptstrich mit Cyan->Blau-Verlauf
    $strokeBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(40, 90)),
        (New-Object System.Drawing.PointF(210, 170)),
        [System.Drawing.Color]::FromArgb(255, 90, 230, 255),
        [System.Drawing.Color]::FromArgb(255, 10, 132, 255))
    $pen = New-Object System.Drawing.Pen($strokeBrush, 24.0)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPath($pen, $top); $g.DrawPath($pen, $bot)

    # Feiner weißer Glanz oben auf den Strichen
    $hi = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(150, 255, 255, 255), 5.0)
    $hi.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $hi.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($hi, 66, 95, 150, 95)
    $g.DrawLine($hi, 106, 161, 190, 161)

    # Leuchtende Endpunkte
    $end = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 130, 240, 255))
    $g.FillEllipse($end, 62-9, 100-9, 18, 18)
    $g.FillEllipse($end, 194-9, 156-9, 18, 18)

    $g.ResetClip()

    # ---- Oberer Glanz (Glas-Look) ----
    $gloss = New-Object System.Drawing.Drawing2D.GraphicsPath
    $gloss.AddArc(8, 8, $d, $d, 180, 90)
    $gloss.AddArc(248 - $d, 8, $d, $d, 270, 90)
    $gloss.AddLine(240, 96, 16, 128)
    $gloss.CloseFigure()
    $glossBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 8)),
        (New-Object System.Drawing.PointF(0, 128)),
        [System.Drawing.Color]::FromArgb(38, 255, 255, 255),
        [System.Drawing.Color]::FromArgb(0, 255, 255, 255))
    $g.SetClip($path)
    $g.FillPath($glossBrush, $gloss)
    $g.ResetClip()

    # ---- Feine Umrandung ----
    $border = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(90, 120, 190, 255), 1.5)
    $g.DrawPath($border, $path)

    $g.Dispose()
    return $bmp
}

# --- PNGs je Größe erzeugen und als ICO packen ---
$sizes = @(256, 128, 64, 48, 32, 16)
$streams = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $streams += ,($ms.ToArray())
    $bmp.Dispose()
}

$icoPath = Join-Path $PSScriptRoot "..\appicon.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)

$count = $sizes.Count
$bw.Write([UInt16]0)      # reserved
$bw.Write([UInt16]1)      # type = icon
$bw.Write([UInt16]$count) # image count

$offset = 6 + (16 * $count)
for ($i = 0; $i -lt $count; $i++) {
    $s = $sizes[$i]
    $len = $streams[$i].Length
    $bw.Write([Byte]($(if ($s -ge 256) {0} else {$s})))  # width  (0 = 256)
    $bw.Write([Byte]($(if ($s -ge 256) {0} else {$s})))  # height (0 = 256)
    $bw.Write([Byte]0)        # palette
    $bw.Write([Byte]0)        # reserved
    $bw.Write([UInt16]1)      # planes
    $bw.Write([UInt16]32)     # bpp
    $bw.Write([UInt32]$len)   # bytes
    $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($data in $streams) { $bw.Write($data) }
$bw.Flush(); $bw.Close(); $fs.Close()

# Zusätzlich eine 256er-PNG-Vorschau
$prev = New-IconBitmap 256
$prev.Save((Join-Path $PSScriptRoot "icon_preview.png"),[System.Drawing.Imaging.ImageFormat]::Png)
$prev.Dispose()

Write-Output "ICO erstellt: $icoPath  ($((Get-Item $icoPath).Length) Bytes)"
