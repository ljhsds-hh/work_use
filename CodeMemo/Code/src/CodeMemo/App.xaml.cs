using System.IO;
using System.Windows;
using CodeMemo.Models;
using CodeMemo.Services;
using CodeMemo.Views;

namespace CodeMemo;

/// <summary>应用入口（组合根）：组装数据存储、偏好、平台服务与主窗口。</summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dataDirectory = GetDataDirectory();
        var store = new LibraryStore(dataDirectory);
        var settingsStore = new SettingsStore(dataDirectory);
        var settings = settingsStore.Load();

        // 先套用皮肤与字号再开窗，避免窗口先按默认外观画一次再跳成暗色
        var appearance = new WindowsAppearanceService();
        appearance.Apply(settings.Theme, settings.FontSize);

        var platform = new AppPlatform(
            new ClipboardService(),          // WPF 剪贴板 API 的 STA 要求由 Application 保证
            new DialogService(),
            new ShellService(),
            appearance,
            new GlobalHotkeyService(),
            new DispatcherSaveScheduler());

        new MainWindow(store, settingsStore, settings, platform).Show();
    }

    /// <summary>用户数据目录：%AppData%\CodeMemo\。</summary>
    public static string GetDataDirectory()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodeMemo");
}
