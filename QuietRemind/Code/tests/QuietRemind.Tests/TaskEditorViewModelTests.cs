using QuietRemind.Models;
using QuietRemind.ViewModels;
using Xunit;

namespace QuietRemind.Tests;

/// <summary>任务编辑表单：时刻时/分选择写入与保存校验。</summary>
public class TaskEditorViewModelTests
{
    [Fact]
    public void 新增任务_默认时刻为0900()
    {
        var vm = new TaskEditorViewModel(null);
        Assert.Equal(9, vm.SelectedHour);
        Assert.Equal(0, vm.SelectedMinute);
    }

    [Fact]
    public void 编辑任务_回显已有时刻()
    {
        var existing = new ReminderTask { Content = "已有任务", Time = new TimeSpan(14, 35, 20) };
        var vm = new TaskEditorViewModel(existing);
        Assert.Equal(14, vm.SelectedHour);
        Assert.Equal(35, vm.SelectedMinute);
    }

    [Fact]
    public void 保存_按所选时分写入任务时刻_秒清零()
    {
        var vm = new TaskEditorViewModel(null)
        {
            Content = "喝水",
            SelectedHour = 7,
            SelectedMinute = 5,
        };
        vm.Save();
        Assert.NotNull(vm.SavedTask);
        Assert.Equal(new TimeSpan(7, 5, 0), vm.SavedTask!.Time);
    }

    [Fact]
    public void 时刻文本_按时分两位补零()
    {
        var vm = new TaskEditorViewModel(null) { SelectedHour = 7, SelectedMinute = 5 };
        Assert.Equal("07:05", vm.TimeText);
    }

    [Fact]
    public void 保存_内容为空_提示错误不产出()
    {
        var vm = new TaskEditorViewModel(null) { Content = "  " };
        vm.Save();
        Assert.Null(vm.SavedTask);
        Assert.Equal("任务内容不能为空", vm.ErrorText);
    }

    [Fact]
    public void 保存_单次任务未选日期_提示错误()
    {
        var vm = new TaskEditorViewModel(null) { Content = "报税", Recurrence = RecurrenceType.Once };
        vm.Save();
        Assert.Null(vm.SavedTask);
        Assert.Equal("单次任务需指定日期", vm.ErrorText);
    }

    [Fact]
    public void 保存_每周任务未勾选星期_提示错误()
    {
        var vm = new TaskEditorViewModel(null) { Content = "周报", Recurrence = RecurrenceType.Weekly };
        vm.Save();
        Assert.Null(vm.SavedTask);
        Assert.Equal("每周任务至少勾选一天", vm.ErrorText);
    }
}
