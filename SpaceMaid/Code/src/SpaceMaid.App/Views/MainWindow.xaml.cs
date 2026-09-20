using System.ComponentModel;
using HandyControl.Controls;
using SpaceMaid.App.Services;
using SpaceMaid.App.ViewModels;

namespace SpaceMaid.App.Views;

/// <summary>
/// 主窗口：只负责"装配 + 窗口级交互"（Growl 注册、设置窗口、启动时先扫一次）。
/// 所有业务判断都在 <see cref="MainViewModel"/> 里，窗口不做任何清理决策。
///
/// 注意：基类必须是 <c>System.Windows.Window</c>（XAML 生成的 g.cs 用它）。为避开
/// <c>Window</c> / <c>MessageBox</c> 同名歧义，本文件不 <c>using System.Windows;</c>，需要处一律写全名。
/// </summary>
public partial class MainWindow : System.Windows.Window
{
    private readonly IFolderPicker _folderPicker;
    private readonly INotificationService _notifications;
    private readonly IShellService _shell;
    private readonly IDialogService _dialogs;

    public MainWindow(
        MainViewModel viewModel,
        IFolderPicker folderPicker,
        IDialogService dialogs,
        INotificationService notifications,
        IShellService shell)
    {
        ViewModel = viewModel;
        _folderPicker = folderPicker;
        _dialogs = dialogs;
        _notifications = notifications;
        _shell = shell;
        DataContext = viewModel;
        InitializeComponent();

        // Growl 轻提示：token + 面板必须成对注册（HandyControl 的约定，参数顺序是 token 在前）
        Growl.Register(NotificationToken, GrowlPanel);

        // 订阅"打开设置"请求。
        //
        // 这一行曾经**漏掉过**：ViewModel 里 `RequestOpenSettings` 事件、`OpenSettingsCommand`、
        // 导航项的 "settings" 分支都齐了，只是没人订阅——结果「设置」按钮和左侧导航项点下去毫无反应，
        // 隔离区位置、保留期、清空隔离区、报告/日志目录这些设置全部进不去（界面看不出异常，因为按钮样式正常）。
        // 现在由 `StaticSafetyTests.App_should_wire_settings_request_from_viewmodel` 守着这对 +=/-= 必须成对存在。
        ViewModel.RequestOpenSettings += ShowSettings;
    }

    /// <summary>Growl token（与 App 里的 <c>GrowlNotificationService</c> 必须一致）。</summary>
    public const string NotificationToken = "SpaceMaidGrowl";

    public MainViewModel ViewModel { get; }

    /// <summary>
    /// 启动后先做一次扫描（只读），让用户一打开就看到真实数据；窗口已经显示出来，所以不阻塞。
    /// </summary>
    protected override async void OnContentRendered(System.EventArgs e)
    {
        base.OnContentRendered(e);

        if (ViewModel.HasScanResult || ViewModel.IsBusy)
        {
            return;
        }

        await ViewModel.RescanAsync();
    }

    private void ShowSettings()
    {
        var settings = SettingsViewModel.Create(ViewModel.Bridge, _folderPicker, _dialogs, _notifications, _shell);
        var window = new SettingsWindow(settings) { Owner = this };
        window.ShowDialog();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        ViewModel.RequestOpenSettings -= ShowSettings;
        base.OnClosing(e);
    }
}
