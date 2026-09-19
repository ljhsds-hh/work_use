using QuietRemind.Models;
using QuietRemind.Services;
using Xunit;

namespace QuietRemind.Tests;

public class ReminderEngineTests
{
    private readonly FakeClock _clock = new(new DateTime(2026, 9, 18, 10, 0, 0));
    private readonly ReminderEngine _engine;
    private readonly List<ReminderTask> _tasks = [];
    private readonly List<Occurrence> _due = [];
    private readonly List<Occurrence> _missed = [];

    public ReminderEngineTests()
    {
        _engine = new ReminderEngine(_clock);
        _engine.RemindersDue += (due, missed) =>
        {
            _due.AddRange(due);
            _missed.AddRange(missed);
        };
        var task = new ReminderTask { Content = "测试任务", Time = new TimeSpan(16, 50, 0), RecurrenceType = RecurrenceType.Daily };
        _tasks.Add(task);
    }

    private Occurrence MakePending(DateTime at) => new()
    {
        TaskId = _tasks[0].Id,
        TriggerAt = at,
        OriginalTriggerAt = at,
    };

    [Fact]
    public void 到点触发_发出正常提醒_状态不变_仅标记已弹()
    {
        var o = MakePending(new DateTime(2026, 9, 18, 16, 50, 0));
        var occs = new List<Occurrence> { o };

        _clock.Set(new DateTime(2026, 9, 18, 16, 50, 1));
        _engine.Poll(occs, _tasks);

        Assert.Single(_due);
        Assert.Same(o, _due[0]);
        Assert.Equal(OccurrenceState.Pending, o.State);      // 弹出不改状态（需求 5.1.4）
        Assert.NotNull(o.ReminderShownAt);                  // 已弹标记
    }

    [Fact]
    public void 已弹实例_后续轮询不重复触发()
    {
        var o = MakePending(new DateTime(2026, 9, 18, 16, 50, 0));
        var occs = new List<Occurrence> { o };

        _clock.Set(new DateTime(2026, 9, 18, 16, 50, 1));
        _engine.Poll(occs, _tasks);
        _clock.Advance(TimeSpan.FromSeconds(3));
        _engine.Poll(occs, _tasks);

        Assert.Single(_due); // 不重复
    }

    [Fact]
    public void 运行期轮询延迟_仍按正常到点触发_不判错过()
    {
        var o = MakePending(new DateTime(2026, 9, 18, 16, 50, 0));
        var occs = new List<Occurrence> { o };

        // 上一轮 16:49:58，本轮突然 16:50:04（轮询延迟 6s）
        _clock.Set(new DateTime(2026, 9, 18, 16, 49, 58));
        _engine.Poll(occs, _tasks);
        _clock.Set(new DateTime(2026, 9, 18, 16, 50, 4));
        _engine.Poll(occs, _tasks);

        Assert.Empty(_missed);                      // 运行期正常到点不判错过（需求 5.1.2 第一条）
        Assert.Single(_due);
    }

    [Fact]
    public void 启动错过扫描_过点未收尾实例判错过_发补提醒()
    {
        // 模拟昨天 16:50 的任务实例至今未收尾，今天开机
        var o = MakePending(new DateTime(2026, 9, 17, 16, 50, 0));
        var occs = new List<Occurrence> { o };

        _clock.Set(new DateTime(2026, 9, 18, 8, 0, 0));
        _engine.ScanMissed(occs, _tasks, _clock.Now);

        Assert.Single(_missed);
        Assert.Equal(OccurrenceState.Missed, o.State);
        Assert.NotNull(o.ReminderShownAt);
    }

    [Fact]
    public void 已错过实例落盘后进程重启_再次扫描仍补提醒_无状态死角()
    {
        // 需求 5.1.4：判错过落盘后、补提醒窗口收尾前进程死亡 → 重启后必须再次补提醒
        var o = MakePending(new DateTime(2026, 9, 17, 16, 50, 0));
        var occs = new List<Occurrence> { o };

        _clock.Set(new DateTime(2026, 9, 18, 8, 0, 0));
        _engine.ScanMissed(occs, _tasks, _clock.Now);
        Assert.Equal(OccurrenceState.Missed, o.State);

        // 进程死亡 → 新引擎（runtimeShown 清空）重启扫描
        var newEngine = new ReminderEngine(_clock);
        var missedAgain = new List<Occurrence>();
        newEngine.RemindersDue += (_, missed) => missedAgain.AddRange(missed);

        _clock.Set(new DateTime(2026, 9, 18, 8, 5, 0));
        newEngine.ScanMissed(occs, _tasks, _clock.Now);

        Assert.Single(missedAgain);                 // 再次补提醒，不静默消失
        Assert.Same(o, missedAgain[0]);
    }

