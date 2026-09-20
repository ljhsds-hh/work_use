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
