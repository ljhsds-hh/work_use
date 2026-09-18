using QuietRemind.Models;
using QuietRemind.Services;
using Xunit;

namespace QuietRemind.Tests;

public class OccurrencePlannerTests
{
    private readonly FakeClock _clock = new(new DateTime(2026, 9, 18, 10, 0, 0)); // 周五
    private readonly OccurrencePlanner _planner;

    public OccurrencePlannerTests() => _planner = new OccurrencePlanner(_clock);

    private static ReminderTask Daily(string content = "每日任务", TimeSpan? time = null) => new()
    {
        Content = content,
        RecurrenceType = RecurrenceType.Daily,
        Time = time ?? new TimeSpan(16, 50, 0),
        CreatedAt = DateTime.Now,
        UpdatedAt = DateTime.Now,
    };

    [Fact]
    public void 每天任务_生成今天起7天内实例()
    {
        var tasks = new List<ReminderTask> { Daily() };
        var occs = new List<Occurrence>();

        _planner.EnsureUpTo(occs, tasks);

        Assert.Equal(7, occs.Count);
        Assert.Equal(new DateTime(2026, 9, 18, 16, 50, 0), occs[0].TriggerAt);
        Assert.Equal(new DateTime(2026, 9, 24, 16, 50, 0), occs[^1].TriggerAt);
    }

    [Fact]
    public void 当天已过时刻_不生成当天实例_不追溯()
    {
        var tasks = new List<ReminderTask> { Daily(time: new TimeSpan(9, 0, 0)) };
        var occs = new List<Occurrence>();

        _planner.EnsureUpTo(occs, tasks);

        Assert.Equal(6, occs.Count);
        Assert.DoesNotContain(occs, o => DateOnly.FromDateTime(o.TriggerAt) == new DateOnly(2026, 9, 18));
    }

    [Fact]
    public void 单次任务_仅生成指定日期一个实例()
    {
        var task = new ReminderTask
        {
            Content = "单次",
            RecurrenceType = RecurrenceType.Once,
            SingleDate = new DateOnly(2026, 9, 24),
            Time = new TimeSpan(9, 30, 0),
        };
        var occs = new List<Occurrence>();

        _planner.EnsureUpTo(occs, [task]);

        Assert.Single(occs);
        Assert.Equal(new DateTime(2026, 9, 24, 9, 30, 0), occs[0].TriggerAt);
    }

    [Fact]
    public void 单次任务_指定日期在生成范围外_不生成()
    {
        var task = new ReminderTask
        {
            RecurrenceType = RecurrenceType.Once,
            SingleDate = new DateOnly(2026, 12, 1),
            Time = new TimeSpan(9, 30, 0),
        };

        var occs = new List<Occurrence>();
        _planner.EnsureUpTo(occs, [task]);
        Assert.Empty(occs);
    }

    [Fact]
    public void 每周任务_仅勾选星期生成()
    {
        var task = new ReminderTask
        {
            RecurrenceType = RecurrenceType.Weekly,
            WeekDays = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday],
            Time = new TimeSpan(8, 0, 0),
        };

        var occs = new List<Occurrence>();
        _planner.EnsureUpTo(occs, [task]);

        Assert.All(occs, o => Assert.Contains(o.TriggerAt.DayOfWeek, new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday }));
        // 9/18(周五 8:00) 在创建时(10:00)已过点不追溯（需求 2.3.1）→ 仅 9/21(周一)、9/23(周三)
        Assert.Equal(2, occs.Count);
        Assert.Equal(new DateTime(2026, 9, 21, 8, 0, 0), occs[0].TriggerAt);
        Assert.Equal(new DateTime(2026, 9, 23, 8, 0, 0), occs[1].TriggerAt);
    }

    [Fact]
    public void 每月任务_31日遇小月取月末()
    {
        // 每月 31 日：9/30 是本月最后一天（9 月无 31）→ 取 9/30；10/31 正常
        var task = new ReminderTask
        {
            RecurrenceType = RecurrenceType.Monthly,
            MonthDay = 31,
            Time = new TimeSpan(12, 0, 0),
        };

        var occs = new List<Occurrence>();
        _planner.EnsureUpTo(occs, [task], horizonDays: 30);

        Assert.Contains(occs, o => o.TriggerAt == new DateTime(2026, 9, 30, 12, 0, 0));
    }

    [Fact]
    public void 每月任务_31日遇二月取闰年2月29()
    {
        // 时钟拨到 2028-02-05（闰年），未来 7 天：2/28 是当月最后一天 → 2/28
        _clock.Set(new DateTime(2028, 2, 5, 10, 0, 0));
        var task = new ReminderTask
        {
            RecurrenceType = RecurrenceType.Monthly,
            MonthDay = 31,
            Time = new TimeSpan(12, 0, 0),
        };

        var occs = new List<Occurrence>();
        _planner.EnsureUpTo(occs, [task], horizonDays: 30);

        Assert.Single(occs);
        // 2028 是闰年，2 月末为 29 日
        Assert.Equal(new DateTime(2028, 2, 29, 12, 0, 0), occs[0].TriggerAt);
    }

    [Fact]
    public void 滚动补缺_不重复生成已存在日期()
    {
        var task = Daily();
        var occs = new List<Occurrence>();

        _planner.EnsureUpTo(occs, [task]);
        var count1 = occs.Count;
        Assert.Equal(7, count1);

        // 时间前进一天：范围右移，补上新的一天，已有日期不重复
        _clock.Advance(TimeSpan.FromDays(1));
        _planner.EnsureUpTo(occs, [task]);
        Assert.Equal(count1 + 1, occs.Count);
        Assert.Equal(occs.Count, occs.Select(o => DateOnly.FromDateTime(o.OriginalTriggerAt)).Distinct().Count());

        // 继续前进 7 天：再补 7 个新实例
        _clock.Advance(TimeSpan.FromDays(7));
        _planner.EnsureUpTo(occs, [task]);
        Assert.Equal(count1 + 1 + 7, occs.Count);
    }

    [Fact]
    public void 停用任务_不生成实例()
    {
        var task = Daily();
        task.Enabled = false;

        var occs = new List<Occurrence>();
        _planner.EnsureUpTo(occs, [task]);
        Assert.Empty(occs);
    }

    [Fact]
    public void 编辑任务_重建未来实例_不影响已收尾历史()
    {
        var task = Daily();
        var occs = new List<Occurrence>();
        _planner.EnsureUpTo(occs, [task]);

        // 模拟今天实例已收尾（历史）
        occs[0].State = OccurrenceState.Completed;
        occs[0].SettledAt = _clock.Now;

        // 编辑：改为 9:00
        task.Time = new TimeSpan(9, 0, 0);
        _planner.RebuildFuture(occs, task);

        // 历史实例保留且终态不变
        Assert.Single(occs, o => o.State == OccurrenceState.Completed);
        // 未来实例按新时刻重建
        Assert.All(occs.Where(o => o.State == OccurrenceState.Pending),
            o => Assert.Equal(new TimeSpan(9, 0, 0), o.TriggerAt.TimeOfDay));
    }

    [Fact]
    public void 删除任务_清理全部实例()
    {
        var task = Daily();
        var occs = new List<Occurrence>();
        _planner.EnsureUpTo(occs, [task]);

        OccurrencePlanner.RemoveTask(occs, task.Id);
        Assert.Empty(occs);
    }
}
