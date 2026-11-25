using QuietRemind.Helpers;
using QuietRemind.Models;

namespace QuietRemind.ViewModels;

/// <summary>新增/编辑任务表单视图模型（需求 2.1 / 2.2）。</summary>
public sealed class TaskEditorViewModel : ViewModelBase
{
    private readonly ReminderTask? _existing;
    private string _content;
    private RecurrenceType _recurrence;
    private DateTime? _singleDate;
    private DateTime? _startDate;
    private DateTime? _endDate;
    private DateTime? _selectedTime;
    private int _monthDay = 1;
    private int _month = 1;
    private int _interval = 1;
    private string? _errorText;
    private bool[] _weekDays = new bool[7];

    public TaskEditorViewModel(ReminderTask? existing)
    {
        _existing = existing;
        _content = existing?.Content ?? "";
        _recurrence = existing?.RecurrenceType ?? RecurrenceType.Daily;
        _singleDate = existing?.SingleDate is { } d ? d.ToDateTime(TimeOnly.MinValue) : null;
        _startDate = existing?.StartDate is { } s ? s.ToDateTime(TimeOnly.MinValue) : null;
        _endDate = existing?.EndDate is { } e ? e.ToDateTime(TimeOnly.MinValue) : null;
        _selectedTime = DateTime.Today.Add(existing?.Time ?? new TimeSpan(9, 0, 0));
        _monthDay = existing?.MonthDay ?? 1;
        _month = existing?.Month ?? 1;
        _interval = Math.Max(1, existing?.Interval ?? 1);
        if (existing?.WeekDays is { Length: > 0 } days)
        {
            foreach (var day in days)
            {
                // 脏数据可能包含未定义枚举值，直接索引会越界导致编辑窗打不开
                if (Enum.IsDefined(typeof(DayOfWeek), day))
                {
                    _weekDays[(int)day] = true;
                }
            }
        }
    }

    public string Content
    {
        get => _content;
        set => SetProperty(ref _content, value);
    }

    public string TitleText => _existing is null ? "新增任务" : "编辑任务";

    public RecurrenceType Recurrence
    {
        get => _recurrence;
        set
        {
            if (SetProperty(ref _recurrence, value))
            {
                if (_interval > MaxInterval)
                {
                    _interval = MaxInterval;
                    OnPropertyChanged(nameof(Interval));
                }
                OnPropertyChanged(nameof(IsOnce));
                OnPropertyChanged(nameof(IsDaily));
                OnPropertyChanged(nameof(IsWeekly));
                OnPropertyChanged(nameof(IsMonthly));
                OnPropertyChanged(nameof(IsYearly));
                OnPropertyChanged(nameof(IsRecurring));
                OnPropertyChanged(nameof(IsIntervalVisible));
                OnPropertyChanged(nameof(MaxInterval));
                OnPropertyChanged(nameof(IntervalUnitText));
            }
        }
    }

    public bool IsOnce => _recurrence == RecurrenceType.Once;
    public bool IsDaily => _recurrence == RecurrenceType.Daily;
    public bool IsWeekly => _recurrence == RecurrenceType.Weekly;
    public bool IsMonthly => _recurrence == RecurrenceType.Monthly;
    public bool IsYearly => _recurrence == RecurrenceType.Yearly;
    public bool IsRecurring => _recurrence != RecurrenceType.Once;

    /// <summary>间隔输入仅对每天/每周/每月生效。</summary>
    public bool IsIntervalVisible => _recurrence is RecurrenceType.Daily or RecurrenceType.Weekly or RecurrenceType.Monthly;

    public int MaxInterval => _recurrence switch
    {
        RecurrenceType.Daily => 365,
        RecurrenceType.Weekly => 52,
        RecurrenceType.Monthly => 24,
        _ => 1,
    };

    public string IntervalUnitText => _recurrence switch
    {
        RecurrenceType.Daily => "天",
        RecurrenceType.Weekly => "周",
        RecurrenceType.Monthly => "个月",
        _ => "",
    };

    public DateTime? SingleDate
    {
        get => _singleDate;
        set => SetProperty(ref _singleDate, value);
    }

    /// <summary>循环开始日期（仅循环规则；null = 从今天开始）。</summary>
    public DateTime? StartDate
    {
        get => _startDate;
        set => SetProperty(ref _startDate, value);
    }

    /// <summary>循环结束日期（含当日；null = 长期有效）。</summary>
    public DateTime? EndDate
    {
        get => _endDate;
        set => SetProperty(ref _endDate, value);
    }

    /// <summary>重复间隔倍数（每 N 天/周/月）。</summary>
    public int Interval
    {
        get => _interval;
        set
        {
            if (SetProperty(ref _interval, Math.Clamp(value, 1, Math.Max(1, MaxInterval))))
            {
                OnPropertyChanged(nameof(IntervalUnitText));
            }
        }
    }

    /// <summary>每年提醒的月份（1~12）。</summary>
    public int Month
    {
        get => _month;
        set => SetProperty(ref _month, Math.Clamp(value, 1, 12));
    }

    public DateTime? SelectedTime
    {
        get => _selectedTime;
        set => SetProperty(ref _selectedTime, value);
    }

    public int MonthDay
    {
        get => _monthDay;
        set => SetProperty(ref _monthDay, value);
    }

