using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using QuietRemind.ViewModels;

namespace QuietRemind.Views;

public partial class ReminderWindow : Window
{
    private readonly ReminderWindowViewModel _vm;
    private bool _allSettled;

    public ReminderWindow(ReminderWindowViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.AllSettled += OnAllSettled;
        Closing += (_, e) =>
        {
            // 未全部收尾禁止关闭（需求 3.2.5：不允许 ESC / Alt+F4 直接关闭）
            if (!_allSettled)
            {
                e.Cancel = true;
            }
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // 浮层定位：主屏工作区右下角，留 24px 边距
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 24;
        Top = workArea.Bottom - Height - 24;
    }

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // 非按下状态竞争：忽略
            }
        }
    }

    private void OnAllSettled()
    {
        _allSettled = true;
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // 屏蔽 ESC 与 Alt+F4（需求 3.2.5）
        if (e.Key == Key.Escape || (e.Key == Key.System && e.SystemKey == Key.F4))
        {
            e.Handled = true;
        }
    }
}
