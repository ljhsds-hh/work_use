param(
    [string]$Out = "$PSScriptRoot\..\src\QuietRemind\Assets\app.ico"
)

# 生成 QuietRemind 应用图标：蓝底白色时钟（圆 + 12 点时针 / 3 点分针），多尺寸 PNG 打包为 ICO
Add-Type -AssemblyName System.Drawing

function New-ClockPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $bg = [System.Drawing.Color]::FromArgb(255, 52, 120, 246)
    $fg = [System.Drawing.Color]::White
    $m = [int]($size * 0.08)

    $bgBrush = New-Object System.Drawing.SolidBrush($bg)
    $g.FillEllipse($bgBrush, $m, $m, $size - 2 * $m, $size - 2 * $m)

    $ringPen = New-Object System.Drawing.Pen($fg, [Math]::Max(1.0, $size * 0.06))
    $ringPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $ringPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawEllipse($ringPen, $m, $m, $size - 2 * $m, $size - 2 * $m)

    $cx = $size / 2.0
    $cy = $size / 2.0
    $w = [Math]::Max(1.0, $size * 0.09)
    $hourPen = New-Object System.Drawing.Pen($fg, $w)
    $minPen = New-Object System.Drawing.Pen($fg, $w)
    $hourPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $hourPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $minPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $minPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($hourPen, $cx, $cy, $cx, $size * 0.30)
    $g.DrawLine($minPen, $cx, $cy, $size * 0.72, $cy)

    $dotBrush = New-Object System.Drawing.SolidBrush($fg)
    $g.FillEllipse($dotBrush, $cx - $w, $cy - $w, 2 * $w, 2 * $w)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $data = $ms.ToArray()
    $ms.Dispose()
    return ,[byte[]]$data
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = New-Object System.Collections.ArrayList
foreach ($s in $sizes) { [void]$images.Add((New-ClockPng $s)) }

$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
for ($i = 0; $i -lt $images.Count; $i++) {
    $s = $sizes[$i]
    $data = $images[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $images) { $bw.Write([byte[]]$data) }
$bw.Close()
$fs.Close()

Write-Host "OK: $Out ($($images.Count) sizes)"
