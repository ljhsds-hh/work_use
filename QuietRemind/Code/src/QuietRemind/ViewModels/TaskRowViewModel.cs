using QuietRemind.Helpers;
using QuietRemind.Models;

namespace QuietRemind.ViewModels;

/// <summary>任务列表行视图模型：纯展示派生，增删改通过回调上抛。</summary>
public sealed class TaskRowViewModel : ViewModelBase
{
    private readonly ReminderTask _task;
    private readonly Action _persist;
    private readonly Action<ReminderTask> _historyRequested;
    private readonly Action<ReminderTask> _editRequested;
    private readonly Action<ReminderTask> _deleteRequested;
    private bool _isTodayDue;
    private bool _isEnded;

    public TaskRowViewModel(ReminderTask task, Action persist, Action<ReminderTask> historyRequested,
        Action<ReminderTask> editRequested, Action<ReminderTask> deleteRequested)
    {
        _task = task;
        _persist = persist;
        _historyRequested = historyRequested;
        _editRequested = editRequested;
        _deleteRequested = deleteRequested;
        HistoryCommand = new RelayCommand(() => _historyRequested(_task));
        EditCommand = new RelayCommand(() => _editRequested(_task));
        DeleteCommand = new RelayCommand(() => _deleteRequested(_task));
    }

    public Guid Id => _task.Id;
    public string Content => _task.Content;
    public string TimeText => _task.Time.ToString(@"hh\:mm");
    public RecurrenceType Recurrence => _task.RecurrenceType;
    public string RecurrenceText => DescribeRecurrence(_task);
    /// <summary>列表“循环规则”列：省略时刻，避免与“下次提醒”列重复并减少截断。</summary>
    public string RuleText => DescribeRule(_task);
    public bool IsEnabled
    {
        get => _task.Enabled;
        set
        {
            if (_task.Enabled == value)
            {
                return;
            }
            _task.Enabled = value;
            _task.UpdatedAt = DateTime.Now;
            _persist();
            OnPropertyChanged();
        }
    }

    /// <summary>下一次未处理提醒时刻（今日 → “今日 HH:mm”；无 → “—”）。</summary>
    public string NextRemindText
    {
        get => _nextRemindText;
        set => SetProperty(ref _nextRemindText, value);
    }
    private string _nextRemindText = "—";

    /// <summary>今日有待提醒/已错过实例（醒目区分，需求 6.1）。</summary>
    public bool IsTodayDue
    {
        get => _isTodayDue;
        set => SetProperty(ref _isTodayDue, value);
    }

    /// <summary>单次任务所有实例均已收尾 → 已结束。</summary>
    public bool IsEnded
    {
        get => _isEnded;
        set => SetProperty(ref _isEnded, value);
    }

    public RelayCommand HistoryCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand DeleteCommand { get; }

    public static string DescribeRecurrence(ReminderTask t)
    {
        var time = t.Time.ToString(@"hh\:mm");
        return t.RecurrenceType switch
        {
            RecurrenceType.Once => $"单次 {t.SingleDate:yyyy-MM-dd} {time}",
            RecurrenceType.Daily => t.Interval > 1
                ? $"每 {t.Interval} 天 {time}"
                : $"每天 {time}",
            RecurrenceType.Weekly => $"{(t.Interval > 1 ? $"每 {t.Interval} 周" : "每周")} {FormatWeekDays(t.WeekDays)} {time}",
            RecurrenceType.Monthly => t.Interval > 1
                ? $"每 {t.Interval} 个月 {t.MonthDay} 日 {time}"
                : $"每月 {t.MonthDay} 日 {time}",
            RecurrenceType.Yearly => $"每年 {t.Month} 月 {t.MonthDay} 日 {time}",
            _ => time,
        };
    }

    /// <summary>不含时刻的循环规则描述（列表紧凑展示用）。</summary>
    public static string DescribeRule(ReminderTask t) => t.RecurrenceType switch
    {
        RecurrenceType.Once => $"单次 {t.SingleDate:yyyy-MM-dd}",
        RecurrenceType.Daily => t.Interval > 1 ? $"每 {t.Interval} 天" : "每天",
        RecurrenceType.Weekly => $"{(t.Interval > 1 ? $"每 {t.Interval} 周" : "每周")} {FormatWeekDays(t.WeekDays)}",
        RecurrenceType.Monthly => t.Interval > 1 ? $"每 {t.Interval} 个月 {t.MonthDay} 日" : $"每月 {t.MonthDay} 日",
        RecurrenceType.Yearly => $"每年 {t.Month} 月 {t.MonthDay} 日",
        _ => "",
    };

    private static string FormatWeekDays(DayOfWeek[] days)
    {
        var names = (days ?? []).Select(d => d switch
        {
            DayOfWeek.Monday => "一",
            DayOfWeek.Tuesday => "二",
            DayOfWeek.Wednesday => "三",
            DayOfWeek.Thursday => "四",
            DayOfWeek.Friday => "五",
            DayOfWeek.Saturday => "六",
            _ => "日",
        });
        return string.Join("、", names);
    }
}
