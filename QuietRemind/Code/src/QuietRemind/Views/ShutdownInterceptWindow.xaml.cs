using System.Windows;
using HandyControl.Controls;
using QuietRemind.ViewModels;

namespace QuietRemind.Views;

/// <summary>
/// 关机前拦截提醒窗口（需求 4 章）。
/// DialogResult：true = 取消关机；false = 仍要关机放行。
/// </summary>
public partial class ShutdownInterceptWindow : HandyControl.Controls.Window
{
    public ShutdownInterceptWindow(ShutdownInterceptWindowViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OnCancelShutdown(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnProceedShutdown(object sender, RoutedEventArgs e) => DialogResult = false;
}
