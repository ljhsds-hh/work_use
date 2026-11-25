using QuietRemind.Models;

namespace QuietRemind.Services;

/// <summary>任务实例规划器：为启用任务滚动生成 [today, today+horizon) 内的缺失实例。</summary>
public class OccurrencePlanner
{
    private readonly IClock _clock;

    public OccurrencePlanner(IClock clock) => _clock = clock;

    /// <summary>
    /// 补齐缺失实例。已过点的当天实例不追溯（需求 2.3.1），返回新增实例数。
    /// 单次任务不受 horizon 限制：日期一旦设定就立即生成实例，避免长期未运行导致日期落入窗口外而静默丢失。
    /// notBefore 为规划下限（通常传上次成功规划日期）：系统日期回拨时避免为已收尾的历史日期重新生成实例。
    /// </summary>
    public int EnsureUpTo(List<Occurrence> occurrences, IReadOnlyList<ReminderTask> tasks,
        int horizonDays = 7, DateOnly? notBefore = null)
    {
        var now = _clock.Now;
        var today = DateOnly.FromDateTime(now);
        var end = today.AddDays(horizonDays);
        var added = 0;

        // 一次性按任务归集未收尾实例的日期，避免每个任务都全量扫描实例列表
        var existingByTask = occurrences
            .Where(o => o.State is OccurrenceState.Pending or OccurrenceState.Missed)
            .GroupBy(o => o.TaskId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(o => DateOnly.FromDateTime(o.OriginalTriggerAt)).ToHashSet());

        foreach (var task in tasks)
        {
            if (!task.Enabled)
            {
                continue;
            }

            // 生效起始日：今天 → 任务开始日期 → 规划下限，三者取最晚
            var from = today;
            if (task.StartDate is { } startDate && startDate > from)
            {
                from = startDate;
            }
            if (notBefore is { } minDate && minDate > from)
            {
                from = minDate;
            }

            // 按 OriginalTriggerAt 去重：仅未收尾实例占位（已收尾为历史记录，编辑重建后同日可再生成新实例）
            if (!existingByTask.TryGetValue(task.Id, out var existing))
            {
                existing = [];
                existingByTask[task.Id] = existing;
            }

            IEnumerable<DateOnly> dates;
            if (task.RecurrenceType == RecurrenceType.Once)
            {
                // 单次任务直接生成指定日期，不受 7 天滚动窗口限制；
                // 脏数据可能给单次任务写入早于日期的结束日，此类实例不应生成
                dates = task.SingleDate is { } single
                        && single >= from
                        && (task.EndDate is not { } onceEnd || single <= onceEnd)
                    ? [single]
                    : [];
            }
            else if (task.RecurrenceType == RecurrenceType.Yearly)
            {
                // 每年任务只生成下一次，不受 7 天滚动窗口限制（长期未运行也不会漏掉）
                dates = NextYearlyDate(task, from);
            }
            else
            {
                dates = EnumerateDates(task, from, end, task.StartDate);
            }

            foreach (var date in dates)
            {
                if (existing.Contains(date))
                {
                    continue;
                }

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
                existing.Add(date);
                added++;
            }
        }

        return added;
    }

