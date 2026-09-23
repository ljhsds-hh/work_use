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
    private int _selectedHour;
    private int _selectedMinute;
    private bool _isTimePanelOpen;
    private int _monthDay = 1;
    private string? _errorText;
    private bool[] _weekDays = new bool[7];

    public TaskEditorViewModel(ReminderTask? existing)
    {
        _existing = existing;
        _content = existing?.Content ?? "";
        _recurrence = existing?.RecurrenceType ?? RecurrenceType.Daily;
        _singleDate = existing?.SingleDate is { } d ? d.ToDateTime(TimeOnly.MinValue) : null;
        _startDate = existing?.StartDate is { } s ? s.ToDateTime(TimeOnly.MinValue) : null;
        var time = existing?.Time ?? new TimeSpan(9, 0, 0);
        _selectedHour = time.Hours;
        _selectedMinute = time.Minutes;
        _monthDay = existing?.MonthDay ?? 1;
        if (existing?.WeekDays is { Length: > 0 } days)
        {
            foreach (var day in days)
            {
                _weekDays[(int)day] = true;
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
                OnPropertyChanged(nameof(IsOnce));
                OnPropertyChanged(nameof(IsDaily));
                OnPropertyChanged(nameof(IsWeekly));
                OnPropertyChanged(nameof(IsMonthly));
                OnPropertyChanged(nameof(IsRecurring));
            }
        }
    }

    public bool IsOnce => _recurrence == RecurrenceType.Once;
    public bool IsDaily => _recurrence == RecurrenceType.Daily;
    public bool IsWeekly => _recurrence == RecurrenceType.Weekly;
    public bool IsMonthly => _recurrence == RecurrenceType.Monthly;
    public bool IsRecurring => _recurrence != RecurrenceType.Once;

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

    /// <summary>可选小时（0~23，24 小时制）。</summary>
    public IReadOnlyList<int> Hours { get; } = Enumerable.Range(0, 24).ToArray();

    /// <summary>可选分钟（0~59）。</summary>
    public IReadOnlyList<int> Minutes { get; } = Enumerable.Range(0, 60).ToArray();

    /// <summary>提醒时刻-小时（0~23）。</summary>
    public int SelectedHour
    {
        get => _selectedHour;
        set
        {
            if (SetProperty(ref _selectedHour, value))
            {
                OnPropertyChanged(nameof(TimeText));
            }
        }
    }

    /// <summary>提醒时刻-分钟（0~59）。</summary>
    public int SelectedMinute
    {
        get => _selectedMinute;
        set
        {
            if (SetProperty(ref _selectedMinute, value))
            {
                OnPropertyChanged(nameof(TimeText));
            }
        }
    }

    /// <summary>时刻触发按钮显示文本（HH:mm）。</summary>
    public string TimeText => $"{SelectedHour:00}:{SelectedMinute:00}";

    /// <summary>时刻点选面板开合（绑定触发按钮与 Popup）。</summary>
    public bool IsTimePanelOpen
    {
        get => _isTimePanelOpen;
        set => SetProperty(ref _isTimePanelOpen, value);
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

        var task = _existing ?? new ReminderTask();
        if (_existing is null)
        {
            task.CreatedAt = DateTime.Now;
        }
        task.Content = Content.Trim();
        task.Time = new TimeSpan(Math.Clamp(SelectedHour, 0, 23), Math.Clamp(SelectedMinute, 0, 59), 0);
        task.RecurrenceType = Recurrence;
        task.SingleDate = Recurrence == RecurrenceType.Once && SingleDate is { } d
            ? DateOnly.FromDateTime(d)
            : null;
        task.StartDate = Recurrence != RecurrenceType.Once && StartDate is { } sd
            ? DateOnly.FromDateTime(sd)
            : null;
        task.WeekDays = Recurrence == RecurrenceType.Weekly
            ? _weekDays.Select((on, i) => (on, i)).Where(x => x.on).Select(x => (DayOfWeek)x.i).ToArray()
            : [];
        task.MonthDay = Recurrence == RecurrenceType.Monthly ? Math.Clamp(MonthDay, 1, 31) : 1;
        task.UpdatedAt = DateTime.Now;
        SavedTask = task;
        ErrorText = null;
    }
}
