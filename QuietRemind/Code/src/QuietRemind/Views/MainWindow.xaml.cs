using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using HandyControl.Controls;
using QuietRemind.Helpers;
using QuietRemind.Models;
using QuietRemind.Services;
using QuietRemind.ViewModels;
using MessageBox = HandyControl.Controls.MessageBox;

namespace QuietRemind.Views;

public partial class MainWindow : System.Windows.Window
{
    private readonly AppServices _services;
    private readonly MainViewModel _vm;
    private ReminderWindow? _previewWindow;
    private DispatcherTimer? _undoTimer;

    public MainWindow(MainViewModel vm, AppServices services)
    {
        InitializeComponent();
        // 默认尺寸适配当前工作区，避免小屏/高分屏上窗口超过桌面而被裁切
        var work = SystemParameters.WorkArea;
        Width = Math.Max(960, Math.Min(1280, work.Width - 48));
        Height = Math.Max(560, Math.Min(820, work.Height - 48));
        _vm = vm;
        _services = services;
        DataContext = vm;
        vm.AddRequested += () => OpenEditor(null);
        vm.EditRequested += task => OpenEditor(task);
        vm.HistoryRequested += OpenHistory;
        vm.PreviewRequested += ShowPreview;
        vm.RestartRequested += () => ((App)Application.Current).RestartForImport();
        vm.DeleteRequested += ConfirmDelete;
        Closing += OnClosing;
        SourceInitialized += (_, _) => WindowEffects.EnableRoundedCorners(this);
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        Loaded += (_, _) => UpdateResponsiveLayout();
        StateChanged += (_, _) =>
        {
            if (MaximizeGlyph is not null)
            {
                // 最大化显示“还原”图标，还原状态显示“最大化”图标
                MaximizeGlyph.Text = char.ConvertFromUtf32(WindowState == WindowState.Maximized ? 0xE923 : 0xE922);
            }
        };
    }

    /// <summary>
    /// 窄窗时侧栏收成图标轨道、内容边距收紧，避免 1024 宽下任务表被设置栏挤扁。
    /// 设置栏默认收起，需要时再从侧栏展开。
    /// </summary>
    private void UpdateResponsiveLayout()
    {
        var compact = ActualWidth > 0
            && (ActualWidth < 1060 || (SettingsPanel.Visibility == Visibility.Visible && ActualWidth < 1220));
        SidebarColumn.Width = new GridLength(compact ? 76 : 208);
        BrandPanel.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        BrandPanel.Margin = compact ? new Thickness(0) : new Thickness(16, 0, 0, 0);
        BrandText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        VersionPill.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        NavTasksText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        NavSettingsText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        NavTasksContent.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        NavSettingsContent.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        SidebarQuickAdd.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SidebarStatusCard.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SidebarFooterText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        PageContent.Margin = compact
            ? new Thickness(24, 22, 24, 20)
            : new Thickness(34, 26, 34, 24);
    }

    private void OpenEditor(ReminderTask? existing)
    {
        // 编辑前快照排期字段：只改内容时不重建实例，避免无谓重置 snooze/提前提醒标记
        var before = existing?.Clone();
        var editorVm = new TaskEditorViewModel(existing);
        var win = new TaskEditorWindow(editorVm) { Owner = this };
        if (win.ShowDialog() == true && editorVm.SavedTask is { } task)
        {
            if (existing is null)
            {
                _services.Data.Tasks.Add(task);
            }
            // 重建未来实例：编辑仅影响未触发实例，不影响已收尾历史（需求 2.3.2）；
            // notBefore 防止系统日期回拨时为已收尾历史日期重新生成实例
            if (before is null || !before.HasSameScheduleAs(task))
            {
                _services.Planner.RebuildFuture(_services.Data.Occurrences, task,
                    notBefore: _services.Data.Settings.LastPlannedDate);
            }
            _services.Persist();
            _services.Log.Info($"任务保存：{task.Content}（{(existing is null ? "新增" : "编辑")}）");
            _vm.Refresh();
        }
    }

