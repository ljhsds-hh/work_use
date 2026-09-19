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
    public DayOfWeek[] WeekDays { get; set; } = [];
    public int MonthDay { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
