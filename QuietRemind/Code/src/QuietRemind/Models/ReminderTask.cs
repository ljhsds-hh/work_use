namespace QuietRemind.Models;

/// <summary>用户设置的一条提醒规则，按循环规则反复产生任务实例。</summary>
public class ReminderTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Content { get; set; } = "";
    public TimeSpan Time { get; set; }
    public RecurrenceType RecurrenceType { get; set; } = RecurrenceType.Daily;
    public DateOnly? SingleDate { get; set; }
    /// <summary>循环开始日期（仅 Daily/Weekly/Monthly 生效；null = 从今天起）。</summary>
    public DateOnly? StartDate { get; set; }
    /// <summary>循环结束日期（含当日；null = 长期有效）。</summary>
    public DateOnly? EndDate { get; set; }
    /// <summary>重复间隔倍数：每 N 天/周/月（默认 1；Yearly 不使用）。</summary>
    public int Interval { get; set; } = 1;
    public DayOfWeek[] WeekDays { get; set; } = [];
    public int MonthDay { get; set; } = 1;
    /// <summary>每年提醒的月份（1~12，仅 Yearly 生效）。</summary>
    public int Month { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>浅拷贝（值类型/字符串字段独立，WeekDays 复制数组），用于编辑前快照对比。</summary>
    public ReminderTask Clone()
    {
        var clone = (ReminderTask)MemberwiseClone();
        clone.WeekDays = WeekDays is null ? [] : [.. WeekDays];
        return clone;
    }

    /// <summary>
    /// 是否与另一任务的“排期相关字段”完全一致（忽略内容、创建/更新时间与启用状态）。
    /// 编辑保存时据此决定是否需要重建实例：仅改内容时不应重置 snooze/提前提醒标记。
    /// </summary>
    public bool HasSameScheduleAs(ReminderTask other)
    {
        return Time == other.Time
            && RecurrenceType == other.RecurrenceType
            && SingleDate == other.SingleDate
            && StartDate == other.StartDate
            && EndDate == other.EndDate
            && Interval == other.Interval
            && MonthDay == other.MonthDay
            && Month == other.Month
            && (WeekDays ?? []).OrderBy(d => d).SequenceEqual((other.WeekDays ?? []).OrderBy(d => d));
    }
}
