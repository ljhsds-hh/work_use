using QuietRemind.Models;
using QuietRemind.Services;
using Xunit;

namespace QuietRemind.Tests;

public class ShutdownInterceptTests
{
    private readonly DateTime _now = new(2026, 9, 18, 20, 0, 0); // 周五 20:00
    private readonly List<ReminderTask> _tasks = [];
    private readonly List<Occurrence> _occs = [];

    private ReminderTask AddTask(bool enabled = true) => new()
    {
        Content = "任务",
        Time = new TimeSpan(16, 50, 0),
        RecurrenceType = RecurrenceType.Daily,
        Enabled = enabled,
    };

    private Occurrence AddOccurrence(Guid taskId, DateTime triggerAt, OccurrenceState state = OccurrenceState.Pending)
    {
        var o = new Occurrence { TaskId = taskId, TriggerAt = triggerAt, OriginalTriggerAt = triggerAt, State = state };
        _occs.Add(o);
        return o;
    }

    [Fact]
    public void 今天欠着_启用任务_待提醒_拦截()
    {
        var t = AddTask();
        _tasks.Add(t);
        AddOccurrence(t.Id, new DateTime(2026, 9, 18, 22, 0, 0));

        var owed = ShutdownIntercept.GetOwedOccurrences(_now, _tasks, _occs);
        Assert.Single(owed);
    }

    [Fact]
    public void 今天已到点未收尾_拦截()
    {
        var t = AddTask();
        _tasks.Add(t);
        AddOccurrence(t.Id, new DateTime(2026, 9, 18, 16, 50, 0));

        var owed = ShutdownIntercept.GetOwedOccurrences(_now, _tasks, _occs);
        Assert.Single(owed);
    }

    [Fact]
    public void 明天任务_不拦截_今天不欠账()
    {
        var t = AddTask();
        _tasks.Add(t);
        AddOccurrence(t.Id, new DateTime(2026, 9, 19, 16, 50, 0));

        var owed = ShutdownIntercept.GetOwedOccurrences(_now, _tasks, _occs);
        Assert.Empty(owed);
    }

    [Fact]
    public void 已收尾_不拦截()
    {
        var t = AddTask();
        _tasks.Add(t);
        AddOccurrence(t.Id, new DateTime(2026, 9, 18, 16, 50, 0), OccurrenceState.Completed);
        AddOccurrence(t.Id, new DateTime(2026, 9, 18, 17, 0, 0), OccurrenceState.Skipped);

        var owed = ShutdownIntercept.GetOwedOccurrences(_now, _tasks, _occs);
        Assert.Empty(owed);
    }

    [Fact]
    public void 停用任务_不拦截()
    {
        var t = AddTask(enabled: false);
        _tasks.Add(t);
        AddOccurrence(t.Id, new DateTime(2026, 9, 18, 22, 0, 0));

        var owed = ShutdownIntercept.GetOwedOccurrences(_now, _tasks, _occs);
        Assert.Empty(owed);
    }

    [Fact]
    public void 稍后再提醒推迟到明天的实例_不拦截()
    {
        var t = AddTask();
        _tasks.Add(t);
        AddOccurrence(t.Id, new DateTime(2026, 9, 19, 0, 20, 0)); // snooze 跨天

        var owed = ShutdownIntercept.GetOwedOccurrences(_now, _tasks, _occs);
        Assert.Empty(owed);
    }

    [Fact]
    public void 今天推迟到今晚的实例_拦截()
    {
        var t = AddTask();
        _tasks.Add(t);
        AddOccurrence(t.Id, new DateTime(2026, 9, 18, 23, 30, 0)); // snooze 到今晚

        var owed = ShutdownIntercept.GetOwedOccurrences(_now, _tasks, _occs);
        Assert.Single(owed);
    }

    [Fact]
    public void 无任何任务_不拦截()
    {
        var owed = ShutdownIntercept.GetOwedOccurrences(_now, _tasks, _occs);
        Assert.Empty(owed);
    }
}
