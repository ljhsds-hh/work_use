using SpaceMaid.App.ViewModels;

namespace SpaceMaid.App.Views;

/// <summary>
/// 设置窗口（隔离区位置 / 保留天数 / 回收站范围 / 立即清空隔离区 / 报告与日志入口）。
/// 全部逻辑在 <see cref="SettingsViewModel"/> 里，这里只做"装配 + 关窗"。
/// 基类必须是 <c>System.Windows.Window</c>（XAML 生成的 g.cs 用它）。
/// </summary>
public partial class SettingsWindow : System.Windows.Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; }

    private void OnCloseClick(object sender, System.Windows.RoutedEventArgs e) => Close();
}
