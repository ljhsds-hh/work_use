# ═══════════════════════════════════════════════════════════════════════════════════════════
# SpaceMaid 界面验证 harness（不需要"看图"）
# ═══════════════════════════════════════════════════════════════════════════════════════════
#
# 为什么用这种方式验证界面：本机没有可用的视觉 provider（模型看不了图片）。所以界面验证靠两样东西：
#   ① UIA 可访问性树：每个元素的 Name / 控件类型 / 屏幕矩形 → 证明"该出现的东西真的出现了、位置合理"；
#   ② PrintWindow 抓窗口自身再算像素统计 → 证明"有层级"（导航栏 / 页头 / 正文亮度不同、不是一片死白）。
#      **不能用 CopyFromScreen**：它抓的是屏幕，被遮挡时会拿到桌面内容。
#
# 用法（在仓库根目录）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File SpaceMaid\Code\scripts\probe-ui.ps1
# 或者：
#   Invoke-Expression (Get-Content -Raw -Encoding UTF8 'SpaceMaid\Code\scripts\probe-ui.ps1')
#
# 说明：
#   * **本文件带 UTF-8 BOM**：Windows PowerShell 5.1 只有看到 BOM 才会按 UTF-8 解析中文，
#     否则中文按 GBK 读进来变乱码（按钮名匹配不上）。这是刻意的，不要"顺手"去掉 BOM。
#   * 未提权时应用会先弹自己的"权限不足"闸门（需求 3.6-2），脚本会点掉它再继续——
#     这不是异常，而是需求要求的行为（exe 清单固定 requireAdministrator，无人值守跑不了 UAC）。
#   * **只点这些按钮**：重新扫描（只读）、左侧导航、设置、关闭。绝不点清理 / 清空 / 还原。
#   * 输出：截图与 UIA 树落在 D:\logs\SpaceMaid\ui\；退出码 0 = 全部断言通过，2 = 有失败。
#
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class SmWin
{
    private delegate bool EP(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EP cb, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int m);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    public struct RECT { public int Left, Top, Right, Bottom; }
    public static List<IntPtr> Of(uint pid)
    {
        var r = new List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr l) { uint o; GetWindowThreadProcessId(h, out o); if (o == pid) r.Add(h); return true; }, IntPtr.Zero);
        return r;
    }
    public static string Title(IntPtr h)
    {
        var sb = new StringBuilder(GetWindowTextLength(h) + 2);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }
    public static void Close(IntPtr h) { PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); }
}
'@

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$appDir = Join-Path $repoRoot 'SpaceMaid\Code\src\SpaceMaid.App\bin\Debug\net8.0-windows'
$outDir = 'D:\logs\SpaceMaid\ui'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$failures = New-Object System.Collections.Generic.List[string]
$results = New-Object System.Collections.Generic.List[string]

function Assert-That([bool]$condition, [string]$name, [string]$detail) {
    if ($condition) {
        $script:results.Add('[PASS] ' + $name + ' -- ' + $detail)
    } else {
        $script:results.Add('[FAIL] ' + $name + ' -- ' + $detail)
        $script:failures.Add($name)
    }
    Write-Host ('[assert] ' + $(if ($condition) { 'PASS' } else { 'FAIL' }) + ' ' + $name + ' -- ' + $detail)
}

$appDll = Join-Path $appDir 'SpaceMaid.dll'
if (-not (Test-Path $appDll)) {
    Write-Host ('[fatal] 找不到 ' + $appDll + '，先跑 dotnet build SpaceMaid\Code\space-maid.slnx')
    exit 2
}

$proc = Start-Process -FilePath 'dotnet' -ArgumentList @('SpaceMaid.dll') -WorkingDirectory $appDir -PassThru
$appPid = [uint32]$proc.Id
Write-Host ('[start] pid=' + $appPid)

function Get-AppWindows { return [SmWin]::Of($appPid) }
function Find-WindowByTitle([string]$like) {
    foreach ($h in Get-AppWindows) { $t = [SmWin]::Title($h); if ($t -and $t -like $like) { return $h } }
    return $null
}

# ── 1) 未提权时先点掉应用自己的"权限不足"闸门（需求 3.6-2）──
$gate = $null
$deadline = (Get-Date).AddSeconds(15)
while (-not $gate -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 300
    $gate = Find-WindowByTitle '*权限不足*'
}
if ($gate) {
    $gateEl = [System.Windows.Automation.AutomationElement]::FromHandle($gate)
    $ok = $gateEl.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '确定')))
    if ($ok) { $ok.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
    else { [SmWin]::Close($gate) }
    Write-Host '[gate] 已点掉"权限不足"提示（未提权是预期情形，不影响后续界面验证）'
    Start-Sleep -Milliseconds 1500
}

# ── 2) 等主窗 ──
$main = $null
$deadline = (Get-Date).AddSeconds(25)
while (-not $main -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 400
    $main = Find-WindowByTitle '*SpaceMaid C*'
}
if (-not $main) {
    Write-Host '[fatal] 主窗没出现，进程内的窗口有：'
    foreach ($h in Get-AppWindows) { $t = [SmWin]::Title($h); if ($t) { Write-Host ('   ' + $t) } }
    if (-not $proc.HasExited) { $proc.Kill() }
    exit 2
}
$mainTitle = [SmWin]::Title($main)
Assert-That ($mainTitle -like '*SpaceMaid*') 'main-title' ('主窗标题 = ' + $mainTitle)

