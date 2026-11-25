using QuietRemind.Helpers;
using QuietRemind.Models;

namespace QuietRemind.ViewModels;

/// <summary>快速新增表单：只保留内容、时刻与单次/每天，保存后走现有规划器。</summary>
public sealed class QuickAddViewModel : ViewModelBase
{
    private string _content = "";
    private bool _isOnce;
    private DateTime? _singleDate = DateTime.Today;
    private DateTime? _selectedTime;
    private string? _errorText;

    public QuickAddViewModel(DateTime now)
    {
        var suggested = now.AddMinutes(5);
        _selectedTime = DateTime.Today.Add(suggested.TimeOfDay);
        // 跨午夜时默认日期同步到明天，避免“日期今天 + 时刻 00:0x”被判为已过去
        _singleDate = suggested.Date;
    }

    public string Content
    {
        get => _content;
        set => SetProperty(ref _content, value);
    }

    public bool IsOnce
    {
        get => _isOnce;
        set
        {
            if (SetProperty(ref _isOnce, value))
            {
                OnPropertyChanged(nameof(IsDaily));
            }
        }
    }

    public bool IsDaily => !_isOnce;

    public DateTime? SingleDate
    {
        get => _singleDate;
        set => SetProperty(ref _singleDate, value);
    }

    public DateTime? SelectedTime
    {
        get => _selectedTime;
        set => SetProperty(ref _selectedTime, value);
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

    public ReminderTask? SavedTask { get; private set; }

    /// <summary>校验并构建任务；通过时 SavedTask 非空。</summary>
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
        if (IsOnce && SingleDate is null)
        {
            ErrorText = "单次任务需指定日期";
            return;
        }

        var now = DateTime.Now;
        if (IsOnce && SingleDate is { } date)
        {
            var at = date.Date.Add(SelectedTime.Value.TimeOfDay);
            if (at <= now)
            {
                ErrorText = "单次任务的日期与时刻必须晚于当前时间";
                return;
            }
        }

        SavedTask = new ReminderTask
        {
            Content = Content.Trim(),
            Time = new TimeSpan(SelectedTime.Value.Hour, SelectedTime.Value.Minute, 0),
            RecurrenceType = IsOnce ? RecurrenceType.Once : RecurrenceType.Daily,
            SingleDate = IsOnce && SingleDate is { } single ? DateOnly.FromDateTime(single) : null,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ErrorText = null;
    }
}
