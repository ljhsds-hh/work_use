using System.IO;
using System.Windows;
using SpaceMaid.App.Services;
using SpaceMaid.App.ViewModels;
using SpaceMaid.App.Views;
using SpaceMaid.Core;
using SpaceMaid.Core.Cli;

namespace SpaceMaid.App;

/// <summary>
/// 应用入口（组合根）。启动编排固定为：
/// ① 解析命令行：命中只读模式（<c>--dry-run</c>/<c>--report</c>/<c>--help</c>）就**不开界面**，跑完即退出；
/// ② 否则创建 <see cref="CoreServices"/>（唯一内核入口）；
/// ③ <c>Prepare()</c> 做启动自检：隔离区路径校验 → 日志滚动 → 隔离区账本自检 → 到期批次惰性释放；
/// ④ 组装平台服务（文件夹选择 / 确认框 / Growl / 打开目录）；
/// ⑤ 建 <see cref="MainViewModel"/> 并显示主窗。
///
/// 注意：命令行**不提供任何删除能力**（<see cref="CliOptions.ForbiddenSwitches"/> 里的开关一律报错）；
/// 删除动作永远由用户在界面里确认后执行（需求 3.9-5 / 4.3-5）。
/// </summary>
public partial class App : Application
{
    /// <summary>主窗关闭即退出（本工具"打开就用、用完就关"，不做托盘常驻）。</summary>
    public App() => ShutdownMode = ShutdownMode.OnMainWindowClose;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ① 命令行分支：给了参数就走命令，**绝不回落到开界面**
        //   （否则一次手滑的 `--clean` 会变成"打开工具 + 顺手做启动维护"，那可不是用户想要的）
        var cliOptions = CliOptions.Parse(e.Args);
        if (CliOptions.IsCommandLineInvocation(e.Args))
        {
            if (cliOptions.IsUsageError)
            {
                ConsoleOutput.Write(cliOptions.Message + Environment.NewLine + Environment.NewLine + CliOptions.HelpText);
                Shutdown(2);
                return;
            }

            RunCli(cliOptions);
            return;
        }

        try
        {
            // ② 内核组合根：设置读不到/读坏了都会回默认值，不会中断启动
            var core = CoreServices.Create();
            var bridge = new CoreServicesBridge(core);

            // ④ 平台服务：ViewModel 只认识这些接口，所以能在无 UI 的测试里跑
            var folderPicker = new FolderPickerService();
            var dialogs = new DialogService();
            var notifications = new GrowlNotificationService("SpaceMaidGrowl");
            var shell = new ShellService();

            // ⑤ 扫描与执行：内核实现，界面不复制任何判定逻辑
            var scanner = new CoreScanService(core);
            var executor = new CoreCleanExecutor(core);

            var viewModel = new MainViewModel(bridge, scanner, executor, dialogs, folderPicker, notifications, shell);

            // ③-a 先把主窗建出来并登记为 Application.MainWindow，**再**跑启动自检。
            //
            // 顺序不是随手写的：自检里可能弹"权限不足"对话框（需求 3.6-2），而 WPF 会把
            // **第一个显示出来的窗口**记为 Application.MainWindow；配合 ShutdownMode.OnMainWindowClose，
            // 那个对话框一被点掉，整个应用就跟着退出了——真机现象是"未提权启动 → 点掉提示 → 程序直接消失，
            // 主界面压根没出现"。先登记主窗，对话框就抢不走这个身份。
            var window = new MainWindow(viewModel, folderPicker, dialogs, notifications, shell);
            MainWindow = window;

            // ③-b 启动自检必须在窗口出现之前跑完（"到期释放"没有常驻进程，只能在这里做）
            var preparation = viewModel.Initialize();

            window.Show();

            if (!preparation.QuarantineUsable)
            {
                notifications.Notify(preparation.QuarantineMessage);
            }
        }
        catch (Exception ex)
        {
            // 启动失败也必须给用户一句人话，并如实说明"没有执行任何清理"。
            // 但**光有一句人话不够**：XAML 解析失败的真实原因（缺哪个资源键、哪个文件哪一行）
            // 全在内部异常里，只显示 ex.Message 等于让人无从下手——把完整异常链写进日志，
            // 并且额外落一份 D:\logs\SpaceMaid\startup-failure.log（日志本身不可用时也留得下证据）。
            var report = DescribeException(ex);
            TryWriteStartupFailure(report);

            MessageBox.Show(
                $"SpaceMaid 启动失败：{ex.Message}{Environment.NewLine}{Environment.NewLine}本次没有执行任何清理或删除动作。"
                + $"{Environment.NewLine}{Environment.NewLine}详细原因（含内部异常）已写入：{StartupFailurePath}",
                "SpaceMaid 启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>启动失败证据的落盘位置（用户报问题时让它直接给这个文件）。</summary>
    private static string StartupFailurePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "logs", "SpaceMaid", "startup-failure.log");

    private static string DescribeException(Exception ex)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"命令行：{string.Join(' ', Environment.GetCommandLineArgs())}");
        builder.AppendLine();

        var current = ex;
        var depth = 0;
        while (current is not null)
        {
            builder.AppendLine($"[{depth}] {current.GetType().FullName}: {current.Message}");
            if (current is System.Windows.Markup.XamlParseException markup)
            {
                builder.AppendLine($"    BaseUri={markup.BaseUri}");
                builder.AppendLine($"    LineNumber={markup.LineNumber} LinePosition={markup.LinePosition}");
            }

            builder.AppendLine(current.StackTrace);
            builder.AppendLine();
            current = current.InnerException;
            depth++;
        }

        return builder.ToString();
    }

    private static void TryWriteStartupFailure(string report)
    {
        try
        {
            var path = StartupFailurePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, report + new string('-', 80) + Environment.NewLine, System.Text.Encoding.UTF8);
        }
        catch (Exception)
        {
            // 连日志都写不了就只剩弹窗了——不能因为记录失败再抛一次
        }
    }

    /// <summary>
    /// 只读命令行分支：扫描导出清单 / 复核既有清单 / 打印帮助。**不做任何删除**，
    /// 完成即按退出码结束进程（0 成功、1 失败、2 用法错误）。
    /// </summary>
    private void RunCli(CliOptions options)
    {
        var exitCode = 1;
        string message;

        try
        {
            var core = CoreServices.Create();
            var result = new CliRunner(core).Run(options);
            exitCode = result.ExitCode;
            message = result.Message;
        }
        catch (Exception ex)
        {
            message = $"命令执行失败：{ex.Message}";
        }

        ConsoleOutput.Write(message);
        Shutdown(exitCode);
    }
}
