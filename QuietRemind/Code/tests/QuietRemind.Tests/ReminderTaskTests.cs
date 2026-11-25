using QuietRemind.Models;
using Xunit;

namespace QuietRemind.Tests;

public class ReminderTaskTests
{
    private static ReminderTask Sample() => new()
    {
        Content = "周会",
        Time = new TimeSpan(9, 30, 0),
        RecurrenceType = RecurrenceType.Weekly,
        WeekDays = [DayOfWeek.Monday, DayOfWeek.Friday],
        StartDate = new DateOnly(2026, 9, 1),
        EndDate = new DateOnly(2026, 12, 31),
        Interval = 2,
        CreatedAt = new DateTime(2026, 9, 1, 8, 0, 0),
        UpdatedAt = new DateTime(2026, 9, 1, 8, 0, 0),
    };

    [Fact]
    public void 克隆_排期字段一致且星期数组互不影响()
    {
        var source = Sample();
        var clone = source.Clone();

        Assert.True(source.HasSameScheduleAs(clone));
        Assert.Equal(source.Content, clone.Content);
        Assert.NotSame(source.WeekDays, clone.WeekDays);
        clone.WeekDays[0] = DayOfWeek.Sunday;
        Assert.Equal(DayOfWeek.Monday, source.WeekDays[0]);
    }

    [Fact]
    public void 仅改内容或启用状态_不视为排期变化()
    {
        var before = Sample();
        var after = before.Clone();
        after.Content = "改名";
        after.Enabled = false;
        after.UpdatedAt = DateTime.Now;

        Assert.True(before.HasSameScheduleAs(after));
    }

    [Fact]
    public void 星期顺序不同不视为排期变化_时刻或间隔变化视为排期变化()
    {
        var before = Sample();
        var reordered = before.Clone();
        reordered.WeekDays = [DayOfWeek.Friday, DayOfWeek.Monday];
        Assert.True(before.HasSameScheduleAs(reordered));

        var timeChanged = before.Clone();
        timeChanged.Time = new TimeSpan(10, 0, 0);
        Assert.False(before.HasSameScheduleAs(timeChanged));

        var intervalChanged = before.Clone();
        intervalChanged.Interval = 3;
        Assert.False(before.HasSameScheduleAs(intervalChanged));

        var dayChanged = before.Clone();
        dayChanged.WeekDays = [DayOfWeek.Monday];
        Assert.False(before.HasSameScheduleAs(dayChanged));
    }
}
