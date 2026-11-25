using QuietRemind.Helpers;
using System.Windows;
using QuietRemind.Models;
using QuietRemind.ViewModels;

namespace QuietRemind.Views;

public partial class TaskEditorWindow : Window
{
    private readonly TaskEditorViewModel _vm;

    public TaskEditorWindow(TaskEditorViewModel vm)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowEffects.EnableRoundedCorners(this);
        _vm = vm;
        DataContext = vm;
        // 小屏适配：表单区按工作区高度收缩，底部保存/取消始终可见
        EditorScroll.MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 52 - 64 - 24);
        // 分段选择由代码同步初始值（单选组下仅靠单向绑定可能丢失选中态）
        (vm.Recurrence switch
        {
            RecurrenceType.Once => OnceRadio,
            RecurrenceType.Daily => DailyRadio,
            RecurrenceType.Weekly => WeeklyRadio,
            RecurrenceType.Monthly => MonthlyRadio,
            _ => YearlyRadio,
        }).IsChecked = true;
    }

    private void OnRecurrenceChecked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton { Tag: string tag }
            && Enum.TryParse<RecurrenceType>(tag, out var type))
        {
            _vm.Recurrence = type;
        }
    }

    private void OnWorkdays(object sender, RoutedEventArgs e) => _vm.ApplyWorkdays();

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _vm.Save();
        if (_vm.SavedTask is not null)
        {
            DialogResult = true;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
