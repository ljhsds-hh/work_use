using QuietRemind.Models;

namespace QuietRemind.Services;

/// <summary>任务实例规划器：为启用任务滚动生成 [today, today+horizon) 内的缺失实例。</summary>
public class OccurrencePlanner
{
    private readonly IClock _clock;

    public OccurrencePlanner(IClock clock) => _clock = clock;

    /// <summary>补齐缺失实例。已过点的当天实例不追溯（需求 2.3.1）。返回新增实例数。</summary>
    public int EnsureUpTo(List<Occurrence> occurrences, IReadOnlyList<ReminderTask> tasks, int horizonDays = 7)
    {
        var now = _clock.Now;
        var today = DateOnly.FromDateTime(now);
        var end = today.AddDays(horizonDays);
        var added = 0;

        foreach (var task in tasks)
        {
            if (!task.Enabled)
            {
                continue;
            }

            // 循环开始日期：晚于今天则从该日起生成（需求：可控制从哪一天开始）；否则从今天起
            var from = task.StartDate is { } sd && sd > today ? sd : today;

            // 按 OriginalTriggerAt 去重：仅未收尾实例占位（已收尾为历史记录，编辑重建后同日可再生成新实例）
            var existing = occurrences
                .Where(o => o.TaskId == task.Id
                    && o.State is OccurrenceState.Pending or OccurrenceState.Missed)
                .Select(o => DateOnly.FromDateTime(o.OriginalTriggerAt))
                .ToHashSet();

            foreach (var date in EnumerateDates(task, from, end))
            {
                if (!existing.Contains(date))
                {
                    var at = date.ToDateTime(TimeOnly.FromTimeSpan(task.Time));
                    if (at <= now)
                    {
                        continue; // 新增/编辑后当天已过点，不追溯
                    }
                    occurrences.Add(new Occurrence
                    {
                        TaskId = task.Id,
                        TriggerAt = at,
                        OriginalTriggerAt = at,
                    });
                    added++;
                }
            }
        }

        return added;
    }

    /// <summary>
    /// 编辑任务后重建：删除该任务全部未收尾实例（含已触发、已错过、稍后再提醒推迟中的），
    /// 按新规则重新生成并立即生效（需求 2.3.2，2026-09-19 用户澄清：编辑后新设置须马上生效）。
    /// 已收尾实例（完成/跳过）为历史记录，保留不动。
    /// </summary>
    public void RebuildFuture(List<Occurrence> occurrences, ReminderTask task, int horizonDays = 7)
    {
        occurrences.RemoveAll(o => o.TaskId == task.Id
            && o.State is OccurrenceState.Pending or OccurrenceState.Missed);

        if (task.Enabled)
        {
            EnsureUpTo(occurrences, [task], horizonDays);
        }
    }

    private static IEnumerable<DateOnly> EnumerateDates(ReminderTask task, DateOnly from, DateOnly end)
    {
        for (var d = from; d < end; d = d.AddDays(1))
        {
            if (Matches(task, d))
            {
                yield return d;
            }
        }
    }

    private static bool Matches(ReminderTask task, DateOnly date) => task.RecurrenceType switch
    {
        RecurrenceType.Once => task.SingleDate == date,
        RecurrenceType.Daily => true,
        RecurrenceType.Weekly => task.WeekDays.Contains(date.DayOfWeek),
        // 每月指定日（1~31），当月无该日时取当月最后一天（需求 2.2）
        RecurrenceType.Monthly => date.Day == Math.Min(task.MonthDay, DateTime.DaysInMonth(date.Year, date.Month)),
        _ => false,
    };
}
