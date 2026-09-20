using SpaceMaid.Core.Models;
using SpaceMaid.Core.Platform;
using SpaceMaid.Core.Scanning;
using SpaceMaid.Core.Scanning.Selectors;
using SpaceMaid.Core.Tests.Abstractions;

namespace SpaceMaid.Core.Tests.Scanning.Selectors;

/// <summary>
/// "候选收窄器只能收窄"的回归测试——守的是**扫描层的安全性质**，而不是某个具体收窄器的行为。
///
/// 背景：收窄器是扫描层唯一的扩展点，允许它"扩大候选"等于给清单开了后门
/// （清单是落地阶段唯一的输入，见设计决策 D-7）。因此 ScanEngine 在调用点做了硬守卫：
/// ① 返回值里不属于原候选集的文件一律丢弃；② 收窄器抛异常时按空集处理（失败关闭）。
/// </summary>
public class CandidateSelectorNarrowingTests
{
    private const string ItemId = "test.item";

    private static CleanItemDefinition Item(TargetRule rule) => new()
    {
        Id = ItemId,
        Category = CleanCategory.L3Cautious,
        DisplayName = "测试项",
        Risk = ItemRisk.Caution,
        ActionKind = CleanActionKind.Quarantine,
        Targets = new[] { rule },
        AutoRegenerated = false,
        DefaultChecked = false
    };

    private static ScanEngine CreateEngine(FakeClock clock, params IItemCandidateSelector[] selectors) => new(
        new WindowsFileSystem(),
        new WindowsEnvironmentProbe(),
        new ScanFakeVolumeProbe(),
        clock,
        new ScanFakeCapacityProbe(),
        recycleBin: null,
        log: null,
        selectors: selectors.Length == 0 ? null : selectors);

    [Fact]
    public async Task Selector_should_only_narrow_the_candidate_set()
    {
        using var root = new TempRoot();
        root.WriteFile(@"cache\a.bin", new string('a', 16));
        root.WriteFile(@"cache\b.bin", new string('b', 32));
        root.WriteFile(@"cache\c.bin", new string('c', 64));

        var item = Item(TargetRule.Contents(root.Combine("cache")));
        var engine = CreateEngine(
            new FakeClock(DateTimeOffset.Now),
            new ScriptedSelector(ItemId, files => files.Take(1).ToList()));

        var report = await engine.ScanAsync(new ScanRequest(new[] { item }, false), null, CancellationToken.None);

        var entry = report.Entries.Single();
        Assert.Equal(1, entry.FileCount);
        Assert.Equal(1, report.TotalFiles);
    }

