using QuietRemind.Models;

namespace QuietRemind.Services;

public enum SettleAction
{
    Complete,
    Snooze,
    Skip,
}

/// <summary>
/// 提醒引擎：秒级轮询触发 + 错过扫描（需求 3.1 / 5.1）。
/// 判定基于"上次轮询时刻 _lastPoll"：
///   - 错过扫描：TriggerAt 早于上次轮询且早于当前 → 判错过（进程未运行/睡眠/时钟跳变期间到点）
///   - 触发判定：TriggerAt 落在 (上次轮询, 当前] → 正常到点提醒
/// 时钟向前大跳（&gt;10s 且非睡眠）时整段过点实例按错过兜底（需求 5.1.2 第二条）。
/// 运行期内存态 _runtimeShown 记录"本次进程已弹过窗"的实例：进程重启即失效，
/// 从而强杀/关机后已弹未收尾实例仍会被错过扫描覆盖（需求 5.1.4），同运行期内不误判（需求 5.1.2 第一条）。
/// 提醒弹出本身不改变实例状态，仅收尾动作变更（需求 5.1.4）。
/// </summary>
public class ReminderEngine
{
    /// <summary>时钟跳变检测阈值：超过则视为时间被整体跳过（远大于 1s 轮询间隔）。</summary>
    private const double ClockJumpThresholdSeconds = 10;

    /// <summary>首轮轮询时打开的正常触发窗口宽度（启动扫描与首轮之间的到点归入正常触发）。</summary>
    private static readonly TimeSpan InitialPollWindow = TimeSpan.FromSeconds(2);

    private readonly IClock _clock;
    private readonly HashSet<Guid> _runtimeShown = [];
    private DateTime _lastPoll = DateTime.MinValue;

    public ReminderEngine(IClock clock) => _clock = clock;

    /// <summary>正常到点提醒（无"已错过"徽标）。</summary>
    public event Action<IReadOnlyList<Occurrence>>? NormalRemindersDue;

    /// <summary>错过补提醒（带"已错过"徽标）。</summary>
    public event Action<IReadOnlyList<Occurrence>>? MissedRemindersDue;

    public IReadOnlyCollection<Guid> RuntimeShown => _runtimeShown;

    /// <summary>单次轮询：先错过扫描（过点未弹），再触发判定（本轮新到点）。停用任务的实例不参与（需求 2.3.4）。</summary>
    public void Poll(List<Occurrence> occurrences, IReadOnlyList<ReminderTask> tasks)
    {
        var now = _clock.Now;

        if (_lastPoll == DateTime.MinValue)
        {
            // 首轮：启动扫描已处理过点实例（runtimeShown 豁免），这里仅打开最近 2 秒的常规触发窗口
            _lastPoll = now - InitialPollWindow;
        }

        if ((now - _lastPoll).TotalSeconds > ClockJumpThresholdSeconds)
        {
            // 首轮或时钟向前大跳：整段过点实例全部按错过兜底
            ScanMissed(occurrences, tasks, now, cutoff: now);
        }
        else
        {
            // 常规轮询：上次轮询之前已到点的（防御性兜底）判错过
            ScanMissed(occurrences, tasks, now, cutoff: _lastPoll);

            // 本轮新到点 → 正常触发（不改 State，仅记录弹窗）
            var due = occurrences
                .Where(o => IsEligible(o, tasks)
                    && o.TriggerAt <= now
                    && o.TriggerAt > _lastPoll)
                .ToList();
            if (due.Count > 0)
            {
                foreach (var o in due)
                {
                    o.ReminderShownAt = now;
                    _runtimeShown.Add(o.Id);
                }
                NormalRemindersDue?.Invoke(due);
            }
        }

        _lastPoll = now;
    }

    /// <summary>错过扫描（启动 / 睡眠唤醒 / 时钟跳变后显式调用）：cutoff 之前到点且未弹过的 Pending 实例全部判错过。</summary>
    public void ScanMissed(List<Occurrence> occurrences, IReadOnlyList<ReminderTask> tasks, DateTime now, DateTime? cutoff = null)
    {
        var cut = cutoff ?? now;
        var missed = occurrences
            .Where(o => IsEligible(o, tasks)
                && o.TriggerAt <= cut
                && o.TriggerAt < now)
            .ToList();
        if (missed.Count == 0)
        {
            return;
        }

        foreach (var o in missed)
        {
            o.State = OccurrenceState.Missed;
            o.ReminderShownAt = now;
            _runtimeShown.Add(o.Id);
        }
        MissedRemindersDue?.Invoke(missed);
    }

    private bool IsEligible(Occurrence o, IReadOnlyList<ReminderTask> tasks)
    {
        return o.State == OccurrenceState.Pending
            && !_runtimeShown.Contains(o.Id)
            && tasks.Any(t => t.Id == o.TaskId && t.Enabled);
    }

    /// <summary>收尾（需求 3.3）：完成/跳过 → 终态；稍后再提醒 → 推迟触发回待提醒。</summary>
    public void Settle(Occurrence occurrence, SettleAction action, int snoozeMinutes = 5)
    {
        var now = _clock.Now;
        switch (action)
        {
            case SettleAction.Complete:
                occurrence.State = OccurrenceState.Completed;
                occurrence.SettledAt = now;
                break;
            case SettleAction.Skip:
                occurrence.State = OccurrenceState.Skipped;
                occurrence.SettledAt = now;
                break;
            case SettleAction.Snooze:
                occurrence.TriggerAt = now.AddMinutes(Math.Clamp(snoozeMinutes, 1, 120));
                occurrence.State = OccurrenceState.Pending;
                occurrence.ReminderShownAt = null;
                occurrence.SettledAt = null;
                break;
        }
        _runtimeShown.Remove(occurrence.Id);
    }
}
