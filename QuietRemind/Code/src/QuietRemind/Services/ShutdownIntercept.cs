using QuietRemind.Models;

namespace QuietRemind.Services;

/// <summary>关机前拦截提醒的欠账判定（需求 4.1）：今天还欠着（今天待提醒、未收尾、任务启用）、且关机后就提醒不到的任务实例。</summary>
public static class ShutdownIntercept
{
    public static IReadOnlyList<Occurrence> GetOwedOccurrences(
        DateTime now, IReadOnlyList<ReminderTask> tasks, IReadOnlyList<Occurrence> occurrences)
    {
        var enabledTaskIds = tasks.Where(t => t.Enabled).Select(t => t.Id).ToHashSet();
        return occurrences
            .Where(o => o.State == OccurrenceState.Pending
                && o.TriggerAt.Date == now.Date
                && enabledTaskIds.Contains(o.TaskId))
            .ToList();
    }
}