    [Fact]
    public async Task Selector_should_not_be_able_to_add_files()
    {
        using var root = new TempRoot();
        root.WriteFile(@"cache\a.bin", new string('a', 16));
        root.WriteFile(@"cache\b.bin", new string('b', 32));
        root.WriteFile(@"cache\c.bin", new string('c', 64));

        var item = Item(TargetRule.Contents(root.Combine("cache")));
        var ghost = root.Combine(@"cache\ghost.bin");

        var engine = CreateEngine(
            new FakeClock(DateTimeOffset.Now),
            new ScriptedSelector(ItemId, files => files
                .Concat(new[] { new ScanFile(ghost, 999_999, DateTimeOffset.Now, CleanActionKind.Quarantine) })
                .ToList()));

        var report = await engine.ScanAsync(new ScanRequest(new[] { item }, false), null, CancellationToken.None);

        var entry = report.Entries.Single();
        Assert.Equal(3, entry.FileCount);
        Assert.DoesNotContain(entry.Files, file => file.Path.Equals(ghost, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(16 + 32 + 64, entry.TotalBytes);
    }

    [Fact]
    public async Task Selector_should_not_be_able_to_duplicate_a_candidate()
    {
        using var root = new TempRoot();
        root.WriteFile(@"cache\a.bin", new string('a', 16));
        root.WriteFile(@"cache\b.bin", new string('b', 32));

        var item = Item(TargetRule.Contents(root.Combine("cache")));
        var engine = CreateEngine(
            new FakeClock(DateTimeOffset.Now),
            new ScriptedSelector(ItemId, files => files.Concat(files).ToList()));

        var report = await engine.ScanAsync(new ScanRequest(new[] { item }, false), null, CancellationToken.None);

        Assert.Equal(2, report.Entries.Single().FileCount);
    }

    [Fact]
    public async Task Selector_failure_should_close_not_open()
    {
        using var root = new TempRoot();
        root.WriteFile(@"cache\a.bin", new string('a', 16));
        root.WriteFile(@"cache\b.bin", new string('b', 32));

        var item = Item(TargetRule.Contents(root.Combine("cache")));
        var engine = CreateEngine(
            new FakeClock(DateTimeOffset.Now),
            new ScriptedSelector(ItemId, _ => throw new InvalidOperationException("收窄器挂了")));

        var report = await engine.ScanAsync(new ScanRequest(new[] { item }, false), null, CancellationToken.None);

        var entry = report.Entries.Single();
        Assert.Empty(entry.Files);
        Assert.Equal(0, entry.TotalBytes);
    }

    [Fact]
    public async Task Engine_without_selectors_should_behave_exactly_as_before()
    {
        using var root = new TempRoot();
        root.WriteFile(@"cache\a.bin", new string('a', 16));
        root.WriteFile(@"cache\b.bin", new string('b', 32));
        root.WriteFile(@"cache\c.bin", new string('c', 64));

        var item = Item(TargetRule.Contents(root.Combine("cache")));
        var clock = new FakeClock(DateTimeOffset.Now);

        var withoutSelectors = CreateEngine(clock);
        var withEmptySelectors = new ScanEngine(
            new WindowsFileSystem(),
            new WindowsEnvironmentProbe(),
            new ScanFakeVolumeProbe(),
            clock,
            new ScanFakeCapacityProbe(),
            selectors: Array.Empty<IItemCandidateSelector>());

        var first = await withoutSelectors.ScanAsync(new ScanRequest(new[] { item }, false), null, CancellationToken.None);
        var second = await withEmptySelectors.ScanAsync(new ScanRequest(new[] { item }, false), null, CancellationToken.None);

        Assert.Equal(3, first.Entries.Single().FileCount);
        Assert.Equal(first.TotalFiles, second.TotalFiles);
        Assert.Equal(first.TotalBytes, second.TotalBytes);
    }

    [Fact]
    public async Task Large_files_item_should_still_be_unchecked_after_narrowing()
    {
        // 需求 1.2-9 / 5.4-1：大文件是展示型条目，收窄只是筛出"值得看的"，绝不替用户勾选。
        using var root = new TempRoot();
        root.WriteFile(@"cache\big.bin", new string('a', 128));

        var item = new CleanItemDefinition
        {
            Id = LargeFileSelector.ItemId,
            Category = CleanCategory.L3Cautious,
            DisplayName = "大文件",
            Risk = ItemRisk.Caution,
            ActionKind = CleanActionKind.Quarantine,
            Targets = new[] { TargetRule.Contents(root.Combine("cache")) },
            DefaultChecked = false
        };

        var engine = CreateEngine(new FakeClock(DateTimeOffset.Now), new LargeFileSelector(minSizeBytes: 64));

        var report = await engine.ScanAsync(new ScanRequest(new[] { item }, false), null, CancellationToken.None);

        var entry = report.Entries.Single();
        Assert.Equal(1, entry.FileCount);
        Assert.False(entry.Item.DefaultChecked);
        Assert.False(entry.Item.AutoRegenerated);
    }

    [Fact]
    public async Task Large_file_selector_should_drop_small_files_inside_the_engine()
    {
        using var root = new TempRoot();
        root.WriteFile(@"cache\big.bin", new string('a', 256));
        root.WriteFile(@"cache\small.bin", new string('b', 8));

        var item = new CleanItemDefinition
        {
            Id = LargeFileSelector.ItemId,
            Category = CleanCategory.L3Cautious,
            DisplayName = "大文件",
            Risk = ItemRisk.Caution,
            ActionKind = CleanActionKind.Quarantine,
            Targets = new[] { TargetRule.Contents(root.Combine("cache")) },
            DefaultChecked = false
        };

        var engine = CreateEngine(new FakeClock(DateTimeOffset.Now), new LargeFileSelector(minSizeBytes: 128));

        var report = await engine.ScanAsync(new ScanRequest(new[] { item }, false), null, CancellationToken.None);

        var entry = report.Entries.Single();
        Assert.Equal(1, entry.FileCount);
        Assert.Equal(256, entry.TotalBytes);
        Assert.EndsWith("big.bin", entry.Files[0].Path, StringComparison.OrdinalIgnoreCase);
    }
}
