Add-Type -AssemblyName System.Drawing

$size = 256
$outDir = "D:\Project\C#\2\src\DllTool.App\Assets"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# ---------- 绘制 logo 位图 ----------
$bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.Clear([System.Drawing.Color]::Transparent)

# 圆角方形背景（渐变 靛蓝 -> 紫）
$radius = 56
$rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$d = 2 * $radius
$path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
$path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
$path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
$path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
$path.CloseFigure()

$grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $rect, [System.Drawing.Color]::FromArgb(255, 47, 84, 235), [System.Drawing.Color]::FromArgb(255, 109, 63, 234), 45.0)
$g.FillPath($grad, $path)

# 顶部高光（微妙渐变增强立体感）
$glowRect = New-Object System.Drawing.Rectangle(0, 0, $size, [int]($size * 0.55))
$glow = New-Object System.Drawing.Drawing2D.LinearGradientBrush($glowRect, [System.Drawing.Color]::FromArgb(45, 255, 255, 255), [System.Drawing.Color]::FromArgb(0, 255, 255, 255), 90.0)
$g.FillPath($glow, $path)

# ---------- 图形符号：双向替换箭头（⇄ 意象）----------
# 白色圆环底
$ringRect = New-Object System.Drawing.Rectangle(44, 44, 168, 168)
$ringPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(60, 255, 255, 255), 3)
$g.DrawEllipse($ringPen, $ringRect)

# 白色箭头（左右两条，风格化 DLL 替换）
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 20)
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

# 左箭头：从右侧指向左侧（表示备份/提取）
$g.DrawLine($pen, 170, 90, 86, 90)
# 左箭头头
$headPen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 20)
$headPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$headPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawLine($headPen, 110, 62, 86, 90)
$g.DrawLine($headPen, 86, 90, 110, 118)

# 右箭头：从左侧指向右侧（表示覆盖/更新）
$g.DrawLine($pen, 86, 166, 170, 166)
$g.DrawLine($headPen, 146, 138, 170, 166)
$g.DrawLine($headPen, 170, 166, 146, 194)

# 中部小圆点（DLL 节点）
$dotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
$g.FillEllipse($dotBrush, 118, 118, 20, 20)

$g.Dispose()

# 保存 PNG
$pngPath = Join-Path $outDir "app_logo.png"
$bmp.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "PNG 已保存: $pngPath"

# ---------- 生成多尺寸 ICO ----------
function Draw-IconBitmap([int]$sz) {
    $b = New-Object System.Drawing.Bitmap($sz, $sz, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gr = [System.Drawing.Graphics]::FromImage($b)
    $gr.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $gr.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $scale = $sz / $size
    $gr.ScaleTransform($scale, $scale)
    $gr.DrawImage($bmp, 0, 0, $size, $size)
    $gr.Dispose()
    return $b
}

$sizes = @(16, 32, 48, 64, 128, 256)
$images = @()
foreach ($s in $sizes) { $images += , (Draw-IconBitmap $s) }

# ICO 文件打包
$icoPath = Join-Path $outDir "app.ico"
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)

# ICONDIR
$bw.Write([UInt16]0)           # reserved
$bw.Write([UInt16]1)           # type = icon
$bw.Write([UInt16]$images.Count)  # count

$offset = 6 + 16 * $images.Count
$imageBytes = @()
foreach ($img in $images) {
    $w = $img.Width; if ($w -ge 256) { $w = 0 }
    $h = $img.Height; if ($h -ge 256) { $h = 0 }
    # 转成 32bpp BMP（顶部倒置）
    $bmpBytes = New-Object System.IO.MemoryStream
    $bw2 = New-Object System.IO.BinaryWriter($bmpBytes)
    # BITMAPINFOHEADER (40 bytes)
    $bw2.Write([UInt32]40)
    $bw2.Write([Int32]$img.Width)
    $bw2.Write([Int32]($img.Height * 2))  # 双高度：XOR + AND
    $bw2.Write([UInt16]1)
    $bw2.Write([UInt16]32)
    $bw2.Write([UInt32]0)
    $bw2.Write([UInt32]($img.Width * $img.Height * 4))
    $bw2.Write([Int32]0)
    $bw2.Write([Int32]0)
    $bw2.Write([UInt32]0)
    $bw2.Write([UInt32]0)
    # 像素（底部向上，BGRA）
    for ($y = $img.Height - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $img.Width; $x++) {
            $c = $img.GetPixel($x, $y)
            $bw2.Write([Byte]$c.B)
            $bw2.Write([Byte]$c.G)
            $bw2.Write([Byte]$c.R)
            $bw2.Write([Byte]$c.A)
        }
    }
    $bw2.Flush()
    $data = $bmpBytes.ToArray()
    $imageBytes += , $data
    $bw2.Dispose()
}

for ($i = 0; $i -lt $images.Count; $i++) {
    $img = $images[$i]
    $w = $img.Width; if ($w -ge 256) { $w = 0 }
    $h = $img.Height; if ($h -ge 256) { $h = 0 }
    $data = $imageBytes[$i]
    $bw.Write([Byte]$w)
    $bw.Write([Byte]$h)
    $bw.Write([Byte]0)
    $bw.Write([Byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}

foreach ($data in $imageBytes) { $bw.Write($data) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
Write-Output "ICO 已保存: $icoPath ($([System.IO.File]::ReadAllBytes($icoPath).Length) bytes)"

# 清理
$bw.Dispose(); $ms.Dispose()
foreach ($img in $images) { $img.Dispose() }
$bmp.Dispose()
