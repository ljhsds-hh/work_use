using System.Windows;
using System.Windows.Input;
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
            // 未全部收尾禁止关闭（需求 3.2.5：不允许 ESC / 遮罩点击 / Alt+F4 直接关闭）
            if (!_allSettled)
            {
                e.Cancel = true;
            }
        };
    }

    private void OnAllSettled()
    {
        _allSettled = true;
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.System && e.SystemKey == Key.F4)
        {
            e.Handled = true; // 屏蔽 ESC 与 Alt+F4
        }
    }
}
