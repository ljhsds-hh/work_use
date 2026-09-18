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

            // 按 OriginalTriggerAt 去重：snooze 只改 TriggerAt，不影响计划日期
            var existing = occurrences
                .Where(o => o.TaskId == task.Id)
                .Select(o => DateOnly.FromDateTime(o.OriginalTriggerAt))
                .ToHashSet();

            foreach (var date in EnumerateDates(task, today, end))
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

    /// <summary>编辑任务后重建未来实例：删除未进入终态且未弹过窗的实例后按新规则重新生成（需求 2.3.2）。</summary>
    public void RebuildFuture(List<Occurrence> occurrences, ReminderTask task, int horizonDays = 7)
    {
        occurrences.RemoveAll(o => o.TaskId == task.Id
            && o.State == OccurrenceState.Pending
            && o.ReminderShownAt == null);

        if (task.Enabled)
        {
            EnsureUpTo(occurrences, [task], horizonDays);
        }
    }

    /// <summary>删除任务时清理其全部实例（含历史，需求 2.3.3）。</summary>
    public static void RemoveTask(List<Occurrence> occurrences, Guid taskId)
    {
        occurrences.RemoveAll(o => o.TaskId == taskId);
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
