using System.Collections.ObjectModel;
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
        Refresh();
    }

    public ObservableCollection<TaskRowViewModel> Tasks { get; } = [];

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

    /// <summary>数据变更后重建任务行并刷新派生状态。</summary>
    public void Refresh()
    {
        var now = _services.Clock.Now;
        var today = DateOnly.FromDateTime(now);

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
            Tasks.Add(row);
        }
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
