namespace CodeMemo.Services;

/// <summary>
/// 剪贴板写入能力。抽成接口是为了让 ViewModel 能脱离 UI / STA 线程单测。
/// </summary>
public interface IClipboardService
{
    /// <summary>写入文本；失败时返回 false 并给出原因（剪贴板被其他进程独占时偶发）。</summary>
    bool TrySetText(string text, out string? error);
}

/// <summary>
/// 对话框与文件选择能力。抽成接口是为了让 ViewModel 能脱离 UI 单测
/// （测试里用假实现返回临时文件路径即可覆盖导入 / 导出流程）。
/// </summary>
public interface IDialogService
{
    /// <summary>警告提示（导入失败、数据文件损坏等），仅提示不阻塞流程。</summary>
    void ShowWarning(string message, string title);

    /// <summary>二次确认，返回用户是否点了「是」。</summary>
    bool Confirm(string message, string title);

    /// <summary>选择待打开的 JSON 文件；取消返回 null。</summary>
    string? PickOpenFile(string title, string filter);

    /// <summary>选择保存位置；取消返回 null。</summary>
    string? PickSaveFile(string title, string filter, string suggestedFileName);

    /// <summary>问导入方式（合并 / 替换）；取消返回 null。</summary>
    ImportMode? ChooseImportMode(string fileName);
}

/// <summary>系统外壳能力（打开数据目录等）。</summary>
public interface IShellService
{
    void OpenDirectory(string path);
}

/// <summary>外观应用（皮肤 + 字号档位）。抽成接口是为了让 ViewModel 能脱离 UI 单测。</summary>
public interface IAppearanceService
{
    /// <summary>
    /// 应用外观偏好，参数是已归一化的取值（见 AppSettingValues）。
    /// 主题传 <see cref="AppSettingValues.ThemeSystem"/> 时会按系统当前亮暗解析，并跟着系统变化自动切换。
    /// </summary>
    void Apply(string theme, string fontSize);
}

/// <summary>
/// 系统亮暗主题来源：读当前值 + 变化通知。
/// 抽成接口是为了让外观服务能在测试里喂一个假的系统状态，不依赖真机设置。
/// </summary>
public interface ISystemThemeSource
{
    /// <summary>系统当前是不是亮色主题。</summary>
    bool IsLightTheme();

    /// <summary>系统主题变化（非 UI 线程回调，实现方自行切线程）。</summary>
    event Action? Changed;

    void Start();

    void Stop();
}

/// <summary>
/// 全局热键注册能力。实现要在 UI 线程上构造；注册失败（被其他程序占用或手势不合法）返回 false。
/// </summary>
public interface IGlobalHotkey : IDisposable
{
    bool TryRegister(string gesture, Action onPressed);

    void Unregister();
}

/// <summary>
/// 平台能力集合：MainViewModel 只依赖这组接口，所以能在无 UI 的环境下单测。
/// </summary>
public sealed record AppPlatform(
    IClipboardService Clipboard,
    IDialogService Dialogs,
    IShellService Shell,
    IAppearanceService Appearance,
    IGlobalHotkey Hotkey,
    ISaveScheduler Scheduler);
