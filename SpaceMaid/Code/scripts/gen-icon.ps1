# SpaceMaid 应用图标生成器（程序化生成，勿手工编辑 app.ico）
#
# 设计：圆角方块（蓝色对角渐变）+ 白色缺口圆环（读作 C，代表 C 盘）+ 缺口处一个小星点（焕新）。
# 为什么程序化生成：与仓库其他工具（CodeMemo / QuietRemind / DshLauncher）保持一致，
# 图标可随设计意图改参数重新生成，不依赖外部素材。
#
# 用法（本机执行策略禁止直接运行 .ps1）：
#   Invoke-Expression (Get-Content -Raw -Encoding UTF8 'Code\scripts\gen-icon.ps1')
#
# 两个必须注意的坑：
# 1. ICO 里的图必须是**经典 DIB**（BITMAPINFOHEADER + 自下而上的 BGRA + AND 掩码）。
#    用 PNG 负载虽然 Vista+ 的资源管理器认，但 System.Drawing.Icon / ToBitmap() 会抛
#    "Requested range extends past the end of the array."，导致无法程序化验证图标内容。
# 2. New-Object 的参数里不要写内联表达式（PowerShell 会先求值导致参数错位），先赋值再传。

Add-Type -AssemblyName System.Drawing

# 定位 Assets 目录。
# 为什么不用 $MyInvocation.MyCommand.Path / $PSCommandPath：本仓库执行策略禁止直接运行 .ps1，
# 实际用法是 Invoke-Expression (Get-Content -Raw ...)，此时这两个变量都是 null。
# 因此从当前目录向上逐级探测，兼容在仓库根 / SpaceMaid / Code 目录下运行几种姿势。
function Resolve-AssetsDirectory {
    $cursor = (Get-Location).Path
    for ($depth = 0; $depth -lt 6; $depth++) {
        foreach ($relative in @('Code\src\SpaceMaid.App\Assets', 'src\SpaceMaid.App\Assets', 'SpaceMaid\Code\src\SpaceMaid.App\Assets')) {
            $candidate = Join-Path $cursor $relative
            $projectDirectory = Split-Path -Parent $candidate
            if (Test-Path $projectDirectory) {
                return [System.IO.Path]::GetFullPath($candidate)
            }
        }

        $parent = Split-Path -Parent $cursor
        if (-not $parent -or $parent -eq $cursor) {
            break
        }

        $cursor = $parent
    }

    throw '找不到 SpaceMaid.App\Assets 目录：请在仓库根目录（或 SpaceMaid / Code 目录）下运行本脚本。'
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # —— 圆角方块 + 对角渐变背景 ——
    $radius = [Math]::Max(2.0, [double]$size * 0.22)
    $diameter = $radius * 2.0

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $diameter, $diameter, 180, 90)
    $path.AddArc($size - $diameter, 0, $diameter, $diameter, 270, 90)
    $path.AddArc($size - $diameter, $size - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc(0, $size - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()

    $startPoint = New-Object System.Drawing.Point(0, 0)
    $endPoint = New-Object System.Drawing.Point($size, $size)
    $colorFrom = [System.Drawing.Color]::FromArgb(255, 70, 146, 238)
    $colorTo = [System.Drawing.Color]::FromArgb(255, 32, 92, 180)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($startPoint, $endPoint, $colorFrom, $colorTo)
    $g.FillPath($brush, $path)

    # —— 白色缺口圆环（读作 C）——
    $inset = [double]$size * 0.24
    $ringBox = [double]$size - ($inset * 2.0)
    $ringWidth = [float]([Math]::Max(1.2, [double]$size * 0.145))
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $ringWidth)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($pen, [float]$inset, [float]$inset, [float]$ringBox, [float]$ringBox, 45, 270)

    # —— 缺口处的小星点（焕新）——
    $dotSize = [Math]::Max(1.5, [double]$size * 0.13)
    $dotX = [float]([double]$size * 0.70)
    $dotY = [float]([double]$size * 0.16)
    $g.FillEllipse([System.Drawing.Brushes]::White, $dotX, $dotY, [float]$dotSize, [float]$dotSize)

    $pen.Dispose()
    $brush.Dispose()
    $path.Dispose()
    $g.Dispose()
    return $bmp
}

