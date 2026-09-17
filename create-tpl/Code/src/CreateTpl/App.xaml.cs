using System.IO;
using System.Windows;

namespace CreateTpl;

/// <summary>
/// 应用程序入口：装载 HandyControl 主题，注册全局异常兜底，处理启动参数。
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 启动参数 --light：以亮色主题启动（默认暗色）
        foreach (var arg in e.Args)
        {
            if (string.Equals(arg, "--light", StringComparison.OrdinalIgnoreCase))
            {
                Helpers.ThemeService.Apply(dark: false);
            }
        }

        base.OnStartup(e);

        // 全局异常兜底：任何未处理异常均给出中文提示，程序不闪退（需求 4：稳定性）
        DispatcherUnhandledException += (_, args) =>
        {
            // 异常落盘便于问题诊断，日志体积可忽略
            try
            {
                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), "create-tpl-error.log"),
                    DateTime.Now + Environment.NewLine + args.Exception);
            }
            catch
            {
                // 日志写入失败不影响兜底流程
            }

            HandyControl.Controls.Growl.Error("程序发生未预期异常，已自动处理：" + args.Exception.Message);
            args.Handled = true;
        };
    }
}
