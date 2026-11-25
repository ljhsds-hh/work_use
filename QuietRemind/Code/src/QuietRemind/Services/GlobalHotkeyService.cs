using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace QuietRemind.Services;

/// <summary>
/// 全局快捷键（默认 Ctrl+Alt+N）：消息专用窗口 + RegisterHotKey，事件驱动、无常驻线程。
/// 注册失败（被占用等）只记录原因，不影响提醒主流程。
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    /// <summary>按住快捷键不产生自动重复，避免连续弹出/反复激活快速新增窗。</summary>
    private const uint ModNoRepeat = 0x4000;
    private const uint VkN = 0x4E;
    private const int HotkeyId = 0x514D;

    private HwndSource? _source;

    /// <summary>快捷键按下（已在 UI 线程）。</summary>
    public event Action? Pressed;

    public bool IsRegistered { get; private set; }

    /// <summary>最近一次注册失败原因，用于日志与诊断。</summary>
    public string? LastError { get; private set; }

    /// <summary>注册快捷键；已注册时直接返回当前状态。</summary>
    public bool Register()
    {
        if (_source is not null)
        {
            return IsRegistered;
        }

        try
        {
            var parameters = new HwndSourceParameters("QuietRemind.Hotkey")
            {
                Width = 0,
                Height = 0,
                ParentWindow = new IntPtr(-3), // HWND_MESSAGE：不显示、不占任务栏
                WindowStyle = 0,
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
            IsRegistered = NativeRegisterHotKey(_source.Handle, HotkeyId, ModControl | ModAlt | ModNoRepeat, VkN);
            if (!IsRegistered)
            {
                LastError = $"快捷键 Ctrl+Alt+N 注册失败（错误码 {Marshal.GetLastWin32Error()}），可能已被其他程序占用";
            }
            else
            {
                LastError = null;
            }
            return IsRegistered;
        }
        catch (Exception ex)
        {
            LastError = $"快捷键初始化失败：{ex.Message}";
            Dispose();
            return false;
        }
    }

    /// <summary>设置开关：关闭即注销，开启即尝试注册。返回是否达到期望状态。</summary>
    public bool SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            Unregister();
            return true;
        }
        return Register();
    }

    public void Unregister()
    {
        if (_source is null)
        {
            IsRegistered = false;
            return;
        }

        try
        {
            if (IsRegistered)
            {
                NativeUnregisterHotKey(_source.Handle, HotkeyId);
            }
            _source.RemoveHook(WndProc);
            _source.Dispose();
        }
        catch (Exception)
        {
            // 注销失败不影响进程退出
        }
        finally
        {
            _source = null;
            IsRegistered = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose() => Unregister();

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "RegisterHotKey")]
    private static extern bool NativeRegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "UnregisterHotKey")]
    private static extern bool NativeUnregisterHotKey(IntPtr hWnd, int id);
}
