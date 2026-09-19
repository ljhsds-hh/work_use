using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using QuietRemind.Helpers;
using QuietRemind.Models;
using QuietRemind.Services;

namespace QuietRemind.ViewModels;

/// <summary>主窗口视图模型：任务列表 + 设置区（需求 6.1）。</summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public MainViewModel(AppServices services)
    {
        _services = services;
        AddCommand = new RelayCommand(OnAdd);
        PickImageCommand = new RelayCommand(OnPickImage);
        ClearImageCommand = new RelayCommand(OnClearImage);
        Refresh();
    }

    public ObservableCollection<TaskRowViewModel> Tasks { get; } = [];

    /// <summary>今日待处理实例数（Pending/Missed），列表标题旁胶囊展示。</summary>
    public string PendingCountText
    {
        get => _pendingCountText;
        set => SetProperty(ref _pendingCountText, value);
    }
    private string _pendingCountText = "0 项待处理";

    /// <summary>无任务时的空态提示。</summary>
    public bool HasNoTask
    {
        get => _hasNoTask;
        set => SetProperty(ref _hasNoTask, value);
    }
    private bool _hasNoTask;

    /// <summary>请求打开新增编辑器（View 层订阅实现）。</summary>
    public event Action? AddRequested;

    /// <summary>请求打开编辑编辑器。</summary>
    public event Action<ReminderTask>? EditRequested;

    /// <summary>请求删除确认。</summary>
    public event Action<ReminderTask>? DeleteRequested;

    public RelayCommand AddCommand { get; }

    public bool GuardEnabled
    {
        get => _services.Data.Settings.GuardEnabled;
        set
        {
            _services.Data.Settings.GuardEnabled = value;
            // 需求 6.3：关闭即注销计划任务，不再自启、不再守护；开启则（重新）注册
            if (value)
            {
                _services.Guard.EnsureRegistered();
            }
            else
            {
                _services.Guard.Unregister();
            }
            _services.PersistSettings();
            OnPropertyChanged();
        }
    }

    public int Snooze1
    {
        get => GetSnooze(0);
        set => SetSnooze(0, value);
    }

    public int Snooze2
    {
        get => GetSnooze(1);
        set => SetSnooze(1, value);
    }

    public int Snooze3
    {
        get => GetSnooze(2);
        set => SetSnooze(2, value);
    }

    private int GetSnooze(int index) =>
        index < _services.Data.Settings.SnoozeMinutes.Length ? _services.Data.Settings.SnoozeMinutes[index] : 5;

    private void SetSnooze(int index, int value)
    {
        var minutes = _services.Data.Settings.SnoozeMinutes;
        if (index < minutes.Length)
        {
            minutes[index] = Math.Clamp(value, 1, 120);
        }
        _services.PersistSettings();
        OnPropertyChanged();
    }

    /// <summary>提醒遮罩暗化不透明度（0~90%）。</summary>
    public int ReminderDimPercent
    {
        get => _services.Data.Settings.ReminderDimPercent;
        set
        {
            _services.Data.Settings.ReminderDimPercent = Math.Clamp(value, 0, 90);
            _services.PersistSettings();
            OnPropertyChanged();
        }
    }

    /// <summary>背景图片模糊半径（0~40px）。</summary>
    public int ReminderBlurRadius
    {
        get => _services.Data.Settings.ReminderBlurRadius;
        set
        {
            _services.Data.Settings.ReminderBlurRadius = Math.Clamp(value, 0, 40);
            _services.PersistSettings();
            OnPropertyChanged();
        }
    }

    /// <summary>已设置的背景图展示名（未设置为提示文字）。</summary>
    public string ReminderImageText =>
        string.IsNullOrEmpty(_services.Data.Settings.ReminderImagePath)
            ? "未设置（使用默认深色底）"
            : Path.GetFileName(_services.Data.Settings.ReminderImagePath);

    public RelayCommand PickImageCommand { get; }
    public RelayCommand ClearImageCommand { get; }

    private void OnPickImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择提醒背景图片",
            Filter = "图片文件 (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            // 拷贝到数据目录统一管理，避免源文件移动/删除后背景失效
            var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            var target = Path.Combine(_services.DataDir, $"reminder-bg{ext}");
            foreach (var stale in Directory.GetFiles(_services.DataDir, "reminder-bg.*"))
            {
                if (!string.Equals(stale, target, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(stale);
                }
            }
            File.Copy(dialog.FileName, target, overwrite: true);
            _services.Data.Settings.ReminderImagePath = target;
            _services.PersistSettings();
            _services.Log.Info($"提醒背景图已设置：{target}");
        }
        catch (Exception ex)
        {
            _services.Log.Error("背景图设置失败", ex);
            System.Windows.MessageBox.Show($"背景图设置失败：{ex.Message}", "QuietRemind",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        OnPropertyChanged(nameof(ReminderImageText));
        OnPropertyChanged(nameof(HasCustomImage));
    }

    private void OnClearImage()
    {
        try
        {
            var path = _services.Data.Settings.ReminderImagePath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 删除失败不影响清除设置
        }
        _services.Data.Settings.ReminderImagePath = null;
        _services.PersistSettings();
        _services.Log.Info("提醒背景图已清除");
        OnPropertyChanged(nameof(ReminderImageText));
        OnPropertyChanged(nameof(HasCustomImage));
    }

    public bool HasCustomImage => !string.IsNullOrEmpty(_services.Data.Settings.ReminderImagePath);

    /// <summary>数据变更后重建任务行并刷新派生状态。</summary>
    public void Refresh()
    {
        var now = _services.Clock.Now;
        var today = DateOnly.FromDateTime(now);
        var todayPending = 0;

        Tasks.Clear();
        foreach (var task in _services.Data.Tasks)
        {
            var row = new TaskRowViewModel(task, OnTaskToggled, t => EditRequested?.Invoke(t), t => DeleteRequested?.Invoke(t));
            var taskOccs = _services.Data.Occurrences.Where(o => o.TaskId == task.Id).ToList();
            row.IsTodayDue = taskOccs.Any(o => DateOnly.FromDateTime(o.TriggerAt) == today
                && o.State is OccurrenceState.Pending or OccurrenceState.Missed);
            row.IsEnded = task.RecurrenceType == RecurrenceType.Once
                && taskOccs.All(o => o.State is OccurrenceState.Completed or OccurrenceState.Skipped)
                && taskOccs.Count > 0;

            // 下一次未处理提醒时刻（列表“下次提醒”列）
            var next = taskOccs
                .Where(o => o.State is OccurrenceState.Pending or OccurrenceState.Missed)
                .Select(o => o.TriggerAt)
                .OrderBy(t => t)
                .FirstOrDefault();
            row.NextRemindText = next == default
                ? "—"
                : DateOnly.FromDateTime(next) switch
                {
                    var d when d == today => $"今日 {next:HH:mm}",
                    var d when d == today.AddDays(1) => $"明日 {next:HH:mm}",
                    var d => $"{d:M月d日} {next:HH:mm}",
                };

            if (row.IsTodayDue)
            {
                todayPending++;
            }
            Tasks.Add(row);
        }

        PendingCountText = $"{todayPending} 项待处理";
        HasNoTask = Tasks.Count == 0;
    }

    private void OnAdd() => AddRequested?.Invoke();

    /// <summary>启用/停用切换后：补生成（启用）实例并按域落盘。</summary>
    private void OnTaskToggled()
    {
        _services.Planner.EnsureUpTo(_services.Data.Occurrences, _services.Data.Tasks);
        _services.PersistTasks();
        _services.PersistOccurrences();
    }
}
