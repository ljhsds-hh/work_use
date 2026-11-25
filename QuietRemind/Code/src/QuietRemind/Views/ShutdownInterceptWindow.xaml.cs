using QuietRemind.Helpers;
using System.ComponentModel;
using System.Windows;
using QuietRemind.ViewModels;

namespace QuietRemind.Views;

/// <summary>
/// 关机前拦截提醒窗口（需求 4 章）。
/// DialogResult：true = 取消关机；false = 仍要关机放行。
/// 未显式选择时关闭窗口（X / Alt+F4）一律按【取消关机】处理，避免隐式放行。
/// </summary>
public partial class ShutdownInterceptWindow : System.Windows.Window
{
    private bool _choiceMade;

    public ShutdownInterceptWindow(ShutdownInterceptWindowViewModel vm)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowEffects.EnableRoundedCorners(this);
        MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 48);
        DataContext = vm;
    }

    private void OnCancelShutdown(object sender, RoutedEventArgs e)
    {
        _choiceMade = true;
        DialogResult = true;
    }

    private void OnProceedShutdown(object sender, RoutedEventArgs e)
    {
        _choiceMade = true;
        DialogResult = false;
    }

    private void OnCloseCaption(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_choiceMade)
        {
            return;
        }

        // 关闭按钮/Alt+F4 等未明确选择的关闭动作：中止关闭，改为“取消关机”结果
        e.Cancel = true;
        _choiceMade = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (IsVisible)
            {
                DialogResult = true;
            }
        }));
    }
}
