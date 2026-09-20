using SpaceMaid.Core.Models;
using SpaceMaid.Core.Scanning;

namespace SpaceMaid.Core.Tests.Scanning;

public class ScanningRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.FromHours(8));

    private static CleanItemDefinition Item(
        string id = "test.item",
        TimeSpan? minAge = null,
        bool keepsNewest = false,
        string? availabilityNote = null) => new()
        {
            Id = id,
            Category = CleanCategory.L1OneClick,
            DisplayName = "测试项",
            Risk = ItemRisk.Safe,
            ActionKind = CleanActionKind.Quarantine,
            Targets = new[] { TargetRule.Tree(@"C:\Temp") },
            AutoRegenerated = true,
            DefaultChecked = true,
            MinAge = minAge,
            KeepsNewest = keepsNewest,
            AvailabilityNote = availabilityNote
        };

    private static ScanFile File(string name, long size, DateTimeOffset lastWrite) =>
        new(name, size, lastWrite, CleanActionKind.Quarantine);

    [Fact]
    public void Should_filter_by_min_age()
    {
        var files = new[]
        {
            File(@"C:\Temp\old.tmp", 100, Now.AddDays(-10)),
            File(@"C:\Temp\new.tmp", 200, Now.AddHours(-1))
        };

        var outcome = ScanningRules.Apply(Item(minAge: TimeSpan.FromDays(1)), files, Now);

        Assert.Single(outcome.Files);
        Assert.Equal(@"C:\Temp\old.tmp", outcome.Files[0].Path);
        Assert.Equal(1, outcome.SkippedCount);
    }

    [Fact]
    public void Should_keep_newest_dump()
    {
        var files = new[]
        {
            File(@"C:\Windows\Minidump\a.dmp", 100, Now.AddDays(-5)),
            File(@"C:\Windows\MEMORY.DMP", 2048, Now.AddDays(-1)),
            File(@"C:\Windows\Minidump\b.dmp", 300, Now.AddDays(-9))
        };

        var outcome = ScanningRules.Apply(Item(keepsNewest: true), files, Now);

        Assert.Single(outcome.Kept);
        Assert.Equal(@"C:\Windows\MEMORY.DMP", outcome.Kept[0].Path);
        Assert.Equal(2, outcome.Files.Count);
        Assert.DoesNotContain(outcome.Files, f => f.Path == @"C:\Windows\MEMORY.DMP");
    }

    [Fact]
    public void Should_flag_windows_old_within_ten_days()
    {
        var item = Item(id: ScanningRules.WindowsOldItemId);
        var files = new[] { File(@"C:\Windows.old\a.dat", 100, Now.AddDays(-1)) };

        var outcome = ScanningRules.Apply(item, files, Now, targetCreatedAt: Now.AddDays(-3));

        Assert.NotNull(outcome.Note);
        Assert.Contains("回退窗口", outcome.Note);
        Assert.False(outcome.Item.DefaultChecked);
    }

    [Fact]
    public void Should_not_flag_windows_old_after_ten_days()
    {
        var item = Item(id: ScanningRules.WindowsOldItemId);
        var files = new[] { File(@"C:\Windows.old\a.dat", 100, Now.AddDays(-30)) };

        var outcome = ScanningRules.Apply(item, files, Now, targetCreatedAt: Now.AddDays(-30));

        Assert.Null(outcome.Note);
        Assert.True(outcome.Item.DefaultChecked);
    }

    [Fact]
    public void Should_carry_definition_availability_note_when_no_time_based_note()
    {
        var item = Item(availabilityNote: "需要系统支持 XX 功能");

        var outcome = ScanningRules.Apply(item, Array.Empty<ScanFile>(), Now);

        Assert.Equal("需要系统支持 XX 功能", outcome.Note);
    }
}