    [Fact]
    public void 睡眠期间到点_唤醒扫描判错过_补提醒()
    {
        var o = MakePending(new DateTime(2026, 9, 18, 23, 30, 0));
        var occs = new List<Occurrence> { o };

        _clock.Set(new DateTime(2026, 9, 18, 22, 0, 0));
        _engine.Poll(occs, _tasks); // 睡前最后一轮

        _clock.Set(new DateTime(2026, 9, 19, 8, 0, 0)); // 唤醒
        _engine.ScanMissed(occs, _tasks, _clock.Now);

        Assert.Single(_missed);
        Assert.Equal(OccurrenceState.Missed, o.State);
    }

    [Fact]
    public void 时钟向前跳变_轮询兜底判错过()
    {
        var o = MakePending(new DateTime(2026, 9, 18, 16, 50, 0));
        var occs = new List<Occurrence> { o };

        _clock.Set(new DateTime(2026, 9, 18, 16, 0, 0));
        _engine.Poll(occs, _tasks);
        _clock.Set(new DateTime(2026, 9, 18, 17, 0, 0)); // 时钟前跳 1 小时
        _engine.Poll(occs, _tasks);

        Assert.Single(_missed);
        Assert.Equal(OccurrenceState.Missed, o.State);
    }

    [Fact]
    public void 时钟回拨_不误判不漏触发()
    {
        var o = MakePending(new DateTime(2026, 9, 18, 16, 50, 0));
        var occs = new List<Occurrence> { o };

        _clock.Set(new DateTime(2026, 9, 18, 16, 50, 1));
        _engine.Poll(occs, _tasks);
        _clock.Set(new DateTime(2026, 9, 18, 15, 0, 0)); // 回拨
        _engine.Poll(occs, _tasks);
        _clock.Set(new DateTime(2026, 9, 18, 16, 50, 2)); // 回到正常
        _engine.Poll(occs, _tasks);

        Assert.Empty(_missed);
        Assert.Single(_due); // 只正常触发一次，不重复
    }

    [Fact]
    public void 强杀重启后_已弹未收尾实例_补提醒覆盖()
    {
        var o = MakePending(new DateTime(2026, 9, 18, 16, 50, 0));
        var occs = new List<Occurrence> { o };

        // 第一次运行：到点触发，用户未收尾
        _clock.Set(new DateTime(2026, 9, 18, 16, 50, 1));
        _engine.Poll(occs, _tasks);
        Assert.Equal(OccurrenceState.Pending, o.State);

        // 进程被强杀 → 新引擎实例（runtimeShown 清空），下次启动扫描
        var newEngine = new ReminderEngine(_clock);
        var missed = new List<Occurrence>();
        newEngine.RemindersDue += (_, list) => missed.AddRange(list);

        _clock.Set(new DateTime(2026, 9, 19, 8, 0, 0));
        newEngine.ScanMissed(occs, _tasks, _clock.Now);

        Assert.Single(missed);
        Assert.Equal(OccurrenceState.Missed, o.State); // 需求 5.1.4：无状态死角
    }

    [Fact]
    public void 同一轮询内错过与到期实例_合并为一次事件()
    {
        // 需求 3.4：同一轮询周期到期的实例合并展示，不弹多个窗口
        var missedOne = MakePending(new DateTime(2026, 9, 18, 16, 0, 0));  // 上轮已过点
        var dueOne = MakePending(new DateTime(2026, 9, 18, 16, 50, 0));    // 本轮新到点
        var occs = new List<Occurrence> { missedOne, dueOne };

        _clock.Set(new DateTime(2026, 9, 18, 16, 49, 58));
        _engine.Poll(occs, _tasks);
        _clock.Set(new DateTime(2026, 9, 18, 16, 50, 1));
        _engine.Poll(occs, _tasks);

        Assert.Single(_missed);
        Assert.Single(_due); // 同轮两个列表分别非空 → App 侧合并一个窗口
    }

