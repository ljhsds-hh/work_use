using QuietRemind.Models;
using QuietRemind.ViewModels;
using Xunit;

namespace QuietRemind.Tests;

public class TaskEditorViewModelTests
{
    private static TaskEditorViewModel NewVm() => new(null)
    {
        Content = "测试任务",
        SelectedTime = DateTime.Today.AddHours(10),
    };

    [Fact]
    public void 间隔大于1但未设置开始日期_保存被拒绝()
    {
        var vm = NewVm();
        vm.Recurrence = RecurrenceType.Weekly;
        vm.Mon = true;
        vm.Interval = 2;

        vm.Save();

        Assert.Null(vm.SavedTask);
        Assert.Contains("开始日期", vm.ErrorText);
    }

    [Fact]
    public void 间隔大于1且设置开始日期_保存间隔()
    {
        var vm = NewVm();
        vm.Recurrence = RecurrenceType.Daily;
        vm.Interval = 3;
        vm.StartDate = DateTime.Today;

        vm.Save();

        var task = Assert.IsType<ReminderTask>(vm.SavedTask);
        Assert.Equal(3, task.Interval);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), task.StartDate);
    }

    [Fact]
    public void 每年任务_保存月份与日期()
    {
        var vm = NewVm();
        vm.Recurrence = RecurrenceType.Yearly;
        vm.Month = 2;
        vm.MonthDay = 29;

        vm.Save();

        var task = Assert.IsType<ReminderTask>(vm.SavedTask);
        Assert.Equal(RecurrenceType.Yearly, task.RecurrenceType);
        Assert.Equal(2, task.Month);
        Assert.Equal(29, task.MonthDay);
        Assert.Equal(1, task.Interval);
    }

    [Fact]
    public void 结束日期早于今天_保存被拒绝()
    {
        var vm = NewVm();
        vm.Recurrence = RecurrenceType.Daily;
        vm.EndDate = DateTime.Today.AddDays(-1);

        vm.Save();

        Assert.Null(vm.SavedTask);
        Assert.Contains("结束日期", vm.ErrorText);
    }

    [Fact]
    public void 结束日期早于开始日期_保存被拒绝()
    {
        var vm = NewVm();
        vm.Recurrence = RecurrenceType.Daily;
        vm.StartDate = DateTime.Today.AddDays(5);
        vm.EndDate = DateTime.Today.AddDays(3);

        vm.Save();

        Assert.Null(vm.SavedTask);
        Assert.Contains("结束日期", vm.ErrorText);
    }

    [Fact]
    public void 编辑已结束任务_把开始日期改到原结束日期之后_被拒绝()
    {
        // 回归：结束日期未变时曾跳过全部校验，导致开始日期 > 结束日期仍可保存，
        // planner 因 d > endDate 直接不生成任何实例，任务静默失效
        var existing = new ReminderTask
        {
            Content = "已结束任务",
            RecurrenceType = RecurrenceType.Daily,
            Time = new TimeSpan(9, 0, 0),
            StartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-10)),
            EndDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),
        };
        var vm = new TaskEditorViewModel(existing)
        {
            SelectedTime = DateTime.Today.AddHours(10),
            StartDate = DateTime.Today,
        };

        vm.Save();

        Assert.Null(vm.SavedTask);
        Assert.Contains("结束日期", vm.ErrorText);
    }

    [Fact]
    public void 编辑已结束任务_保留过去结束日期仍可修改其他字段()
    {
        var existing = new ReminderTask
        {
            Content = "已结束任务",
            RecurrenceType = RecurrenceType.Daily,
            Time = new TimeSpan(9, 0, 0),
            StartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-10)),
            EndDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),
        };
        var vm = new TaskEditorViewModel(existing)
        {
            Content = "改名后仍可保存",
            SelectedTime = DateTime.Today.AddHours(10),
        };

        vm.Save();

        var task = Assert.IsType<ReminderTask>(vm.SavedTask);
        Assert.Equal("改名后仍可保存", task.Content);
        Assert.Equal(existing.EndDate, task.EndDate);
    }

    [Fact]
    public void 历史任务含无效星期值_编辑窗可正常打开()
    {
        var existing = new ReminderTask
        {
            Content = "脏星期",
            RecurrenceType = RecurrenceType.Weekly,
            WeekDays = [(DayOfWeek)99],
        };

        var vm = new TaskEditorViewModel(existing);

        Assert.False(vm.Mon);
        Assert.False(vm.Sun);
    }

    [Fact]
    public void 工作日预设_只勾选周一至周五()
    {
        var vm = new TaskEditorViewModel(null);

        vm.ApplyWorkdays();

        Assert.True(vm.Mon);
        Assert.True(vm.Tue);
        Assert.True(vm.Wed);
        Assert.True(vm.Thu);
        Assert.True(vm.Fri);
        Assert.False(vm.Sat);
        Assert.False(vm.Sun);
    }
}
