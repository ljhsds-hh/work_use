$ErrorActionPreference = "Stop"

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class M {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, int e);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(150);
        mouse_event(2, 0, 0, 0, 0);
        mouse_event(4, 0, 0, 0, 0);
    }
    public static void Top(IntPtr h) {
        SetWindowPos(h, (IntPtr)(-1), 0, 0, 0, 0, 0x3);
        SetForegroundWindow(h);
    }
}
"@

function Get-CenterX($r) { return [int]([double]$r.Left + [double]$r.Width / 2) }
function Get-CenterY($r) { return [int]([double]$r.Top + [double]$r.Height / 2) }

Get-Process -Name "CreateTpl" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800

Start-Process -FilePath "D:\Project\LocalItems\create-tpl\Code\src\CreateTpl\bin\Release\net8.0-windows\win-x64\publish\CreateTpl.exe" | Out-Null

$target = $null
for ($i = 0; $i -lt 120; $i++) {
    Start-Sleep -Milliseconds 500
    $target = Get-Process -Name "CreateTpl" -ErrorAction SilentlyContinue |
              Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if ($target) { break }
}
if (-not $target) { throw "window not found" }
$hwnd = $target.MainWindowHandle
[M]::Top($hwnd)
Start-Sleep -Milliseconds 1000

$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
$editCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Edit)
$edits = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCond)
if ($edits.Count -lt 2) { throw "edit boxes not found" }

# 1. 根目录（键入）
$r1 = $edits[0].Current.BoundingRectangle
[M]::Click((Get-CenterX $r1), (Get-CenterY $r1))
Start-Sleep -Milliseconds 400
[System.Windows.Forms.SendKeys]::SendWait("^a")
Start-Sleep -Milliseconds 150
[System.Windows.Forms.SendKeys]::SendWait("D:/ProjLocal/final-check")
Start-Sleep -Milliseconds 400

# 2. 工程名（键入；回车换行在此环境不可靠，单工程验证 UI 链路，多工程逻辑由单测覆盖）
$r2 = $edits[1].Current.BoundingRectangle
[M]::Click((Get-CenterX $r2), (Get-CenterY $r2))
Start-Sleep -Milliseconds 400
[System.Windows.Forms.SendKeys]::SendWait("^a")
Start-Sleep -Milliseconds 150
[System.Windows.Forms.SendKeys]::SendWait("Project-Alpha")
Start-Sleep -Milliseconds 400

# 3. 启动构建
$btnCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, "启动蓝图构建")
$btn = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
if (-not $btn) { throw "build button not found" }
$rb = $btn.Current.BoundingRectangle
[M]::Click((Get-CenterX $rb), (Get-CenterY $rb))
Start-Sleep -Milliseconds 2500

# 4. 拓扑树验收：默认展开 → 点击箭头折叠 → 节点数应减少
$itemCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::TreeItem)
$items = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCond)
Write-Host ("[EXPANDED] tree items: " + $items.Count)
foreach ($it in $items) { Write-Host ("   - " + $it.Current.Name) }

if ($items.Count -gt 0) {
    $pat = $items[0].GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    Write-Host ("[BEFORE] first item state: " + $pat.Current.ExpandCollapseState)
    # 定位折叠开关（ToggleButton 在 UIA 中呈现为 Button，名称为其 ToolTip）
    $btnCond2 = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $allBtns = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond2)
    Write-Host ("buttons: " + $allBtns.Count)
    foreach ($b in $allBtns) { Write-Host ("   btn [" + $b.Current.Name + "]") }

    $expander = $null
    # 折叠箭头为 18×18 的小按钮（主题切换按钮为 36×36，需排除）
    foreach ($b in $allBtns) {
        $br = $b.Current.BoundingRectangle
        if ([string]::IsNullOrEmpty($b.Current.Name) -and $br.Width -le 20) { $expander = $b; break }
    }
    if ($expander) {
        $er = $expander.Current.BoundingRectangle
        Write-Host ("expander rect: L=" + $er.Left + " T=" + $er.Top + " W=" + $er.Width + " H=" + $er.Height)
        [M]::Click([int]($er.Left + $er.Width / 2), [int]($er.Top + $er.Height / 2))
        Start-Sleep -Milliseconds 800
        $pat2 = $items[0].GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        Write-Host ("[AFTER-CLICK] first item state: " + $pat2.Current.ExpandCollapseState)

        [M]::Click([int]($er.Left + $er.Width / 2), [int]($er.Top + $er.Height / 2))
        Start-Sleep -Milliseconds 800
        $pat3 = $items[0].GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        Write-Host ("[AFTER-SECOND-CLICK] first item state: " + $pat3.Current.ExpandCollapseState)
    } else {
        Write-Host "expander button not found by name"
    }
}

Write-Host "driven"
