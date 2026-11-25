using QuietRemind.Helpers;
using System.Windows;
using System.Windows.Input;
using QuietRemind.ViewModels;

namespace QuietRemind.Views;

public partial class QuickAddWindow : Window
{
    private readonly QuickAddViewModel _vm;

    public QuickAddWindow(QuickAddViewModel vm)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowEffects.EnableRoundedCorners(this);
        _vm = vm;
        DataContext = vm;
        // 默认每天；分段选择由代码同步，避免单选组单向绑定丢失选中态
        (vm.IsOnce ? OnceRadio : DailyRadio).IsChecked = true;
        Loaded += (_, _) => ContentBox.Focus();
    }

    private void OnOnceChecked(object sender, RoutedEventArgs e) => _vm.IsOnce = true;

    private void OnDailyChecked(object sender, RoutedEventArgs e) => _vm.IsOnce = false;

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _vm.Save();
        if (_vm.SavedTask is not null)
        {
            DialogResult = true;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            // 输入内容后回车即保存，Ctrl+Alt+N 呼出后可直接录完退出
            e.Handled = true;
            OnSave(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            OnCancel(sender, e);
        }
    }
}
