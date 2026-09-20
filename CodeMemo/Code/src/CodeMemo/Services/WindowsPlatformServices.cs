using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CodeMemo.Models;
using Microsoft.Win32;

namespace CodeMemo.Services;

/// <summary>WPF 剪贴板实现（需 STA 线程，由 Application 保证）。</summary>
public sealed class ClipboardService : IClipboardService
{
    public bool TrySetText(string text, out string? error)
    {
        try
        {
            Clipboard.SetText(text);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            // 剪贴板被其他进程独占时偶发，给出可感知的失败原因而不是静默
            error = ex.Message;
            return false;
        }
    }
}

/// <summary>WPF 对话框实现（MessageBox + 文件选择框）。</summary>
public sealed class DialogService : IDialogService
{
    public void ShowWarning(string message, string title)
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public bool Confirm(string message, string title)
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
            == MessageBoxResult.Yes;

    public string? PickOpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickSaveFile(string title, string filter, string suggestedFileName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = suggestedFileName };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public ImportMode? ChooseImportMode(string fileName)
    {
        // 默认按钮放在「是（合并）」上：合并不会丢现有数据，回车走安全路径
        var answer = MessageBox.Show(
            $"「{fileName}」怎么并入？\n\n是：与现有命令库合并（重复条目自动跳过）\n否：整体替换现有命令库\n取消：不导入",
            "导入方式",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);

        return answer switch
        {
            MessageBoxResult.Yes => ImportMode.Merge,
            MessageBoxResult.No => ImportMode.Replace,
            _ => null,
        };
    }
}

/// <summary>DispatcherTimer 版写盘调度器：节流后的落盘回调在 UI 线程上跑，和 ViewModel 的数据访问同线程。</summary>
public sealed class DispatcherSaveScheduler : ISaveScheduler
{
    private DispatcherTimer? _timer;
    private Action? _callback;

    public void Schedule(TimeSpan delay, Action callback)
    {
        Cancel();

        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _callback = callback;
        _timer = new DispatcherTimer(delay, DispatcherPriority.Background, OnTick, dispatcher);
        _timer.Start();
    }

    public void Cancel()
    {
        _timer?.Stop();
        _timer = null;
        _callback = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var callback = _callback;
        Cancel();
        callback?.Invoke();
    }
}

/// <summary>系统外壳实现：用资源管理器打开数据目录。</summary>
public sealed class ShellService : IShellService
{
    public void OpenDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}

/// <summary>
/// 外观实现：切换到 HandyControl 对应皮肤（SkinType.Default / Dark）并写入字号资源。
///
/// HandyControl 3.5.1 没有 ThemeManager，皮肤 = 「颜色字典（Skin*.xaml）+ 样式字典（Theme.xaml）」这一对资源。
/// 样式字典里的画刷是**首次使用时**按当时的颜色字典算出来的，算完就固定不再变：
/// 所以换肤必须重新挂一套全新的字典实例 —— 复用已算过的样式字典、或者只改 hc:Theme 的 Skin，
/// 实测都只会换掉颜色 token，界面画刷还是上一套（换肤看起来没反应）。
/// 这里按 pack URI 现场 new，保证每次切换都拿到全新实例，同进程内反复切换也有效。
///
/// 主题取「跟随系统」时按 <see cref="ISystemThemeSource"/> 解析，并订阅系统亮暗变化自动重切。
/// </summary>
public sealed class WindowsAppearanceService : IAppearanceService
{
    private static readonly Uri SkinDefaultUri = new("pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml");
    private static readonly Uri SkinDarkUri = new("pack://application:,,,/HandyControl;component/Themes/SkinDark.xaml");
    private static readonly Uri StylesUri = new("pack://application:,,,/HandyControl;component/Themes/Theme.xaml");

    private readonly ISystemThemeSource _systemTheme;
    private string _fontSize = AppSettingValues.FontNormal;
    private bool _followSystem;
    private bool _subscribed;

    public WindowsAppearanceService(ISystemThemeSource? systemTheme = null)
        => _systemTheme = systemTheme ?? new WindowsSystemThemeSource();

    public void Apply(string theme, string fontSize)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        _fontSize = fontSize;
        _followSystem = AppSettingValues.IsSystemTheme(theme);

