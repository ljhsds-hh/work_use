using System.IO;
using System.Windows;
using System.Windows.Threading;
using DllTool.App.ViewModels;
using DllTool.Core;
using DllTool.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DllTool.App;

/// <summary>
/// 应用入口：组装依赖注入容器、注册全局异常处理。
/// </summary>
public partial class App : Application
{
    private const string LogRootDirectory = @"D:\logs";
    private const string ApplicationName = "DllTool";

    private ServiceProvider? _services;
    private ILogger<App>? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _services = ConfigureServices();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        _logger = _services.GetRequiredService<ILogger<App>>();
        _logger.LogInformation("===== 应用启动 =====");
        _logger.LogInformation("应用版本：{Version}", typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown");
        _logger.LogInformation("日志目录：{Directory}", Path.Combine(LogRootDirectory, ApplicationName));

        var mainWindow = _services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("===== 应用退出 =====");
        _services?.GetRequiredService<DllTool.Infrastructure.Logging.FileLoggerProvider>().Flush();
        _services?.Dispose();
        base.OnExit(e);
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddDllToolCore();
        services.AddDllToolInfrastructure(LogRootDirectory, ApplicationName);

        services.AddSingleton<DllTool.App.Services.IOverwriteConfirmation, DllTool.App.Services.MessageOverwriteConfirmation>();
        services.AddSingleton<DllTool.App.Services.AppSettings>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogCritical(e.Exception, "未处理的UI线程异常：{Message}", e.Exception.Message);
        e.Handled = true;
        MessageBox.Show(
            $"程序发生未处理异常：{e.Exception.Message}\n\n详细堆栈已记录至日志文件。",
            "异常提示",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            _logger?.LogCritical(ex, "未处理的后台线程异常：{Message}", ex.Message);
        }
    }
}