$root = [System.Windows.Automation.AutomationElement]::FromHandle($main)

function Find-ControlByText([string]$text, $controlType) {
    foreach ($el in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $controlType)))) {
        if ($el.Current.Name -eq $text) { return $el }
        foreach ($k in $el.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))) {
            if ($k.Current.Name -eq $text) { return $el }
        }
    }
    return $null
}

function Export-Tree([string]$state) {
    $script:elementCount = 0
    $lines = New-Object System.Collections.Generic.List[string]
    function Walk($el, $depth) {
        if ($depth -gt 7) { return }
        $script:elementCount++
        $c = $el.Current
        $r = $c.BoundingRectangle
        if (-not [string]::IsNullOrWhiteSpace($c.Name)) {
            $lines.Add(("  " * $depth) + '[' + $c.ControlType.ProgrammaticName.Replace('ControlType.', '') + '] ' + $c.Name + ' @ ' + [int]$r.X + ',' + [int]$r.Y + ' ' + [int]$r.Width + 'x' + [int]$r.Height)
        }
        $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
        $child = $walker.GetFirstChild($el)
        while ($child) { Walk $child ($depth + 1); $child = $walker.GetNextSibling($child) }
    }
    Walk $root 0
    $path = Join-Path $outDir ('tree-' + $state + '-' + $stamp + '.txt')
    $lines | Set-Content -Encoding UTF8 $path
    Write-Host ('[uia] ' + $state + ' elements=' + $script:elementCount + ' -> ' + $path)
    return $script:elementCount
}

function Get-Metrics([string]$state, [IntPtr]$hwnd) {
    $r = New-Object SmWin+RECT
    [void][SmWin]::GetWindowRect($hwnd, [ref]$r)
    $w = $r.Right - $r.Left
    $h = $r.Bottom - $r.Top
    $metrics = @{ width = $w; height = $h; managed = $false }
    if ($w -le 0 -or $h -le 0) { return $metrics }

    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [void][SmWin]::PrintWindow($hwnd, $hdc, 2)
    $g.ReleaseHdc($hdc)
    $g.Dispose()

    $shotPath = Join-Path $outDir ('shot-' + $state + '-' + $stamp + '.png')
    $bmp.Save($shotPath, [System.Drawing.Imaging.ImageFormat]::Png)

    $sum = 0.0; $n = 0
    $colors = @{}
    $bandSum = New-Object double[] 10
    $bandN = New-Object int[] 10
    $railSum = 0.0; $railN = 0; $headSum = 0.0; $headN = 0
    for ($y = 0; $y -lt $h; $y += 2) {
        for ($x = 0; $x -lt $w; $x += 2) {
            $c = $bmp.GetPixel($x, $y)
            $lum = 0.299 * $c.R + 0.587 * $c.G + 0.114 * $c.B
            $sum += $lum; $n++
            $key = $c.ToArgb()
            if ($colors.ContainsKey($key)) { $colors[$key]++ } else { $colors[$key] = 1 }
            $b = [int][Math]::Floor($y * 10 / $h); if ($b -gt 9) { $b = 9 }
            $bandSum[$b] += $lum; $bandN[$b]++
            if ($x -lt ($w * 0.18)) { $railSum += $lum; $railN++ }
            elseif ($y -lt ($h * 0.09)) { $headSum += $lum; $headN++ }
        }
    }
    $top = $colors.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1
    $bands = @()
    for ($i = 0; $i -lt 10; $i++) { if ($bandN[$i] -gt 0) { $bands += [int]($bandSum[$i] / $bandN[$i]) } else { $bands += 0 } }
    $bmp.Dispose()

    $metrics.managed = $true
    $metrics.shot = $shotPath
    $metrics.mean = [int]($sum / $n)
    $metrics.colors = $colors.Count
    $metrics.topSharePercent = [int](100 * $top.Value / $n)
    $metrics.bands = $bands
    $metrics.bandRange = ([int]($bands | Measure-Object -Maximum | Select-Object -ExpandProperty Maximum)) - ([int]($bands | Measure-Object -Minimum | Select-Object -ExpandProperty Minimum))
    $metrics.railMean = [int]($railSum / $railN)
    $metrics.headerMean = [int]($headSum / $headN)
    $metrics.railHeaderDelta = [int][Math]::Abs($metrics.railMean - $metrics.headerMean)
    Write-Host ('[pixels] ' + $state + ' ' + $w + 'x' + $h + ' mean=' + $metrics.mean + ' colors=' + $metrics.colors + ' topShare=' + $metrics.topSharePercent + '% bands=' + ($bands -join ',') + ' rail=' + $metrics.railMean + ' header=' + $metrics.headerMean)
    return $metrics
}

# ── 3) 概览页（未扫描）──
$overviewCount = Export-Tree 'overview'
$overviewMetrics = Get-Metrics 'overview' $main