    private void OpenHistory(ReminderTask task)
    {
        var win = new HistoryWindow(new HistoryViewModel(_services, task)) { Owner = this };
        win.ShowDialog();
    }

    private void ShowPreview()
    {
        if (_previewWindow is { IsVisible: true })
        {
            _previewWindow.Activate();
            return;
        }

        // 合成一条不进入数据集的预览实例：同一主屏全屏样式，预览窗可随时关闭且不落盘
        var now = _services.Clock.Now;
        var task = new ReminderTask
        {
            Content = "预览：这是一条任务提醒",
            Time = now.TimeOfDay,
            RecurrenceType = RecurrenceType.Daily,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var at = now.AddMinutes(2);
        var occurrence = new Occurrence
        {
            TaskId = task.Id,
            TriggerAt = at,
            OriginalTriggerAt = at,
        };

        var vm = new ReminderWindowViewModel(_services, [(task, occurrence)], isPreview: true);
        var win = new ReminderWindow(vm);
        _previewWindow = win;
        win.Closed += (_, _) =>
        {
            if (ReferenceEquals(_previewWindow, win))
            {
                _previewWindow = null;
            }
        };
        win.Show();
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

        // 需求 2.3.3 / 8.3：删除任务不追溯、不清除历史实例；保留一次撤销快照
        _vm.DeleteTask(task);
        StartUndoTimer();
    }

    private void StartUndoTimer()
    {
        _undoTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _undoTimer.Stop();
        _undoTimer.Tick -= OnUndoTick;
        _undoTimer.Tick += OnUndoTick;
        _undoTimer.Start();
    }

    private void OnUndoTick(object? sender, EventArgs e)
    {
        _undoTimer?.Stop();
        _vm.DismissUndo();
    }

    private void OnUndoClicked(object sender, RoutedEventArgs e)
    {
        _undoTimer?.Stop();
        // 撤销动作由 Command 执行，这里只负责停表
    }

    private void OnDismissUndo(object sender, RoutedEventArgs e)
    {
        _undoTimer?.Stop();
        _vm.DismissUndo();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // 关闭主窗口 = 最小化到托盘，不退出进程（需求 6.2.1）
        e.Cancel = true;
        Hide();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnQuickAddHint(object sender, MouseButtonEventArgs e)
        => (Application.Current as App)?.ShowQuickAdd();

    private void OnToggleMaximize(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseToTray(object sender, RoutedEventArgs e) => Hide();

    private void OnTasksRail(object sender, RoutedEventArgs e)
    {
        // 任务区是主工作区：点击后保持高亮，并收起设置检查器
        TasksRailButton.IsChecked = true;
        SetSettingsVisible(false);
    }

    private void OnToggleSettings(object sender, RoutedEventArgs e)
        => SetSettingsVisible(SettingsPanel.Visibility != Visibility.Visible);

    private void SetSettingsVisible(bool visible)
    {
        // 设置面板首次展开会生成外观分组的项容器；任何异常都不允许把主界面带崩
        try
        {
            SettingsPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            SettingsColumn.Width = visible ? new GridLength(360) : new GridLength(0);
            SettingsRailButton.IsChecked = visible;
            if (visible)
            {
                SettingsScroll.ScrollToTop();
            }
            UpdateResponsiveLayout();
        }
        catch (Exception ex)
        {
            _services.Log.Error($"切换设置面板失败（visible={visible}）", ex);
            try
            {
                SettingsPanel.Visibility = Visibility.Collapsed;
                SettingsColumn.Width = new GridLength(0);
                SettingsRailButton.IsChecked = false;
            }
            catch
            {
                // 回退失败时保持窗口其余部分可用
            }
        }
    }

    /// <summary>提醒收尾等实例状态变化后，刷新列表派生数据（下次提醒 / 待处理数）。</summary>
    public void RefreshRows() => _vm.Refresh();
}
