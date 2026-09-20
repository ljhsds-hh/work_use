param(
    [string]$Out = "$PSScriptRoot\..\src\CodeMemo\Assets\app.ico"
)

# 生成 CodeMemo 应用图标：蓝底圆角方块 + 白色命令提示符 "C>"，多尺寸 PNG 打包为 ICO
Add-Type -AssemblyName System.Drawing

function New-TerminalPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $bg = [System.Drawing.Color]::FromArgb(255, 52, 140, 246)
    $fg = [System.Drawing.Color]::White
    $m = [int]($size * 0.08)
    $r = [int]($size * 0.22)

    # 圆角方块背景
    $bgBrush = New-Object System.Drawing.SolidBrush($bg)
    $gp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $gp.AddArc($m, $m, $r, $r, 180, 90)
    $gp.AddArc($size - $m - $r, $m, $r, $r, 270, 90)
    $gp.AddArc($size - $m - $r, $size - $m - $r, $r, $r, 0, 90)
    $gp.AddArc($m, $size - $m - $r, $r, $r, 90, 90)
    $gp.CloseFigure()
    $g.FillPath($bgBrush, $gp)

    # 白色 "C>"（命令提示符意象）
    $text = "C>"
    $font = New-Object System.Drawing.Font("Consolas", [float]($size * 0.42),
        [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $fgBrush = New-Object System.Drawing.SolidBrush($fg)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $inner = $size - 2 * $m
    $rect = New-Object System.Drawing.RectangleF([float]$m, [float]$m, [float]$inner, [float]$inner)
    $g.DrawString($text, $font, $fgBrush, $rect, $sf)

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
foreach ($s in $sizes) { [void]$images.Add((New-TerminalPng $s)) }

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
