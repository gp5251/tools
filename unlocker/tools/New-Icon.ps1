# Generates src/Unlocker/Unlocker.ico: Fluent crimson squircle + white trash
# can whose middle slit is a lightning bolt ("force" + "delete" in one glyph).
# Vector-rendered at each size (no downscale blur), PNG-compressed frames (Vista+).
Add-Type -AssemblyName System.Drawing

$outIco = Join-Path $PSScriptRoot '..\src\Unlocker\Unlocker.ico'
$previewDir = Join-Path $PSScriptRoot 'previews'

function RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc(($x + $w - $d), $y, $d, $d, 270, 90)
    $path.AddArc(($x + $w - $d), ($y + $h - $d), $d, $d, 0, 90)
    $path.AddArc($x, ($y + $h - $d), $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Render([int]$size) {
    $u = $size / 256.0  # design space is 256x256
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    function U([float]$v) { return $v * $u }

    # ---- tile: crimson gradient squircle ----
    $tile = RoundedRect (U 0) (U 0) (U 256) (U 256) (U 56)
    $tileRect = New-Object System.Drawing.RectangleF(0, 0, (U 256), (U 256))
    $tileBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $tileRect,
        [System.Drawing.Color]::FromArgb(244, 89, 58),    # top: warm red
        [System.Drawing.Color]::FromArgb(200, 30, 30),    # bottom: deep crimson
        [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillPath($tileBrush, $tile)

    # top gloss highlight, clipped to the tile
    $g.SetClip($tile)
    $gloss = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(26, 255, 255, 255))
    $g.FillRectangle($gloss, (New-Object System.Drawing.RectangleF(0, 0, (U 256), (U 118))))
    $g.ResetClip()

    # ---- trash can glyph (white) ----
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $handle = RoundedRect (U 110) (U 58) (U 36) (U 18) (U 8)
    $g.FillPath($white, $handle)
    $lid = RoundedRect (U 66) (U 76) (U 124) (U 20) (U 9)
    $g.FillPath($white, $lid)
    $body = RoundedRect (U 80) (U 94) (U 96) (U 114) (U 14)
    $g.FillPath($white, $body)

    # ---- slits punched in tile-red: two plain, middle is a lightning bolt ----
    $hole = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(210, 40, 32))
    if ($size -ge 24) {
        $slitW = [Math]::Max(1.2, (U 9))
        $slit1 = RoundedRect (U 101) (U 112) (U 9) (U 74) (U 4)
        $g.FillPath($hole, $slit1)
        $slit2 = RoundedRect (U 146) (U 112) (U 9) (U 74) (U 4)
        $g.FillPath($hole, $slit2)

        $bolt = New-Object System.Drawing.Drawing2D.GraphicsPath
        $bolt.AddPolygon([System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF((U 134), (U 110))),
            (New-Object System.Drawing.PointF((U 112), (U 146))),
            (New-Object System.Drawing.PointF((U 126), (U 146))),
            (New-Object System.Drawing.PointF((U 118), (U 190))),
            (New-Object System.Drawing.PointF((U 142), (U 148))),
            (New-Object System.Drawing.PointF((U 128), (U 148)))
        ))
        $g.FillPath($hole, $bolt)
    }

    $g.Dispose()
    return $bmp
}

function PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return ,$bytes
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = @()
New-Item -ItemType Directory -Path $previewDir -Force | Out-Null
foreach ($s in $sizes) {
    $bmp = Render $s
    $frames += ,@($s, (PngBytes $bmp))
    $bmp.Save((Join-Path $previewDir "icon_$s.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([uint16]0)                 # reserved
$bw.Write([uint16]1)                 # type: icon
$bw.Write([uint16]$frames.Count)     # frame count
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $s = $f[0]; $png = $f[1]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # width  (0 = 256)
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # height (0 = 256)
    $bw.Write([byte]0)               # palette colors
    $bw.Write([byte]0)               # reserved
    $bw.Write([uint16]1)             # color planes
    $bw.Write([uint16]32)            # bits per pixel
    $bw.Write([uint32]$png.Length)   # payload size
    $bw.Write([uint32]$offset)       # payload offset
    $offset += $png.Length
}
foreach ($f in $frames) { $bw.Write($f[1], 0, $f[1].Length) }
[System.IO.File]::WriteAllBytes($outIco, $ms.ToArray())
$bw.Dispose(); $ms.Dispose()
Write-Output "wrote $outIco ($($frames.Count) frames)"
