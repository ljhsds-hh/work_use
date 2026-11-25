using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace QuietRemind.Helpers;

/// <summary>
/// Windows 11 的 DWM 圆角开关。无边框窗口默认是直角，开启后与系统窗口的圆角语言一致；
/// Windows 10 不认识该属性会直接返回失败，静默忽略即可。
/// </summary>
internal static class WindowEffects
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void EnableRoundedCorners(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }
        var preference = DwmwcpRound;
        try
        {
            DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        }
        catch (Exception)
        {
            // 旧系统 / 远程会话不支持，不影响窗口功能
        }
    }
}
