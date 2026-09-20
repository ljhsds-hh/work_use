using SpaceMaid.Core.Execution;
using SpaceMaid.Core.Models;

namespace SpaceMaid.Core.Tests.Execution;

public class CleanExecutorTests
{
    [Fact]
    public void Should_execute_only_planned_files()
    {
        using var h = new ExecutorHarness();
        var planned = h.Root.WriteFile(@"temp\planned.tmp", "planned");
        var notPlanned = h.Root.WriteFile(@"temp\not-planned.tmp", "not-planned");

        var (_, entry, planItem) = ExecutorHarness.MakeItem(
            "l1.user-temp",
            TargetRule.Contents(h.Root.Combine("temp")),
            new[] { h.ScanOf(planned) });

        var report = h.Executor.Execute(h.BuildPlan(new[] { planItem }, new[] { entry }), h.Options());

        Assert.Equal(1, report.MovedFileCount);
        Assert.False(File.Exists(planned));
        Assert.True(File.Exists(notPlanned), "清单外的文件绝不能被碰");
    }

    [Fact]
    public void Should_never_delete_without_quarantine_copy()
    {
        using var h = new ExecutorHarness();
        var planned = h.Root.WriteFile(@"temp\a.tmp", "content-a");

        var (_, entry, planItem) = ExecutorHarness.MakeItem(
            "l1.user-temp",
            TargetRule.Contents(h.Root.Combine("temp")),
            new[] { h.ScanOf(planned) });

        var report = h.Executor.Execute(h.BuildPlan(new[] { planItem }, new[] { entry }), h.Options());

        Assert.False(File.Exists(planned));
        Assert.NotNull(report.BatchId);

        var map = h.Service.ReadMaps(h.Options().QuarantineRoot).Single();
        var stored = map.Entries.Single(e => e.OriginalPath == planned);
        var payload = Path.Combine(h.Options().QuarantineRoot, map.BatchId, stored.StoredAs);
        Assert.True(File.Exists(payload), "源文件消失前必须先有隔离副本");
        Assert.Equal("content-a", File.ReadAllText(payload));
    }

    [Fact]
    public void Should_skip_locked_file_and_continue()
    {
        using var h = new ExecutorHarness();
        var free = h.Root.WriteFile(@"temp\free.tmp", "free");
        var locked = h.Root.WriteFile(@"temp\locked.tmp", "locked");

        var (_, entry, planItem) = ExecutorHarness.MakeItem(
            "l1.user-temp",
            TargetRule.Contents(h.Root.Combine("temp")),
            new[] { h.ScanOf(free), h.ScanOf(locked) });

        using (var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var report = h.Executor.Execute(h.BuildPlan(new[] { planItem }, new[] { entry }), h.Options());

            Assert.Equal(1, report.MovedFileCount);
            Assert.False(File.Exists(free));
            Assert.True(File.Exists(locked), "被占用的文件应被跳过");
            var skipped = Assert.Single(report.Skipped, s => s.Path == locked);
            Assert.Contains("占用", skipped.Reason);
        }
    }

    [Fact]
    public void Should_abort_without_touching_anything_when_quarantine_path_invalid()
    {
        using var h = new ExecutorHarness(quarantinePath: "   ");
        var planned = h.Root.WriteFile(@"temp\a.tmp", "a");

        var (_, entry, planItem) = ExecutorHarness.MakeItem(
            "l1.user-temp",
            TargetRule.Contents(h.Root.Combine("temp")),
            new[] { h.ScanOf(planned) });

        var report = h.Executor.Execute(h.BuildPlan(new[] { planItem }, new[] { entry }), h.Options());

        Assert.Equal(0, report.MovedFileCount);
        Assert.True(File.Exists(planned), "隔离区不可用时必须一个文件都不动");
        Assert.Contains(report.Skipped, s => s.Reason.Contains("隔离区不可用"));
        Assert.Contains(report.Items, i => i.Note != null && i.Note.Contains("隔离区不可用"));
    }

