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
    public void 单次任务_超过7天horizon_仍立即生成()
    {
        // 单次任务日期一旦设定就生成，避免长期未运行导致日期落入滚动窗口外而静默丢失
        var task = new ReminderTask
        {
            RecurrenceType = RecurrenceType.Once,
            SingleDate = new DateOnly(2026, 12, 1),
            Time = new TimeSpan(9, 30, 0),
        };

        var occs = new List<Occurrence>();
        _planner.EnsureUpTo(occs, [task]);
        var occ = Assert.Single(occs);
        Assert.Equal(new DateTime(2026, 12, 1, 9, 30, 0), occ.TriggerAt);
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
    public void 循环开始日期_晚于今天_从该日起生成()
    {
        var task = Daily();
        task.StartDate = new DateOnly(2026, 9, 21); // 今天 9/18，从 9/21 起

        var occs = new List<Occurrence>();
        _planner.EnsureUpTo(occs, [task]);

        Assert.Equal(4, occs.Count); // 9/21 ~ 9/24
        Assert.Equal(new DateTime(2026, 9, 21, 16, 50, 0), occs[0].TriggerAt);
        Assert.DoesNotContain(occs, o => o.TriggerAt < new DateTime(2026, 9, 21));
    }

    [Fact]
    public void 循环开始日期_早于今天_仍从今天生成()
    {
        var task = Daily();
        task.StartDate = new DateOnly(2026, 9, 1);

        var occs = new List<Occurrence>();
        _planner.EnsureUpTo(occs, [task]);

        Assert.Equal(7, occs.Count);
        Assert.Equal(new DateTime(2026, 9, 18, 16, 50, 0), occs[0].TriggerAt);
    }

    [Fact]
    public void 编辑任务_已触发未收尾实例_按新时间立即生效()
    {
        // 用户场景：16:50 已触发过一次（未收尾），编辑把时间改到 17:10 → 今天必须按新时间再次触发
        var task = Daily();
        var occs = new List<Occurrence>();
        _planner.EnsureUpTo(occs, [task]);

        // 模拟 16:50 已触发（已弹、未收尾）
        occs[0].ReminderShownAt = _clock.Now;
        task.Time = new TimeSpan(17, 10, 0);

        _planner.RebuildFuture(occs, task);

        // 旧实例（已触发）被重建，今天生成 17:10 的新实例
        var todayOcc = occs.Single(o => DateOnly.FromDateTime(o.TriggerAt) == new DateOnly(2026, 9, 18));
        Assert.Equal(new DateTime(2026, 9, 18, 17, 10, 0), todayOcc.TriggerAt);
        Assert.Equal(OccurrenceState.Pending, todayOcc.State);
        Assert.Null(todayOcc.ReminderShownAt); // 待新时间再次触发
    }

    [Fact]
    public void 编辑任务_稍后再提醒推迟中的实例一并重建_按新设置生效()
    {
        var task = Daily();
        var occs = new List<Occurrence>();
        _planner.EnsureUpTo(occs, [task]);

        // snooze 推迟中（Pending、ReminderShownAt=null、TriggerAt 已推迟）
        occs[0].TriggerAt = _clock.Now.AddMinutes(10);

        task.Time = new TimeSpan(18, 0, 0);
        _planner.RebuildFuture(occs, task);

        // 用户主动编辑 = 以新设置为准：推迟被重置，按新时刻生成
        Assert.All(occs.Where(o => o.State == OccurrenceState.Pending),
            o => Assert.Equal(new TimeSpan(18, 0, 0), o.TriggerAt.TimeOfDay));
        Assert.DoesNotContain(occs, o => o.TriggerAt > _clock.Now && o.TriggerAt < new DateTime(2026, 9, 18, 18, 0, 0));
    }

    [Fact]
    public void 停用任务_未收尾实例置为跳过_重新启用不补历史欠账()
    {
        var task = Daily();
        var occs = new List<Occurrence>();
        _planner.EnsureUpTo(occs, [task]);
        Assert.Equal(7, occs.Count);

        // 停用：已生成未收尾实例全部收尾为跳过
        task.Enabled = false;
        var skipped = _planner.SettleUnfinishedForDisabledTasks(occs, [task]);
        Assert.Equal(7, skipped);
        Assert.All(occs, o => Assert.Equal(OccurrenceState.Skipped, o.State));
        Assert.All(occs, o => Assert.NotNull(o.SettledAt));

        // 停用十天后重新启用：只从新的一天开始生成，不产生“已错过”历史欠账
        _clock.Advance(TimeSpan.FromDays(10));
        task.Enabled = true;
        _planner.EnsureUpTo(occs, [task]);

        Assert.DoesNotContain(occs, o => o.State == OccurrenceState.Missed);
        Assert.Equal(7, occs.Count(o => o.State == OccurrenceState.Pending));
    }

    [Fact]
    public void 日期回拨_按上次计划日下限跳过历史日期()
    {
        var task = Daily();
        var occs = new List<Occurrence>();

        // 今天 9/18，规划下限 9/20（模拟日期回拨到 9/18，上次计划日为 9/20）
        _planner.EnsureUpTo(occs, [task], notBefore: new DateOnly(2026, 9, 20));

        Assert.Equal(5, occs.Count); // 9/20 ~ 9/24
        Assert.All(occs, o => Assert.True(DateOnly.FromDateTime(o.TriggerAt) >= new DateOnly(2026, 9, 20)));
        Assert.True(OccurrencePlanner.IsClockRolledBack(new DateOnly(2026, 9, 18), new DateOnly(2026, 9, 20)));
        Assert.False(OccurrencePlanner.IsClockRolledBack(new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 20)));
        // 7 天内视为可信回拨（与规划窗口一致）；远超当前视为历史前跳残留，不应长期阻断规划
        Assert.True(OccurrencePlanner.IsUsableRollbackGuard(new DateOnly(2026, 9, 18), new DateOnly(2026, 9, 20)));
        Assert.False(OccurrencePlanner.IsUsableRollbackGuard(new DateOnly(2026, 9, 18), new DateOnly(2026, 9, 28)));
        Assert.False(OccurrencePlanner.IsUsableRollbackGuard(new DateOnly(2026, 9, 18), new DateOnly(2027, 9, 18)));
    }

    [Fact]
    public void 删除任务_历史实例保留_引擎按任务缺失过滤()
    {
        // 需求 2.3.3 / 8.3：删除任务不清除历史实例记录，仅停止参与提醒
        var task = Daily();
        var occs = new List<Occurrence>();
        _planner.EnsureUpTo(occs, [task]);
        occs[0].State = OccurrenceState.Completed; // 历史

        // 模拟删除：任务从 tasks 列表移除，实例保留
        var tasksAfterDelete = new List<ReminderTask>();
        var engine = new ReminderEngine(_clock);
        var due = new List<Occurrence>();
        engine.RemindersDue += (d, _) => due.AddRange(d);

        _clock.Set(new DateTime(2026, 9, 18, 16, 50, 1));
        engine.Poll(occs, tasksAfterDelete);

        Assert.Equal(7, occs.Count); // 实例全部保留
        Assert.Empty(due);           // 但不再触发提醒
    }

    [Fact]
    public void 每2天_以开始日期为锚点生成()
    {
        var task = Daily();
        task.StartDate = new DateOnly(2026, 9, 18);
        task.Interval = 2;

        var occs = new List<Occurrence>();
        _planner.EnsureUpTo(occs, [task]);

        Assert.Equal(
            new[] { "2026-09-18", "2026-09-20", "2026-09-22", "2026-09-24" },
            occs.Select(o => o.TriggerAt.ToString("yyyy-MM-dd")).ToArray());
    }

    [Fact]
    public void 每2周_仅在锚点周及每隔一周生成()
    {
        var task = new ReminderTask
        {
            Content = "双周例会",
            RecurrenceType = RecurrenceType.Weekly,
            WeekDays = [DayOfWeek.Friday],
            Time = new TimeSpan(16, 50, 0),
            StartDate = new DateOnly(2026, 9, 18), // 周五
            Interval = 2,
        };

        var occs = new List<Occurrence>();
        _planner.EnsureUpTo(occs, [task], horizonDays: 30);

        Assert.Equal(
            new[] { "2026-09-18", "2026-10-02", "2026-10-16" },
            occs.Select(o => o.TriggerAt.ToString("yyyy-MM-dd")).ToArray());
    }

    [Fact]
    public void 每2个月_跨年按锚点月份生成()
    {
        _clock.Set(new DateTime(2026, 11, 24, 10, 0, 0));
        var task = new ReminderTask
        {
            Content = "双月账单",
            RecurrenceType = RecurrenceType.Monthly,
            MonthDay = 30,
            Time = new TimeSpan(12, 0, 0),
            StartDate = new DateOnly(2026, 11, 1),
            Interval = 2,
        };

        var occs = new List<Occurrence>();
        _planner.EnsureUpTo(occs, [task], horizonDays: 150);

        Assert.Equal(
            new[] { "2026-11-30", "2027-01-30", "2027-03-30" },
            occs.Select(o => o.TriggerAt.ToString("yyyy-MM-dd")).ToArray());
    }

    [Fact]
    public void 每年任务_不受7天窗口限制_立即生成下一次()
    {
        var task = new ReminderTask
        {
            Content = "年度体检",
            RecurrenceType = RecurrenceType.Yearly,
            Month = 12,
            MonthDay = 1,
            Time = new TimeSpan(9, 0, 0),
        };

        var occs = new List<Occurrence>();
        _planner.EnsureUpTo(occs, [task]);

        var occurrence = Assert.Single(occs);
        Assert.Equal(new DateTime(2026, 12, 1, 9, 0, 0), occurrence.TriggerAt);
    }

    [Fact]
    public void 每年任务_今年日期已过则生成明年()
    {
        _clock.Set(new DateTime(2026, 12, 2, 10, 0, 0));
        var task = new ReminderTask
        {
            RecurrenceType = RecurrenceType.Yearly,
            Month = 1,
            MonthDay = 1,
            Time = new TimeSpan(9, 0, 0),
        };

        var occs = new List<Occurrence>();
        _planner.EnsureUpTo(occs, [task]);

        Assert.Equal(new DateTime(2027, 1, 1, 9, 0, 0), Assert.Single(occs).TriggerAt);
    }

    [Fact]
    public void 每年2月29_平年取2月28_闰年取2月29()
    {
        var task = new ReminderTask
        {
            RecurrenceType = RecurrenceType.Yearly,
            Month = 2,
            MonthDay = 29,
            Time = new TimeSpan(9, 0, 0),
        };

        _clock.Set(new DateTime(2027, 2, 1, 10, 0, 0));
        var flat = new List<Occurrence>();
        _planner.EnsureUpTo(flat, [task]);
        Assert.Equal(new DateTime(2027, 2, 28, 9, 0, 0), Assert.Single(flat).TriggerAt);

        _clock.Set(new DateTime(2028, 2, 1, 10, 0, 0));
        var leap = new List<Occurrence>();
        _planner.EnsureUpTo(leap, [task]);
        Assert.Equal(new DateTime(2028, 2, 29, 9, 0, 0), Assert.Single(leap).TriggerAt);
    }

    [Fact]
    public void 结束日期_当天仍生成_之后不再生成()
    {
        var task = Daily();
        task.EndDate = new DateOnly(2026, 9, 20);

        var occs = new List<Occurrence>();
        _planner.EnsureUpTo(occs, [task]);

        Assert.Equal(
            new[] { "2026-09-18", "2026-09-19", "2026-09-20" },
            occs.Select(o => o.TriggerAt.ToString("yyyy-MM-dd")).ToArray());
    }

    [Fact]
    public void 每年任务_超过结束日期不生成()
    {
        var task = new ReminderTask
        {
            RecurrenceType = RecurrenceType.Yearly,
            Month = 1,
            MonthDay = 1,
            Time = new TimeSpan(9, 0, 0),
            EndDate = new DateOnly(2026, 12, 31),
        };

        var occs = new List<Occurrence>();
        _planner.EnsureUpTo(occs, [task]);

        Assert.Empty(occs);
    }

    [Fact]
    public void 缺少开始日期且间隔大于1_按默认间隔生成避免静默丢失()
    {
        // 脏数据兜底：没有锚点无法计算间隔，宁可每天提醒也不静默漏提醒
        var task = Daily();
        task.Interval = 3;

        var occs = new List<Occurrence>();
        _planner.EnsureUpTo(occs, [task]);

        Assert.Equal(7, occs.Count);
    }

    [Fact]
    public void 单次任务_结束日期早于单次日期_不生成()
    {
        // 正常 UI 不会给单次任务写结束日期；此处兜底人工编辑 JSON 的脏数据
        var task = new ReminderTask
        {
            RecurrenceType = RecurrenceType.Once,
            SingleDate = new DateOnly(2026, 9, 24),
            EndDate = new DateOnly(2026, 9, 20),
            Time = new TimeSpan(9, 30, 0),
        };

        var occs = new List<Occurrence>();
        _planner.EnsureUpTo(occs, [task]);

        Assert.Empty(occs);
    }
}
