param(
    [string]$Out = "$PSScriptRoot\..\src\Assets\app.ico"
)

# 生成小叮应用图标：品牌绿渐变圆角方块 + 白色对勾 + 右上角赭色提示点
# 多尺寸 PNG 打包为 ICO（16/24/32/48/64/128/256），与 Code/src/UI/Theme.cs 的品牌色同族
Add-Type -AssemblyName System.Drawing

$topColor = [System.Drawing.Color]::FromArgb(255, 58, 141, 114)
$bottomColor = [System.Drawing.Color]::FromArgb(255, 30, 92, 74)
$amber = [System.Drawing.Color]::FromArgb(255, 196, 98, 52)

function New-RoundedPath([System.Drawing.RectangleF]$rect, [single]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $arc = New-Object System.Drawing.RectangleF($rect.X, $rect.Y, $d, $d)
    $path.AddArc($arc, 180, 90)
    $arc.X = $rect.Right - $d
    $path.AddArc($arc, 270, 90)
    $arc.Y = $rect.Bottom - $d
    $path.AddArc($arc, 0, 90)
    $arc.X = $rect.X
    $path.AddArc($arc, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $m = $size * 0.02
    $body = New-Object System.Drawing.RectangleF($m, $m, $size - 2 * $m, $size - 2 * $m)
    $radius = $size * 0.235

    $path = New-RoundedPath $body $radius
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($body, $topColor, $bottomColor, [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillPath($brush, $path)

    if ($size -ge 48) {
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(46, 255, 255, 255), [Math]::Max(1.0, $size * 0.008))
        $g.DrawPath($pen, $path)
        $pen.Dispose()
    }

    # 白色对勾
    $checkPen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [Math]::Max(1.6, $size * 0.115))
    $checkPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $checkPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $checkPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $points = @(
        (New-Object System.Drawing.PointF(($size * 0.275), ($size * 0.535))),
        (New-Object System.Drawing.PointF(($size * 0.425), ($size * 0.685))),
        (New-Object System.Drawing.PointF(($size * 0.70), ($size * 0.36)))
    )
    $g.DrawLines($checkPen, $points)

    # 右上角提示点（16px 省略，保证对勾清晰）
    if ($size -ge 24) {
        $r = $size * 0.086
        $cx = $size * 0.795
        $cy = $size * 0.208
        $halo = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(235, $bottomColor))
        $g.FillEllipse($halo, ($cx - $r * 1.32), ($cy - $r * 1.32), ($r * 2.64), ($r * 2.64))
        $dot = New-Object System.Drawing.SolidBrush($amber)
        $g.FillEllipse($dot, ($cx - $r), ($cy - $r), ($r * 2), ($r * 2))
        $halo.Dispose()
        $dot.Dispose()
    }

    $checkPen.Dispose()
    $brush.Dispose()
    $path.Dispose()
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
foreach ($s in $sizes) { [void]$images.Add((New-IconPng $s)) }

$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
for ($i = 0; $i -lt $images.Count; $i++) {
    $dim = $sizes[$i]
    $bw.Write([byte]$(if ($dim -ge 256) { 0 } else { $dim }))
    $bw.Write([byte]$(if ($dim -ge 256) { 0 } else { $dim }))
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$images[$i].Length)
    $bw.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write([byte[]]$img) }
$bw.Close()
$fs.Close()

Write-Host "输出: $Out (7 frames)"