# ── 4) 点「重新扫描」（只读）并等扫描结束 ──
$scanned = $false
$rescan = Find-ControlByText '重新扫描' ([System.Windows.Automation.ControlType]::Button)
if ($rescan) {
    $rescan.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Write-Host '[action] 已点「重新扫描」（只读）'
    $deadline = (Get-Date).AddSeconds(120)
    while ((Get-Date) -lt $deadline -and -not $scanned) {
        Start-Sleep -Seconds 1
        foreach ($el in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))) {
            if ($el.Current.Name -like '*扫描完成*') { $scanned = $true; break }
        }
    }
}
Assert-That $scanned 'scan-completes' '点「重新扫描」后界面出现"扫描完成"（扫描只读、不删文件）'
$scannedCount = Export-Tree 'overview-scanned'
$scannedMetrics = Get-Metrics 'overview-scanned' $main

# ── 5) 清理计划页（分级列表）──
$cleanCount = 0
$navClean = Find-ControlByText '清理计划' ([System.Windows.Automation.ControlType]::RadioButton)
if ($navClean) {
    $navClean.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 1500
    $cleanCount = Export-Tree 'clean'
    $null = Get-Metrics 'clean' $main
}
Assert-That ($cleanCount -ge 60) 'clean-page-tree' ('清理计划页 UIA 元素数 = ' + $cleanCount + '（阈值 60）')

# ── 6) 隔离区页 ──
$quarantineCount = 0
$navQuarantine = Find-ControlByText '隔离区' ([System.Windows.Automation.ControlType]::RadioButton)
if ($navQuarantine) {
    $navQuarantine.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 1500
    $quarantineCount = Export-Tree 'quarantine'
    $null = Get-Metrics 'quarantine' $main
}
Assert-That ($quarantineCount -ge 20) 'quarantine-page-tree' ('隔离区页 UIA 元素数 = ' + $quarantineCount + '（阈值 20）')

# ── 7) 层级与"不是一片死白"的像素断言（"好不好看"里唯一能自动化的部分）──
$bestMetrics = $overviewMetrics
if ($scannedMetrics.managed) { $bestMetrics = $scannedMetrics }
Assert-That ($overviewMetrics.railHeaderDelta -ge 8) 'rail-vs-header-surface' ('左导航栏与页头的表面亮度差 = ' + $overviewMetrics.railHeaderDelta + '（阈值 8）')
Assert-That ($bestMetrics.bandRange -ge 8) 'vertical-hierarchy' ('十条横带亮度极差 = ' + $bestMetrics.bandRange + '（阈值 8）')
Assert-That ($bestMetrics.topSharePercent -le 90) 'not-flat-single-color' ('最高频颜色占比 = ' + $bestMetrics.topSharePercent + '%（阈值 <= 90%）')
Assert-That ($bestMetrics.colors -ge 120) 'color-variety' ('不同颜色数 = ' + $bestMetrics.colors + '（阈值 120）')

# ── 8) 设置窗口（模态；主窗会被禁用，所以按进程窗口直接找并抓它）──
$settingsOpened = $false
$settingsButton = Find-ControlByText '设置' ([System.Windows.Automation.ControlType]::Button)
if ($settingsButton) {
    $settingsButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Write-Host '[action] 已点左侧「设置」'
    $deadline = (Get-Date).AddSeconds(15)
    $settingsWindow = $null
    while (-not $settingsWindow -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $settingsWindow = Find-WindowByTitle '*设置*'
    }
    if ($settingsWindow) {
        $settingsOpened = $true
        $savedRoot = $root
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($settingsWindow)
        $null = Export-Tree 'settings'
        $null = Get-Metrics 'settings' $settingsWindow
        $hasSave = $null -ne (Find-ControlByText '保存设置' ([System.Windows.Automation.ControlType]::Button))
        Assert-That $hasSave 'settings-has-save' '设置窗口里能找到「保存设置」按钮'
        $root = $savedRoot
        [SmWin]::Close($settingsWindow)
        Start-Sleep -Milliseconds 800
    }
}
Assert-That $settingsOpened 'settings-opens' '点「设置」能打开设置窗口（这条曾因事件没人订阅而整块进不去）'

# ── 9) 截图落盘 ──
foreach ($m in @($overviewMetrics, $scannedMetrics)) {
    if ($m.managed) {
        $len = (Get-Item $m.shot).Length
        Assert-That ($len -gt 20480) ('shot-size-' + [System.IO.Path]::GetFileNameWithoutExtension($m.shot)) ('截图字节数 = ' + $len)
    }
}

if (-not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(5000) | Out-Null }
Write-Host '[cleanup] 应用进程已结束'

$passed = 0
foreach ($line in $results) { if ($line.StartsWith('[PASS]')) { $passed++ } }
Write-Host ''
Write-Host ('=== 界面验证：' + $passed + ' 通过 / ' + $failures.Count + ' 失败 ===')
foreach ($line in $results) { Write-Host $line }
Write-Host ('[output] ' + $outDir)

if ($failures.Count -gt 0) { exit 2 }
exit 0
