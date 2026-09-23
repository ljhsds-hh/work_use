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
        _vm = vm;
        DataContext = vm;
    }

    private void OnRecurrenceChecked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton { Tag: string tag }
            && Enum.TryParse<RecurrenceType>(tag, out var type))
        {
            _vm.Recurrence = type;
        }
    }

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
