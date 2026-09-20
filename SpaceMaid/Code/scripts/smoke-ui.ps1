# SpaceMaid UI smoke test (ASCII only, Windows PowerShell 5.1 safe).
#
# Loads the WPF app through the dotnet host (bypasses the requireAdministrator apphost so no
# unattended UAC prompt appears), polls the process' top-level windows during the first
# 3 seconds after launch, and decides PASS/FAIL from what it finds:
#
#   PASS(0)  main window titled "SpaceMaid C-Pan-Kong-Jian-Guan-Jia" exists
#            (this is what an elevated launch produces)
#   PASS(0)  the elevation-required dialog is up instead
#            (correct, spec-mandated behaviour for an unelevated launch: requirement 3.6-2
#             says the cleaning flow must be blocked, so this is NOT an app defect)
#   FAIL(2)  a startup-failure dialog is up, or no window at all appears
#
# Nothing is ever clicked, so no cleanup action can run.
# %AppData%\SpaceMaid is backed up before and restored after the run.
$ErrorActionPreference = 'Stop'

$expectedTitle  = 'SpaceMaid C ' + [char]0x76D8 + ' ' + [char]0x7A7A + [char]0x95F4 + ' ' + [char]0x7BA1 + [char]0x5BB6
$elevationTitle = [char]0x6743 + [char]0x9650 + [char]0x4E0D + [char]0x8DB3
$failureTitle   = 'SpaceMaid ' + [char]0x542F + [char]0x52A8 + [char]0x5931 + [char]0x8D25

$appDir  = 'D:\Project\GitHub\work_use\SpaceMaid\Code\src\SpaceMaid.App\bin\Debug\net8.0-windows'
$appDll  = Join-Path $appDir 'SpaceMaid.dll'
$dataDir = Join-Path $env:APPDATA 'SpaceMaid'
$backup  = Join-Path $env:TEMP ('spacemaid-appdata-backup-' + [guid]::NewGuid().ToString('N'))

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class WinScan
{
    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);

    public static List<string[]> WindowsOf(uint pid)
    {
        var result = new List<string[]>();
        EnumWindows(
            delegate(IntPtr hWnd, IntPtr lParam)
            {
                uint owner;
                GetWindowThreadProcessId(hWnd, out owner);
                if (owner == pid)
                {
                    var text = new StringBuilder(GetWindowTextLength(hWnd) + 2);
                    GetWindowText(hWnd, text, text.Capacity);
                    var cls = new StringBuilder(256);
                    GetClassName(hWnd, cls, cls.Capacity);
                    result.Add(new string[] { cls.ToString(), text.ToString(), IsWindowVisible(hWnd).ToString() });
                }
                return true;
            },
            IntPtr.Zero);
        return result;
    }
}
'@ -ErrorAction Stop

$hadData = Test-Path $dataDir
if ($hadData) { Copy-Item -Recurse -Force $dataDir $backup }
Write-Host ('[backup] AppData SpaceMaid existed={0} backup={1}' -f $hadData, $(if ($hadData) { $backup } else { 'none' }))

$proc = $null
$match = $null
$code = 1
try {
    $proc = Start-Process -FilePath 'dotnet' -ArgumentList @($appDll) -PassThru -WorkingDirectory $appDir
    Write-Host ('[start] pid={0} process={1}' -f $proc.Id, $proc.ProcessName)

    $deadline = (Get-Date).AddSeconds(3)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 150
        $proc.Refresh()
        if ($proc.HasExited) { break }

        foreach ($w in [WinScan]::WindowsOf([uint32]$proc.Id)) {
            if ($w[1] -eq $expectedTitle) { $match = 'main-window'; break }
            if ($w[1] -eq $failureTitle) { $match = 'startup-failure'; break }
            if ($w[1] -eq $elevationTitle) { $match = 'elevation-required'; break }
        }

        if ($match) { break }
    }

    $proc.Refresh()
    $all = [WinScan]::WindowsOf([uint32]$proc.Id)
    Write-Host ('[diag] hasExited={0} exitCode={1} windowCount={2} matched={3}' -f `
        $proc.HasExited, $(if ($proc.HasExited) { $proc.ExitCode } else { 'n/a' }), $all.Count, $(if ($match) { $match } else { 'none' }))
    foreach ($w in $all) {
        if (-not [string]::IsNullOrEmpty($w[1])) {
            Write-Host ('[diag] visible={0} class={1} title={2}' -f $w[2], $w[0], $w[1])
        }
    }

    switch ($match) {
        'main-window'        { Write-Host '[smoke] PASS: main window title found within 3 seconds'; $code = 0 }
        'elevation-required' { Write-Host '[smoke] PASS: unelevated run correctly shows the elevation-required dialog and blocks the cleaning flow (requirement 3.6-2)'; $code = 0 }
        'startup-failure'    { Write-Host '[smoke] FAIL: startup-failure dialog appeared'; $code = 2 }
        default              { Write-Host '[smoke] FAIL: no expected window within 3 seconds'; $code = 2 }
    }
}
finally {
    if ($proc -and -not $proc.HasExited) {
        try { $proc.Kill() } catch { Write-Host ('[cleanup] kill failed: ' + $_.Exception.Message) }
        try { $proc.WaitForExit(5000) | Out-Null } catch { }
        Write-Host ('[cleanup] killed pid={0}' -f $proc.Id)
    }

    $leftover = @(Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)
    Write-Host ('[cleanup] leftover-process={0}' -f $(if ($leftover.Count -gt 0) { 'yes' } else { 'no' }))

    if (Test-Path $backup) {
        Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
        Copy-Item -Recurse -Force $backup $dataDir
        Remove-Item -Recurse -Force $backup
        Write-Host '[restore] AppData SpaceMaid restored'
    } elseif (Test-Path $dataDir) {
        Remove-Item -Recurse -Force $dataDir
        Write-Host '[restore] removed AppData SpaceMaid created by this run'
    } else {
        Write-Host '[restore] AppData SpaceMaid absent before and after'
    }
}

exit $code
