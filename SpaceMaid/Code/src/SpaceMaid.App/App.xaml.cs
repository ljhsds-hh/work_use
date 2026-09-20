using System.Windows;
using SpaceMaid.App.Services;
using SpaceMaid.App.ViewModels;
using SpaceMaid.App.Views;
using SpaceMaid.Core;

namespace SpaceMaid.App;

/// <summary>
/// 应用入口（组合根）。启动编排固定为：
/// ① 创建 <see cref="CoreServices"/>（唯一内核入口）；
/// ② <c>Prepare()</c> 做启动自检：日志滚动 → 隔离区账本自检 → 到期批次惰性释放 → 隔离区路径校验；
/// ③ 组装平台服务（文件夹选择 / 确认框 / Growl / 打开目录）；
/// ④ 建 <see cref="MainViewModel"/> 并显示主窗。
///
/// 注意：这里**不做任何清理**，也不解析任何"跳过确认"的参数（需求 3.9-5 / 4.3-5）。
/// 只读 CLI（<c>--dry-run</c>/<c>--report</c>）属于另一个任务，本文件不涉及。
/// </summary>
public partial class App : Application
{
    /// <summary>主窗关闭即退出（本工具"打开就用、用完就关"，不做托盘常驻）。</summary>
    public App() => ShutdownMode = ShutdownMode.OnMainWindowClose;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            // ① 内核组合根：设置读不到/读坏了都会回默认值，不会中断启动
            var core = CoreServices.Create();
            var bridge = new CoreServicesBridge(core);

            // ③ 平台服务：ViewModel 只认识这些接口，所以能在无 UI 的测试里跑
            var folderPicker = new FolderPickerService();
            var dialogs = new DialogService();
            var notifications = new GrowlNotificationService("SpaceMaidGrowl");
            var shell = new ShellService();

            // ④ 扫描与执行：内核实现，界面不复制任何判定逻辑
            var scanner = new CoreScanService(core);
            var executor = new CoreCleanExecutor(core);

            var viewModel = new MainViewModel(bridge, scanner, executor, dialogs, folderPicker, notifications, shell);

            // ② 启动自检必须在窗口出现之前跑完（"到期释放"没有常驻进程，只能在这里做）
            var preparation = viewModel.Initialize();

            var window = new MainWindow(viewModel, folderPicker, dialogs, notifications, shell);
            MainWindow = window;
            window.Show();

            if (!preparation.QuarantineUsable)
            {
                notifications.Notify(preparation.QuarantineMessage);
            }
        }
        catch (Exception ex)
        {
            // 启动失败也必须给用户一句人话，并如实说明"没有执行任何清理"
            MessageBox.Show(
                $"SpaceMaid 启动失败：{ex.Message}{Environment.NewLine}{Environment.NewLine}本次没有执行任何清理或删除动作。",
                "SpaceMaid 启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