    public string? ErrorText
    {
        get => _errorText;
        set
        {
            if (SetProperty(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => _errorText is not null;

    public bool Mon { get => _weekDays[1]; set { _weekDays[1] = value; OnPropertyChanged(); } }
    public bool Tue { get => _weekDays[2]; set { _weekDays[2] = value; OnPropertyChanged(); } }
    public bool Wed { get => _weekDays[3]; set { _weekDays[3] = value; OnPropertyChanged(); } }
    public bool Thu { get => _weekDays[4]; set { _weekDays[4] = value; OnPropertyChanged(); } }
    public bool Fri { get => _weekDays[5]; set { _weekDays[5] = value; OnPropertyChanged(); } }
    public bool Sat { get => _weekDays[6]; set { _weekDays[6] = value; OnPropertyChanged(); } }
    public bool Sun { get => _weekDays[0]; set { _weekDays[0] = value; OnPropertyChanged(); } }

    /// <summary>一键勾选周一至周五（工作日预设）。</summary>
    public void ApplyWorkdays()
    {
        _weekDays = [false, true, true, true, true, true, false];
        OnPropertyChanged(nameof(Mon));
        OnPropertyChanged(nameof(Tue));
        OnPropertyChanged(nameof(Wed));
        OnPropertyChanged(nameof(Thu));
        OnPropertyChanged(nameof(Fri));
        OnPropertyChanged(nameof(Sat));
        OnPropertyChanged(nameof(Sun));
    }

    /// <summary>表单保存结果（校验通过才置位），由 View 层判断并关闭窗口。</summary>
    public ReminderTask? SavedTask { get; private set; }

    /// <summary>校验并构建任务结果；校验失败时写入 ErrorText。</summary>
    public void Save()
    {
        if (string.IsNullOrWhiteSpace(Content))
        {
            ErrorText = "任务内容不能为空";
            return;
        }
        if (Content.Trim().Length > 200)
        {
            ErrorText = "任务内容不能超过 200 字";
            return;
        }
        if (SelectedTime is null)
        {
            ErrorText = "请选择提醒时刻";
            return;
        }
        if (Recurrence == RecurrenceType.Once && SingleDate is null)
        {
            ErrorText = "单次任务需指定日期";
            return;
        }
        if (Recurrence == RecurrenceType.Weekly && _weekDays.All(d => !d))
        {
            ErrorText = "每周任务至少勾选一天";
            return;
        }
        if (IsIntervalVisible && Interval > 1 && StartDate is null)
        {
            ErrorText = "间隔大于 1 时需要设置开始日期作为锚点";
            return;
        }
        if (Recurrence == RecurrenceType.Once && SingleDate is { } picked)
        {
            var newDate = DateOnly.FromDateTime(picked);
            var today = DateOnly.FromDateTime(DateTime.Now);
            var at = picked.Date.Add(SelectedTime.Value.TimeOfDay);
            var isUnchangedPastDate = _existing?.RecurrenceType == RecurrenceType.Once
                && _existing.SingleDate == newDate;
            if (!isUnchangedPastDate && (newDate < today || (newDate == today && at <= DateTime.Now)))
            {
                ErrorText = "单次任务的日期与时刻必须晚于当前时间";
                return;
            }
        }
        if (Recurrence != RecurrenceType.Once && EndDate is { } endPicked)
        {
            var end = DateOnly.FromDateTime(endPicked);
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (StartDate is { } startPicked)
            {
                var start = DateOnly.FromDateTime(startPicked);
                // 开始日期被改到结束日期之后时必须拒绝：即使结束日期本身未变，
                // 否则会生成一个永远不会产生实例的任务（planner 直接跳过）
                if (end < start)
                {
                    ErrorText = "结束日期不能早于开始日期";
                    return;
                }
            }
            // 编辑既有任务时允许保留过去的结束日期（任务已结束），避免无法修改其他字段
            var isUnchangedEnd = _existing?.EndDate == end;
            if (!isUnchangedEnd && end < today)
            {
                ErrorText = "结束日期不能早于今天，也不能早于开始日期";
                return;
            }
        }

        var task = _existing ?? new ReminderTask();
        if (_existing is null)
        {
            task.CreatedAt = DateTime.Now;
        }
        task.Content = Content.Trim();
        task.Time = new TimeSpan(SelectedTime.Value.Hour, SelectedTime.Value.Minute, 0);
        task.RecurrenceType = Recurrence;
        task.SingleDate = Recurrence == RecurrenceType.Once && SingleDate is { } d
            ? DateOnly.FromDateTime(d)
            : null;
        task.StartDate = Recurrence != RecurrenceType.Once && StartDate is { } sd
            ? DateOnly.FromDateTime(sd)
            : null;
        task.EndDate = Recurrence != RecurrenceType.Once && EndDate is { } ed
            ? DateOnly.FromDateTime(ed)
            : null;
        task.Interval = IsIntervalVisible ? Math.Clamp(Interval, 1, MaxInterval) : 1;
        task.WeekDays = Recurrence == RecurrenceType.Weekly
            ? _weekDays.Select((on, i) => (on, i)).Where(x => x.on).Select(x => (DayOfWeek)x.i).ToArray()
            : [];
        task.MonthDay = Recurrence is RecurrenceType.Monthly or RecurrenceType.Yearly
            ? Math.Clamp(MonthDay, 1, 31)
            : 1;
        task.Month = Recurrence == RecurrenceType.Yearly ? Math.Clamp(Month, 1, 12) : 1;
        task.UpdatedAt = DateTime.Now;
        SavedTask = task;
        ErrorText = null;
    }
}
