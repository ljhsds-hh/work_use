namespace QuietRemind.Models;

/// <summary>某任务按循环规则在具体日期/时刻产生的一次计划触发，是提醒与收尾的基本单位。</summary>
public class Occurrence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    /// <summary>当前计划触发时刻（稍后再提醒时被推迟更新）。</summary>
    public DateTime TriggerAt { get; set; }
    /// <summary>初始计划时刻，历史展示用，不可变。</summary>
    public DateTime OriginalTriggerAt { get; set; }
    public OccurrenceState State { get; set; } = OccurrenceState.Pending;
    /// <summary>首次弹窗时刻（幂等标识）；稍后再提醒时清空。</summary>
    public DateTime? ReminderShownAt { get; set; }
    public DateTime? SettledAt { get; set; }
}
