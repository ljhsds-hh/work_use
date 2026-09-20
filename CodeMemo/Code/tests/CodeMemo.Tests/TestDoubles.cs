using CodeMemo.Services;

namespace CodeMemo.Tests;

/// <summary>剪贴板 / 对话框 / 系统外壳的假实现：让 ViewModel 能在无 UI 环境下单测。</summary>
internal sealed class FakeClipboard : IClipboardService
{
    public string? LastText { get; private set; }

    public bool Succeed { get; set; } = true;

    public string Error { get; set; } = "剪贴板被其他进程占用";

    public bool TrySetText(string text, out string? error)
    {
        if (!Succeed)
        {
            error = Error;
            return false;
        }
        LastText = text;
        error = null;
        return true;
    }
}

internal sealed class FakeDialogs : IDialogService
{
    public List<(string Message, string Title)> Warnings { get; } = [];

    public List<(string Message, string Title)> Confirms { get; } = [];

    public bool ConfirmResult { get; set; }

    public string? OpenFile { get; set; }

    public string? SaveFile { get; set; }

    /// <summary>导入方式：默认替换（要测合并就设成 Merge）；设成 null 模拟用户取消。</summary>
    public ImportMode? ImportModeResult { get; set; } = ImportMode.Replace;

    public List<string> ImportModeAsked { get; } = [];

    public void ShowWarning(string message, string title) => Warnings.Add((message, title));

    public bool Confirm(string message, string title)
    {
        Confirms.Add((message, title));
        return ConfirmResult;
    }

    public string? PickOpenFile(string title, string filter) => OpenFile;

    public string? PickSaveFile(string title, string filter, string suggestedFileName) => SaveFile;

    public ImportMode? ChooseImportMode(string fileName)
    {
        ImportModeAsked.Add(fileName);
        return ImportModeResult;
    }
}

/// <summary>假的写盘调度器：手动决定什么时候「到点」。</summary>
internal sealed class FakeSaveScheduler : ISaveScheduler
{
    public List<TimeSpan> Scheduled { get; } = [];

    public int CancelCount { get; private set; }

    public bool HasPending => _pending is not null;

    private Action? _pending;

    public void Schedule(TimeSpan delay, Action callback)
    {
        Scheduled.Add(delay);
        _pending = callback;
    }

    public void Cancel()
    {
        CancelCount++;
        _pending = null;
    }

    /// <summary>模拟延迟到点，触发最后一次排定的回调。</summary>
    public void Fire()
    {
        var callback = _pending;
        _pending = null;
        callback?.Invoke();
    }
}

internal sealed class FakeShell : IShellService
{
    public string? OpenedDirectory { get; private set; }

    public void OpenDirectory(string path) => OpenedDirectory = path;
}

internal sealed class FakeAppearance : IAppearanceService
{
    public List<(string Theme, string FontSize)> Applied { get; } = [];

    public void Apply(string theme, string fontSize) => Applied.Add((theme, fontSize));
}

internal sealed class FakeHotkey : IGlobalHotkey
{
    /// <summary>成功注册过的手势（按首次注册顺序）。</summary>
    public List<string> Registered { get; } = [];

    /// <summary>模拟被其他程序占用的组合：出现在这里的手势注册一定失败。</summary>
    public List<string> Rejected { get; } = [];

    public int UnregisterCount { get; private set; }

    public bool Succeed { get; set; } = true;

    private Action? _callback;

    public bool TryRegister(string gesture, Action onPressed)
    {
        if (!Succeed || Rejected.Contains(gesture))
        {
            return false;
        }
        if (!Registered.Contains(gesture))
        {
            Registered.Add(gesture);
        }
        _callback = onPressed;
        return true;
    }

    public void Unregister()
    {
        UnregisterCount++;
        _callback = null;
    }

    /// <summary>模拟用户按下全局热键。</summary>
    public void Press() => _callback?.Invoke();

    public void Dispose() => Unregister();
}

/// <summary>假的系统亮暗来源：测试里手动翻转并触发变化通知。</summary>
internal sealed class FakeSystemThemeSource : ISystemThemeSource
{
    public bool Light { get; set; } = true;

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public event Action? Changed;

    public bool IsLightTheme() => Light;

    public void Start() => StartCount++;

    public void Stop() => StopCount++;

    public void RaiseChanged() => Changed?.Invoke();
}
