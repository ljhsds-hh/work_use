using QuietRemind.Helpers;
using QuietRemind.Models;

namespace QuietRemind.ViewModels;

/// <summary>任务列表行视图模型：纯展示派生，增删改通过回调上抛。</summary>
public sealed class TaskRowViewModel : ViewModelBase
{
    private readonly ReminderTask _task;
    private readonly Action _persist;
    private readonly Action<ReminderTask> _editRequested;
    private readonly Action<ReminderTask> _deleteRequested;
    private bool _isTodayDue;
    private bool _isEnded;

    public TaskRowViewModel(ReminderTask task, Action persist, Action<ReminderTask> editRequested, Action<ReminderTask> deleteRequested)
    {
        _task = task;
        _persist = persist;
        _editRequested = editRequested;
        _deleteRequested = deleteRequested;
        EditCommand = new RelayCommand(() => _editRequested(_task));
        DeleteCommand = new RelayCommand(() => _deleteRequested(_task));
    }

    public Guid Id => _task.Id;
    public string Content => _task.Content;
    public string TimeText => _task.Time.ToString(@"hh\:mm");
    public RecurrenceType Recurrence => _task.RecurrenceType;
    public string RecurrenceText => DescribeRecurrence(_task);
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

    public RelayCommand EditCommand { get; }
    public RelayCommand DeleteCommand { get; }

    public static string DescribeRecurrence(ReminderTask t)
    {
        var time = t.Time.ToString(@"hh\:mm");
        return t.RecurrenceType switch
        {
            RecurrenceType.Once => $"单次 {t.SingleDate:yyyy-MM-dd} {time}",
            RecurrenceType.Daily => $"每天 {time}",
            RecurrenceType.Weekly => $"每周 {FormatWeekDays(t.WeekDays)} {time}",
            RecurrenceType.Monthly => $"每月 {t.MonthDay} 日 {time}",
            _ => time,
        };
    }

    private static string FormatWeekDays(DayOfWeek[] days)
    {
        var names = days.Select(d => d switch
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
