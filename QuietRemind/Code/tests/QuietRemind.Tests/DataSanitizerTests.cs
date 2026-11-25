using QuietRemind.Models;
using QuietRemind.Services;
using Xunit;

namespace QuietRemind.Tests;

public class DataSanitizerTests
{
    [Fact]
    public void 稍后提醒缺失_归一化为默认三个候选()
    {
        var data = new AppData { Settings = new AppSettings { SnoozeMinutes = null! } };

        var warnings = DataSanitizer.Normalize(data);

        Assert.Equal([5, 10, 30], data.Settings.SnoozeMinutes);
        Assert.NotEmpty(warnings);
    }

    [Fact]
    public void 稍后提醒过短或越界_补齐并夹在1到120()
    {
        var data = new AppData { Settings = new AppSettings { SnoozeMinutes = [0, 999] } };

        DataSanitizer.Normalize(data);

        Assert.Equal([1, 120, 30], data.Settings.SnoozeMinutes);
    }

    [Fact]
    public void 提前提醒越界_夹在0到120()
    {
        var data = new AppData { Settings = new AppSettings { PreAlertMinutes = 500 } };

        var warnings = DataSanitizer.Normalize(data);

        Assert.Equal(120, data.Settings.PreAlertMinutes);
        Assert.NotEmpty(warnings);
    }

    [Fact]
    public void 循环扩展字段越界与结束日期倒置_归一化并提示()
    {
        var task = new ReminderTask
        {
            Content = "脏循环",
            RecurrenceType = RecurrenceType.Weekly,
            WeekDays = [DayOfWeek.Monday],
            Interval = 0,
            Month = 99,
            StartDate = new DateOnly(2026, 9, 20),
            EndDate = new DateOnly(2026, 9, 1),
        };
        var data = new AppData { Tasks = [task] };

        var warnings = DataSanitizer.Normalize(data);

        Assert.Equal(1, task.Interval);
        Assert.Equal(1, task.Month);
        Assert.Equal(task.StartDate, task.EndDate);
        Assert.NotEmpty(warnings);
    }

    [Fact]
    public void 任务时刻越界_调整为0900避免启动异常()
    {
        var task = new ReminderTask
        {
            Content = "坏时刻",
            Time = TimeSpan.FromHours(30),
            RecurrenceType = RecurrenceType.Daily,
        };
        var data = new AppData { Tasks = [task] };

        var warnings = DataSanitizer.Normalize(data);

        Assert.Equal(new TimeSpan(9, 0, 0), task.Time);
        Assert.Contains(warnings, w => w.Contains("提醒时刻越界"));
    }

    [Fact]
    public void 空星期与空任务记录_被归一化或过滤()
    {
        var task = new ReminderTask
        {
            Content = "空星期",
            RecurrenceType = RecurrenceType.Weekly,
            WeekDays = null!,
        };
        var data = new AppData { Tasks = [task, null!] };

        var warnings = DataSanitizer.Normalize(data);

        Assert.Single(data.Tasks);
        Assert.Empty(task.WeekDays);
        Assert.NotEmpty(warnings);
    }

    [Fact]
    public void 未知状态实例_按待提醒处理()
    {
        var occurrence = new Occurrence
        {
            TriggerAt = new DateTime(2026, 9, 18, 9, 0, 0),
            OriginalTriggerAt = new DateTime(2026, 9, 18, 9, 0, 0),
            State = (OccurrenceState)99,
        };
        var data = new AppData { Occurrences = [occurrence] };

        var warnings = DataSanitizer.Normalize(data);

        Assert.Equal(OccurrenceState.Pending, occurrence.State);
        Assert.NotEmpty(warnings);
    }

    [Fact]
    public void 缺少计划时刻的实例_忽略而不是生成0001年提醒()
    {
        var invalid = new Occurrence { State = OccurrenceState.Pending };
        var data = new AppData { Occurrences = [invalid] };

        var warnings = DataSanitizer.Normalize(data);

        Assert.Empty(data.Occurrences);
        Assert.Contains(warnings, w => w.Contains("缺少计划时刻"));
    }

    [Fact]
    public void 星期枚举含未定义值_过滤后编辑器可安全回填()
    {
        var task = new ReminderTask
        {
            Content = "脏星期",
            RecurrenceType = RecurrenceType.Weekly,
            WeekDays = [DayOfWeek.Monday, (DayOfWeek)99, DayOfWeek.Monday],
        };
        var data = new AppData { Tasks = [task] };

        var warnings = DataSanitizer.Normalize(data);

        Assert.Equal([DayOfWeek.Monday], task.WeekDays);
        Assert.Contains(warnings, w => w.Contains("星期配置包含无效值"));
        // 过滤后每个值都能安全用作 0..6 数组下标（编辑器回填依赖该前提）
        Assert.All(task.WeekDays, d => Assert.InRange((int)d, 0, 6));
    }

    [Fact]
    public void 任务ID重复或为空_重新分配保证唯一()
    {
        var duplicated = Guid.NewGuid();
        var first = new ReminderTask { Id = duplicated, Content = "A" };
        var second = new ReminderTask { Id = duplicated, Content = "B" };
        var empty = new ReminderTask { Id = Guid.Empty, Content = "C" };
        var data = new AppData { Tasks = [first, second, empty] };

        var warnings = DataSanitizer.Normalize(data);

        Assert.Equal(3, data.Tasks.Select(t => t.Id).Distinct().Count());
        Assert.DoesNotContain(data.Tasks, t => t.Id == Guid.Empty);
        Assert.Contains(warnings, w => w.Contains("重复"));
        Assert.Contains(warnings, w => w.Contains("缺少 ID"));
    }

    [Fact]
    public void 实例ID重复或为空_重新分配避免弹窗幂等键冲突()
    {
        var duplicated = Guid.NewGuid();
        var data = new AppData
        {
            Occurrences =
            [
                new Occurrence { Id = duplicated, TriggerAt = new DateTime(2026, 9, 18, 9, 0, 0) },
                new Occurrence { Id = duplicated, TriggerAt = new DateTime(2026, 9, 18, 10, 0, 0) },
                new Occurrence { Id = Guid.Empty, TriggerAt = new DateTime(2026, 9, 18, 11, 0, 0) },
            ],
        };

        var warnings = DataSanitizer.Normalize(data);

        Assert.Equal(3, data.Occurrences.Select(o => o.Id).Distinct().Count());
        Assert.DoesNotContain(data.Occurrences, o => o.Id == Guid.Empty);
        Assert.Contains(warnings, w => w.Contains("实例 ID"));
    }
}