# 把位图序列化成 ICO 用的经典 DIB：BITMAPINFOHEADER(40) + BGRA 行（自下而上）+ AND 掩码（全 0）
function Get-IconDibBytes([System.Drawing.Bitmap]$bitmap) {
    $width = $bitmap.Width
    $height = $bitmap.Height
    $xorSize = $width * 4 * $height
    $andStride = [int]([Math]::Ceiling($width / 32.0) * 4)
    $andSize = $andStride * $height

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)

    $writer.Write([UInt32]40)                 # biSize
    $writer.Write([Int32]$width)              # biWidth
    $writer.Write([Int32]($height * 2))       # biHeight：DIB 里包含 XOR + AND 两张图
    $writer.Write([UInt16]1)                  # biPlanes
    $writer.Write([UInt16]32)                 # biBitCount
    $writer.Write([UInt32]0)                  # biCompression = BI_RGB
    $writer.Write([UInt32]($xorSize + $andSize))
    $writer.Write([Int32]0)                   # biXPelsPerMeter
    $writer.Write([Int32]0)                   # biYPelsPerMeter
    $writer.Write([UInt32]0)                  # biClrUsed
    $writer.Write([UInt32]0)                  # biClrImportant

    for ($y = $height - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $width; $x++) {
            $pixel = $bitmap.GetPixel($x, $y)
            $writer.Write([Byte]$pixel.B)
            $writer.Write([Byte]$pixel.G)
            $writer.Write([Byte]$pixel.R)
            $writer.Write([Byte]$pixel.A)
        }
    }

    $andMask = New-Object byte[] $andSize
    $writer.Write($andMask)

    $writer.Flush()
    $result = $stream.ToArray()
    $writer.Dispose()
    $stream.Dispose()
    return $result
}

$assetsDir = Resolve-AssetsDirectory
New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null
$icoPath = Join-Path $assetsDir 'app.ico'

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

# 用强类型 List[byte[]]：PowerShell 里 `$a += @(bytes)` 会把字节数组**拆散成单个字节**再拼接，
# 后果是每个图标目录项的长度变成 1、整个 ico 只有几百字节，系统会直接判定"这不是一个图标"。
$payloads = New-Object 'System.Collections.Generic.List[byte[]]'

foreach ($size in $sizes) {
    $bitmap = New-IconBitmap -size $size
    $payload = [byte[]](Get-IconDibBytes -bitmap $bitmap)
    $payloads.Add($payload)
    $bitmap.Dispose()
}

$fileStream = [System.IO.File]::Create($icoPath)
$writer = New-Object System.IO.BinaryWriter($fileStream)

$writer.Write([UInt16]0)                  # reserved
$writer.Write([UInt16]1)                  # type = icon
$writer.Write([UInt16]$sizes.Count)       # image count

$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $dimension = $size
    if ($size -ge 256) { $dimension = 0 }     # 256 在 ICO 目录里用 0 表示

    $writer.Write([Byte]$dimension)           # width
    $writer.Write([Byte]$dimension)           # height
    $writer.Write([Byte]0)                    # palette
    $writer.Write([Byte]0)                    # reserved
    $writer.Write([UInt16]1)                  # color planes
    $writer.Write([UInt16]32)                 # bits per pixel
    $writer.Write([UInt32]$payloads[$i].Length)
    $writer.Write([UInt32]$offset)
    $offset += $payloads[$i].Length
}

foreach ($payload in $payloads) { $writer.Write($payload) }

$writer.Flush()
$writer.Close()
$fileStream.Close()

$length = (Get-Item $icoPath).Length
Write-Host ("已生成图标：{0}（{1} 个尺寸，{2} 字节）" -f $icoPath, $sizes.Count, $length)
