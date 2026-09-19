using System.ComponentModel;
using System.Windows;
using HandyControl.Controls;
using QuietRemind.Models;
using QuietRemind.Services;
using QuietRemind.ViewModels;
using MessageBox = HandyControl.Controls.MessageBox;

namespace QuietRemind.Views;

public partial class MainWindow : System.Windows.Window
{
    private readonly AppServices _services;
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm, AppServices services)
    {
        InitializeComponent();
        _vm = vm;
        _services = services;
        DataContext = vm;
        vm.AddRequested += () => OpenEditor(null);
        vm.EditRequested += task => OpenEditor(task);
        vm.DeleteRequested += ConfirmDelete;
        Closing += OnClosing;
        StateChanged += (_, _) =>
        {
            if (MaximizeGlyph is not null)
            {
                // 最大化显示“还原”图标，还原状态显示“最大化”图标
                MaximizeGlyph.Text = char.ConvertFromUtf32(WindowState == WindowState.Maximized ? 0xE923 : 0xE922);
            }
        };
    }

    private void OpenEditor(ReminderTask? existing)
    {
        var editorVm = new TaskEditorViewModel(existing);
        var win = new TaskEditorWindow(editorVm);
        if (win.ShowDialog() == true && editorVm.SavedTask is { } task)
        {
            if (existing is null)
            {
                _services.Data.Tasks.Add(task);
            }
            // 重建未来实例：编辑仅影响未触发实例，不影响已收尾历史（需求 2.3.2）
            _services.Planner.RebuildFuture(_services.Data.Occurrences, task);
            _services.Persist();
            _services.Log.Info($"任务保存：{task.Content}（{(existing is null ? "新增" : "编辑")}）");
            _vm.Refresh();
        }
    }

    private void ConfirmDelete(ReminderTask task)
    {
        var result = MessageBox.Show(
            $"确认删除任务「{task.Content}」？\n删除后不再提醒；已产生的历史记录将保留供查询。",
            "删除确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        // 需求 2.3.3 / 8.3：删除任务不追溯、不清除历史实例记录，仅停止参与提醒
        //（引擎按"任务列表中不存在"自然过滤其全部实例）
        _services.Data.Tasks.Remove(task);
        _services.Persist();
        _services.Log.Info($"删除任务：{task.Content}（历史实例保留）");
        _vm.Refresh();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // 关闭主窗口 = 最小化到托盘，不退出进程（需求 6.2.1）
        e.Cancel = true;
        Hide();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnToggleMaximize(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseToTray(object sender, RoutedEventArgs e) => Hide();

    /// <summary>提醒收尾等实例状态变化后，刷新列表派生数据（下次提醒 / 待处理数）。</summary>
    public void RefreshRows() => _vm.Refresh();
}