    /// <summary>
    /// 停用任务的未收尾实例统一收尾为已跳过：停用期间不生成、不提醒，也避免重新启用时
    /// 把停用前预生成的多日实例集中判为“已错过”补提醒。返回处理条数。
    /// </summary>
    public int SettleUnfinishedForDisabledTasks(List<Occurrence> occurrences, IReadOnlyList<ReminderTask> tasks)
    {
        var disabledIds = tasks.Where(t => !t.Enabled).Select(t => t.Id).ToHashSet();
        if (disabledIds.Count == 0)
        {
            return 0;
        }

        var now = _clock.Now;
        var count = 0;
        foreach (var occurrence in occurrences)
        {
            if (occurrence.State is OccurrenceState.Pending or OccurrenceState.Missed
                && disabledIds.Contains(occurrence.TaskId))
            {
                occurrence.State = OccurrenceState.Skipped;
                occurrence.SettledAt = now;
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// 编辑任务后重建：删除该任务全部未收尾实例（含已触发、已错过、稍后再提醒推迟中的），
    /// 按新规则重新生成并立即生效（需求 2.3.2，2026-09-19 用户澄清：编辑后新设置须马上生效）。
    /// 已收尾实例（完成/跳过）为历史记录，保留不动。
    /// </summary>
    public void RebuildFuture(List<Occurrence> occurrences, ReminderTask task,
        int horizonDays = 7, DateOnly? notBefore = null)
    {
        occurrences.RemoveAll(o => o.TaskId == task.Id
            && o.State is OccurrenceState.Pending or OccurrenceState.Missed);

        if (task.Enabled)
        {
            EnsureUpTo(occurrences, [task], horizonDays, notBefore);
        }
    }

    /// <summary>系统日期是否回拨到上次成功规划日期之前。</summary>
    public static bool IsClockRolledBack(DateOnly today, DateOnly? lastPlannedDate)
        => lastPlannedDate is { } last && today < last;

    /// <summary>
    /// 可信回拨窗口。与默认 7 天规划窗口一致：回拨不超过该天数时，已有实例仍能覆盖未来提醒；
    /// 超过则视为历史时钟前跳残留，重置水位继续规划，避免长期停止提醒。
    /// </summary>
    public const int MaxRollbackDays = 7;

    public static bool IsUsableRollbackGuard(DateOnly today, DateOnly? lastPlannedDate)
        => lastPlannedDate is { } last
           && today < last
           && last.DayNumber - today.DayNumber <= MaxRollbackDays;

    private static IEnumerable<DateOnly> EnumerateDates(ReminderTask task, DateOnly from, DateOnly end, DateOnly? anchor)
    {
        for (var d = from; d < end; d = d.AddDays(1))
        {
            if (task.EndDate is { } endDate && d > endDate)
            {
                yield break;
            }
            if (Matches(task, d, anchor))
            {
                yield return d;
            }
        }
    }

    /// <summary>
    /// 每年任务的下一次：从 from 起最近的一个「Month 月 MonthDay 日」。
    /// 当月无该日时取当月最后一天（如 2 月 30 日 → 2/28 或闰年 2/29）。
    /// </summary>
    private static IEnumerable<DateOnly> NextYearlyDate(ReminderTask task, DateOnly from)
    {
        var month = Math.Clamp(task.Month, 1, 12);
        var candidate = new DateOnly(from.Year, month, ClampedMonthDay(from.Year, month, task.MonthDay));
        if (candidate < from)
        {
            var nextYear = from.Year + 1;
            candidate = new DateOnly(nextYear, month, ClampedMonthDay(nextYear, month, task.MonthDay));
        }
        if (task.EndDate is { } endDate && candidate > endDate)
        {
            yield break;
        }
        yield return candidate;
    }

    private static bool Matches(ReminderTask task, DateOnly date, DateOnly? anchor) => task.RecurrenceType switch
    {
        RecurrenceType.Once => task.SingleDate == date,
        RecurrenceType.Daily => IntervalMatches(date.DayNumber - (anchor?.DayNumber ?? date.DayNumber), task.Interval),
        RecurrenceType.Weekly => (task.WeekDays ?? []).Contains(date.DayOfWeek)
            && WeekIntervalMatches(date, anchor, task.Interval),
        // 每月指定日（1~31），当月无该日时取当月最后一天（需求 2.2）
        RecurrenceType.Monthly => date.Day == ClampedMonthDay(date.Year, date.Month, task.MonthDay)
            && MonthIntervalMatches(date, anchor, task.Interval),
        RecurrenceType.Yearly => date.Month == Math.Clamp(task.Month, 1, 12)
            && date.Day == ClampedMonthDay(date.Year, task.Month, task.MonthDay),
        _ => false,
    };

    private static bool IntervalMatches(int dayDiff, int interval)
        => interval <= 1 || dayDiff % interval == 0;

    private static bool WeekIntervalMatches(DateOnly date, DateOnly? anchor, int interval)
    {
        if (interval <= 1 || anchor is null)
        {
            return true;
        }
        var weeks = (StartOfWeek(date).DayNumber - StartOfWeek(anchor.Value).DayNumber) / 7;
        return weeks % interval == 0;
    }

    private static bool MonthIntervalMatches(DateOnly date, DateOnly? anchor, int interval)
    {
        if (interval <= 1 || anchor is null)
        {
            return true;
        }
        var months = (date.Year - anchor.Value.Year) * 12 + date.Month - anchor.Value.Month;
        return months % interval == 0;
    }

    private static DateOnly StartOfWeek(DateOnly date)
        => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static int ClampedMonthDay(int year, int month, int monthDay)
        => Math.Min(Math.Clamp(monthDay, 1, 31), DateTime.DaysInMonth(year, Math.Clamp(month, 1, 12)));
}