        if (_followSystem)
        {
            if (!_subscribed)
            {
                _systemTheme.Changed += OnSystemThemeChanged;
                _subscribed = true;
            }
            _systemTheme.Start();
        }
        else
        {
            _systemTheme.Stop();
        }

        ApplySkin(app, ResolveSkin(theme), fontSize);
    }

    /// <summary>系统亮暗变化：只有处于「跟随系统」时才重切（回调可能不在 UI 线程）。</summary>
    private void OnSystemThemeChanged()
    {
        if (!_followSystem || Application.Current is not { } app)
        {
            return;
        }

        app.Dispatcher.Invoke(() => ApplySkin(app, ResolveSkin(AppSettingValues.ThemeSystem), _fontSize));
    }

    /// <summary>把偏好取值解析成真实皮肤：跟随系统时问系统要，其余按取值。</summary>
    private string ResolveSkin(string theme) => AppSettingValues.NormalizeTheme(theme) switch
    {
        AppSettingValues.ThemeDark => AppSettingValues.ThemeDark,
        AppSettingValues.ThemeSystem => _systemTheme.IsLightTheme() ? AppSettingValues.ThemeDefault : AppSettingValues.ThemeDark,
        _ => AppSettingValues.ThemeDefault,
    };

    private static void ApplySkin(Application app, string skinTheme, string fontSize)
    {
        var skinUri = skinTheme == AppSettingValues.ThemeDark ? SkinDarkUri : SkinDefaultUri;

        var merged = app.Resources.MergedDictionaries;
        merged.Clear();
        merged.Add(new ResourceDictionary { Source = skinUri });
        merged.Add(new ResourceDictionary { Source = StylesUri });

        foreach (var (key, value) in AppearanceScale.ResolveFontSizes(fontSize))
        {
            app.Resources[key] = value;
        }

        // 皮肤换了要让已有控件重新取一遍资源，否则部分控件还挂着旧画刷
        app.MainWindow?.OnApplyTemplate();
    }
}

/// <summary>
/// 系统亮暗主题的生产实现：读注册表的 AppsUseLightTheme，并借 SystemEvents 监听系统设置变化。
/// </summary>
public sealed class WindowsSystemThemeSource : ISystemThemeSource
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private bool _listening;

    public event Action? Changed;

    public bool IsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            // 取不到（旧系统 / 权限问题）时按亮色处理，宁可亮也不要黑得莫名其妙
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (Exception)
        {
            return true;
        }
    }

    public void Start()
    {
        if (_listening)
        {
            return;
        }
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _listening = true;
    }

    public void Stop()
    {
        if (!_listening)
        {
            return;
        }
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _listening = false;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
        {
            Changed?.Invoke();
        }
    }
}

/// <summary>
/// 全局热键实现：注册到一个隐藏的消息窗口（HwndSource），收到 WM_HOTKEY 后回调。
/// 热键注册失败（被占用）时返回 false，由调用方提示并关闭该偏好。
/// </summary>
public sealed class GlobalHotkeyService : IGlobalHotkey
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x0C0D3;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExToolWindow = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    private HwndSource? _sink;
    private Action? _onPressed;
    private bool _registered;

    public bool TryRegister(string gesture, Action onPressed)
    {
        Unregister();

        if (!HotkeyGesture.TryParse(gesture, out var parsed) || parsed is null)
        {
            return false;
        }

        _sink ??= CreateSink();
        _registered = RegisterHotKey(_sink.Handle, HotkeyId, parsed.Modifiers, parsed.VirtualKey);
        _onPressed = _registered ? onPressed : null;
        return _registered;
    }

    public void Unregister()
    {
        if (_registered && _sink is not null)
        {
            UnregisterHotKey(_sink.Handle, HotkeyId);
        }
        _registered = false;
        _onPressed = null;
    }

    private HwndSource CreateSink()
    {
        var parameters = new HwndSourceParameters("CodeMemoHotkeySink")
        {
            Width = 0,
            Height = 0,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = WsPopup,
            ExtendedWindowStyle = WsExToolWindow,
        };

        var sink = new HwndSource(parameters);
        sink.AddHook(WndProc);
        return sink;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            _onPressed?.Invoke();
            handled = true;
        }
        return nint.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _sink?.Dispose();
        _sink = null;
    }
}
