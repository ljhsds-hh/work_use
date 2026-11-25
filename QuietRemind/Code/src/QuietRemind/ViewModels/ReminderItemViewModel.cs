using QuietRemind.Helpers;
using QuietRemind.Models;
using QuietRemind.Services;
using QuietRemind.ViewModels;

namespace QuietRemind.ViewModels;

public sealed class SnoozeOptionViewModel(string label, RelayCommand command)
{
    public string Label { get; } = label;
    public RelayCommand Command { get; } = command;
}

/// <summary>提醒窗口单条任务条目：内容 + 计划时刻 + 收尾按钮（需求 3.3）。</summary>
public sealed class ReminderItemViewModel : ViewModelBase
{
    private readonly ReminderTask _task;
    private readonly ReminderEngine _engine;
    private bool _isSettled;

    public ReminderItemViewModel(ReminderTask task, Occurrence occurrence, ReminderEngine engine,
        int[] snoozeMinutes, bool isPreview = false)
    {
        _task = task;
        _engine = engine;
        IsPreview = isPreview;
        Occurrence = occurrence;
        IsMissed = occurrence.State == OccurrenceState.Missed;
        Content = task.Content;

        CompleteCommand = new RelayCommand(() => Settle(SettleAction.Complete, 0));
        SkipCommand = new RelayCommand(() => Settle(SettleAction.Skip, 0));
        SnoozeOptions = (snoozeMinutes ?? [5, 10, 30])
            .Select(m => new SnoozeOptionViewModel($"{m} 分钟", new RelayCommand(() => Settle(SettleAction.Snooze, m))))
            .ToList();
    }

    public Occurrence Occurrence { get; }
    public string Content { get; }
    public bool IsPreview { get; }
    /// <summary>预览模式只展示样式，隐藏收尾操作并禁止写入。</summary>
    public bool ShowActions => !IsPreview;
    public string TimeText => Occurrence.OriginalTriggerAt.ToString("yyyy-MM-dd HH:mm");
    public bool IsMissed { get; }
    /// <summary>标题旁徽标：已错过（红）或循环规则（蓝）。</summary>
    public string BadgeText => IsMissed ? "已错过" : TaskRowViewModel.DescribeRecurrence(_task);
    public bool IsSettled => _isSettled;
    public List<SnoozeOptionViewModel> SnoozeOptions { get; }

    /// <summary>该条目收尾完成（窗口据此判断是否全部收尾）。</summary>
    public event Action<ReminderItemViewModel>? Settled;

    public RelayCommand CompleteCommand { get; }
    public RelayCommand SkipCommand { get; }

    private void Settle(SettleAction action, int minutes)
    {
        if (_isSettled || IsPreview)
        {
            return;
        }
        _engine.Settle(Occurrence, action, minutes);
        _isSettled = true;
        OnPropertyChanged(nameof(IsSettled));
        Settled?.Invoke(this);
    }
}