    [Fact]
    public void Should_not_touch_unchecked_items()
    {
        using var h = new ExecutorHarness();
        var uncheckedFile = h.Root.WriteFile(@"temp\unchecked.tmp", "u");

        var (_, entry, planItem) = ExecutorHarness.MakeItem(
            "l3.chat-cache",
            TargetRule.Contents(h.Root.Combine("temp")),
            new[] { h.ScanOf(uncheckedFile) },
            userChecked: false,
            category: CleanCategory.L3Cautious);

        var report = h.Executor.Execute(h.BuildPlan(new[] { planItem }, new[] { entry }), h.Options());

        Assert.Equal(0, report.MovedFileCount);
        Assert.True(File.Exists(uncheckedFile));
    }

    [Fact]
    public void Should_skip_item_when_definition_is_missing_from_scan()
    {
        using var h = new ExecutorHarness();
        var planned = h.Root.WriteFile(@"temp\a.tmp", "a");

        // 故意构造一份"扫描条目缺失"的计划：没有清理定义就没有允许路径，必须整项跳过
        var planItem = new CleanPlanItem(
            "ghost.item",
            CleanCategory.L1OneClick,
            "幽灵项",
            "移入隔离区",
            new[] { h.ScanOf(planned) },
            new FileInfo(planned).Length,
            true,
            true)
        {
            ActionKind = CleanActionKind.Quarantine
        };

        var report = h.Executor.Execute(h.BuildPlan(new[] { planItem }, Array.Empty<ScanEntry>()), h.Options());

        Assert.Equal(0, report.MovedFileCount);
        Assert.True(File.Exists(planned));
        Assert.Contains(report.Items, i => i.Note != null && i.Note.Contains("缺少该项的清理定义"));
    }

    [Fact]
    public void Should_mark_informational_item_as_not_executed()
    {
        using var h = new ExecutorHarness();
        var pagefile = h.Root.WriteFile(@"temp\pagefile.sys", "fake");

        var (_, entry, planItem) = ExecutorHarness.MakeItem(
            "l3.pagefile",
            TargetRule.File(pagefile),
            new[] { h.ScanOf(pagefile) },
            action: CleanActionKind.InformationalOnly,
            category: CleanCategory.L3Cautious);

        var report = h.Executor.Execute(h.BuildPlan(new[] { planItem }, new[] { entry }), h.Options());

        Assert.Equal(0, report.MovedFileCount);
        Assert.True(File.Exists(pagefile));
        Assert.Contains(report.Items, i => i.Note != null && i.Note.Contains("仅展示"));
    }

    [Fact]
    public void Should_report_special_action_as_not_executed_when_no_handler()
    {
        using var h = new ExecutorHarness();
        var (_, entry, planItem) = ExecutorHarness.MakeItem(
            "l3.hibernate",
            TargetRule.Contents(h.Root.Combine("temp")),
            Array.Empty<ScanFile>(),
            action: CleanActionKind.HibernateOff,
            category: CleanCategory.L3Cautious);

        var report = h.Executor.Execute(h.BuildPlan(new[] { planItem }, new[] { entry }), h.Options());

        Assert.Contains(report.Items, i => i.Note != null && i.Note.Contains("未接入专项处理器"));
    }

    [Fact]
    public void Should_respect_cancellation_between_files()
    {
        using var h = new ExecutorHarness();
        var first = h.Root.WriteFile(@"temp\1.tmp", "1");
        var second = h.Root.WriteFile(@"temp\2.tmp", "2");

        var (_, entry, planItem) = ExecutorHarness.MakeItem(
            "l1.user-temp",
            TargetRule.Contents(h.Root.Combine("temp")),
            new[] { h.ScanOf(first), h.ScanOf(second) });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var report = h.Executor.Execute(h.BuildPlan(new[] { planItem }, new[] { entry }), h.Options(), null, cts.Token);

        Assert.Equal(0, report.MovedFileCount);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.All(report.Skipped, s => Assert.Contains("取消", s.Reason));
    }

    [Fact]
    public void Should_report_same_volume_flag_for_quarantine_inside_source_volume()
    {
        using var h = new ExecutorHarness();
        var planned = h.Root.WriteFile(@"temp\a.tmp", "a");

        var (_, entry, planItem) = ExecutorHarness.MakeItem(
            "l1.user-temp",
            TargetRule.Contents(h.Root.Combine("temp")),
            new[] { h.ScanOf(planned) });

        var report = h.Executor.Execute(h.BuildPlan(new[] { planItem }, new[] { entry }), h.Options());

        Assert.True(report.SameVolume);
    }
}