    [Fact]
    public void 收尾完成_进入终态()
    {
        var o = MakePending(new DateTime(2026, 9, 18, 16, 50, 0));
        _engine.Settle(o, SettleAction.Complete);

        Assert.Equal(OccurrenceState.Completed, o.State);
        Assert.NotNull(o.SettledAt);
    }

    [Fact]
    public void 收尾跳过_进入终态()
    {
        var o = MakePending(new DateTime(2026, 9, 18, 16, 50, 0));
        _engine.Settle(o, SettleAction.Skip);

        Assert.Equal(OccurrenceState.Skipped, o.State);
        Assert.NotNull(o.SettledAt);
    }

    [Fact]
    public void 稍后再提醒_推迟触发回待提醒_到点再触发()
    {
        var o = MakePending(new DateTime(2026, 9, 18, 16, 50, 0));
        var occs = new List<Occurrence> { o };
        _clock.Set(new DateTime(2026, 9, 18, 16, 50, 1));
        _engine.Poll(occs, _tasks);
        _clock.Set(new DateTime(2026, 9, 18, 16, 50, 2));
        _engine.Poll(occs, _tasks); // 常规轮询步进

        _engine.Settle(o, SettleAction.Snooze, 10);

        Assert.Equal(OccurrenceState.Pending, o.State);
        Assert.Equal(new DateTime(2026, 9, 18, 17, 0, 2), o.TriggerAt);
        Assert.Null(o.ReminderShownAt); // 待再次触发

        // 模拟真实 1s 轮询节奏逼近 snooze 到点
        _clock.Set(new DateTime(2026, 9, 18, 17, 0, 0));
        _engine.Poll(occs, _tasks);
        _clock.Set(new DateTime(2026, 9, 18, 17, 0, 1));
        _engine.Poll(occs, _tasks);

        _clock.Set(new DateTime(2026, 9, 18, 17, 0, 3));
        _engine.Poll(occs, _tasks);

        // 首轮窗口触发 1 次 + snooze 后再触发 1 次；snooze 后那次不判错过（需求 3.3）
        Assert.Equal(2, _due.Count);
        Assert.Empty(_missed);
        Assert.Equal(new DateTime(2026, 9, 18, 17, 0, 3), o.ReminderShownAt);
        Assert.Equal(OccurrenceState.Pending, o.State);
    }

    [Fact]
    public void 稍后再提醒推迟期间关机重启_按错过处理()
    {
        var o = MakePending(new DateTime(2026, 9, 18, 16, 50, 0));
        var occs = new List<Occurrence> { o };
        _clock.Set(new DateTime(2026, 9, 18, 16, 50, 1));
        _engine.Poll(occs, _tasks);
        _engine.Settle(o, SettleAction.Snooze, 10); // 推迟到 17:00:01

        var newEngine = new ReminderEngine(_clock);
        var missed = new List<Occurrence>();
        newEngine.RemindersDue += (_, list) => missed.AddRange(list);

        _clock.Set(new DateTime(2026, 9, 19, 8, 0, 0)); // 关机一夜后开机
        newEngine.ScanMissed(occs, _tasks, _clock.Now);

        Assert.Single(missed); // 需求 3.3 说明：推迟期间关机按错过处理
        Assert.Equal(OccurrenceState.Missed, o.State);
    }

    [Fact]
    public void 停用任务实例_不触发不判错过()
    {
        _tasks[0].Enabled = false;
        var o = MakePending(new DateTime(2026, 9, 18, 16, 50, 0));
        var occs = new List<Occurrence> { o };

        _clock.Set(new DateTime(2026, 9, 18, 16, 50, 1));
        _engine.Poll(occs, _tasks);
        _clock.Set(new DateTime(2026, 9, 19, 8, 0, 0));
        _engine.ScanMissed(occs, _tasks, _clock.Now);

        Assert.Empty(_due);
        Assert.Empty(_missed);
        Assert.Equal(OccurrenceState.Pending, o.State);
    }

    [Fact]
    public void 多实例同时到点_一次事件全部列出()
    {
        var o1 = MakePending(new DateTime(2026, 9, 18, 16, 50, 0));
        var o2 = MakePending(new DateTime(2026, 9, 18, 16, 50, 0));
        var occs = new List<Occurrence> { o1, o2 };

        _clock.Set(new DateTime(2026, 9, 18, 16, 50, 1));
        _engine.Poll(occs, _tasks);

        Assert.Equal(2, _due.Count);
    }
}
