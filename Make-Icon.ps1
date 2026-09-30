# Generuje NewsyVE.ico (wielorozmiarowy, zapis PNG w kontenerze ICO)
param([string]$Out = "$PSScriptRoot\NewsyVE.ico")

Add-Type -AssemblyName System.Drawing

function New-Tile([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.Clear([System.Drawing.Color]::Transparent)

    # zaokrąglone tło z gradientem
    $r = [int]($S * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($S - $d, 0, $d, $d, 270, 90)
    $path.AddArc($S - $d, $S - $d, $d, $d, 0, 90)
    $path.AddArc(0, $S - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(0, 0)),
        (New-Object System.Drawing.Point($S, $S)),
        [System.Drawing.Color]::FromArgb(255, 32, 64, 140),
        [System.Drawing.Color]::FromArgb(255, 12, 20, 48))
    $g.FillPath($br, $path)

    # słońce
    $sc = [System.Drawing.Color]::FromArgb(255, 255, 194, 60)
    $sunB = New-Object System.Drawing.SolidBrush($sc)
    $cx = $S * 0.38; $cy = $S * 0.38; $rad = $S * 0.16
    $g.FillEllipse($sunB, [single]($cx - $rad), [single]($cy - $rad), [single]($rad * 2), [single]($rad * 2))
    $pen = New-Object System.Drawing.Pen($sc, [single]($S * 0.045))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    foreach ($a in 0, 45, 90, 135, 180, 225, 270, 315) {
        $t = $a * [Math]::PI / 180
        $x1 = $cx + [Math]::Cos($t) * $rad * 1.45; $y1 = $cy + [Math]::Sin($t) * $rad * 1.45
        $x2 = $cx + [Math]::Cos($t) * $rad * 1.95; $y2 = $cy + [Math]::Sin($t) * $rad * 1.95
        $g.DrawLine($pen, [single]$x1, [single]$y1, [single]$x2, [single]$y2)
    }

    # chmura
    $cb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 240, 246, 255))
    $g.FillEllipse($cb, [single]($S * 0.30), [single]($S * 0.52), [single]($S * 0.30), [single]($S * 0.30))
    $g.FillEllipse($cb, [single]($S * 0.50), [single]($S * 0.46), [single]($S * 0.34), [single]($S * 0.34))
    $g.FillEllipse($cb, [single]($S * 0.22), [single]($S * 0.62), [single]($S * 0.26), [single]($S * 0.22))
    $g.FillRectangle($cb, [single]($S * 0.33), [single]($S * 0.66), [single]($S * 0.40), [single]($S * 0.16))

    # kropla
    $db = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 90, 175, 255))
    $dp = New-Object System.Drawing.Pen($db.Color, [single]($S * 0.06))
    $dp.StartCap = 'Round'; $dp.EndCap = 'Round'
    $g.DrawLine($dp, [single]($S * 0.42), [single]($S * 0.84), [single]($S * 0.38), [single]($S * 0.93))
    $g.DrawLine($dp, [single]($S * 0.62), [single]($S * 0.84), [single]($S * 0.58), [single]($S * 0.93))

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 256, 128, 64, 48, 32, 16
$pngs = @{}
foreach ($s in $sizes) { $pngs[$s] = [byte[]](New-Tile $s) }

$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $bytes = $pngs[$s]
    $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$bytes.Length); $bw.Write([uint32]$offset)
    $offset += $bytes.Length
}
foreach ($s in $sizes) { $bw.Write($pngs[$s]) }
$bw.Flush(); $bw.Close(); $fs.Close()
Write-Output "ICO: $Out ($((Get-Item $Out).Length) B)"
