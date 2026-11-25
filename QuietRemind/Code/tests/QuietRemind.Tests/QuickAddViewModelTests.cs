using QuietRemind.Models;
using QuietRemind.ViewModels;
using Xunit;

namespace QuietRemind.Tests;

public class QuickAddViewModelTests
{
    [Fact]
    public void 跨午夜_默认日期顺延到明天_可正常保存()
    {
        var vm = new QuickAddViewModel(new DateTime(2026, 11, 24, 23, 58, 0))
        {
            Content = "喝水",
            IsOnce = true,
        };

        Assert.Equal(new DateTime(2026, 11, 25), vm.SingleDate);
        Assert.Equal(new TimeSpan(0, 3, 0), vm.SelectedTime!.Value.TimeOfDay);

        // 保存校验使用真实当前时间，这里改用未来日期避免测试依赖运行日期
        vm.SingleDate = DateTime.Today.AddDays(10);
        vm.Save();

        var task = Assert.IsType<ReminderTask>(vm.SavedTask);
        Assert.Equal(RecurrenceType.Once, task.RecurrenceType);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today.AddDays(10)), task.SingleDate);
    }

    [Fact]
    public void 单次任务_过去时间被拒绝()
    {
        var vm = new QuickAddViewModel(new DateTime(2026, 11, 24, 10, 0, 0))
        {
            Content = "已过时间",
            IsOnce = true,
            SingleDate = DateTime.Today.AddDays(-1),
        };

        vm.Save();

        Assert.Null(vm.SavedTask);
        Assert.Contains("晚于当前时间", vm.ErrorText);
    }

    [Fact]
    public void 每天任务_无需日期_保存成功()
    {
        var vm = new QuickAddViewModel(new DateTime(2026, 11, 24, 10, 0, 0))
        {
            Content = "每日站会",
        };

        vm.Save();

        var task = Assert.IsType<ReminderTask>(vm.SavedTask);
        Assert.Equal(RecurrenceType.Daily, task.RecurrenceType);
        Assert.Null(task.SingleDate);
        Assert.Equal(1, task.Interval);
    }
}
