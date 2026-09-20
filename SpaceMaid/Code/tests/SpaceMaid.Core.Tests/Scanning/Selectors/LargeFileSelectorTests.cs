using SpaceMaid.Core.Models;
using SpaceMaid.Core.Platform;
using SpaceMaid.Core.Scanning;
using SpaceMaid.Core.Scanning.Selectors;
using SpaceMaid.Core.Tests.Abstractions;

namespace SpaceMaid.Core.Tests.Scanning.Selectors;

/// <summary>
/// `l3.large-files`（大文件）候选收窄器的用例。
/// 收窄器是纯决策逻辑（不读磁盘），因此用合成的 ScanFile 列表就能穷尽断言排序与阈值行为。
/// </summary>
public class LargeFileSelectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Defaults_should_match_the_documented_thresholds()
    {
        Assert.Equal("l3.large-files", LargeFileSelector.ItemId);
        Assert.Equal(100L * 1024 * 1024, LargeFileSelector.MinSizeBytes);
        Assert.Equal(200, LargeFileSelector.MaxCount);
    }

    [Fact]
    public void Should_handle_only_its_own_item()
    {
        var selector = new LargeFileSelector();

        Assert.True(selector.CanHandle(LargeFileSelector.ItemId));
        Assert.False(selector.CanHandle("l3.duplicate-files"));
        Assert.False(selector.CanHandle("l3.orphan-app-dirs"));
    }

    [Fact]
    public void Should_drop_files_below_the_size_threshold()
    {
        using var root = new TempRoot();

        var candidates = new[]
        {
            SelectorTestData.File(root.Combine("big.bin"), LargeFileSelector.MinSizeBytes, Now),
            SelectorTestData.File(root.Combine("just-below.bin"), LargeFileSelector.MinSizeBytes - 1, Now),
            SelectorTestData.File(root.Combine("empty.bin"), 0, Now)
        };

        var selected = new LargeFileSelector().Select(
            SelectorTestData.Item(LargeFileSelector.ItemId),
            candidates,
            new WindowsFileSystem(),
            new RecordingLogSink());

        Assert.Single(selected);
        Assert.Equal("big.bin", Path.GetFileName(selected[0].Path));
    }

    [Fact]
    public void Should_keep_only_the_largest_when_more_than_max_count()
    {
        using var root = new TempRoot();

        var candidates = Enumerable
            .Range(1, 5)
            .Select(index => SelectorTestData.File(root.Combine($"f{index}.bin"), index * 10L, Now))
            .ToList();

        var selector = new LargeFileSelector(minSizeBytes: 1, maxCount: 3);

        var selected = selector.Select(
            SelectorTestData.Item(LargeFileSelector.ItemId),
            candidates,
            new WindowsFileSystem(),
            new RecordingLogSink());

        Assert.Equal(3, selected.Count);
        Assert.Equal(new[] { 50L, 40L, 30L }, selected.Select(file => file.Size).ToArray());
    }

    [Fact]
    public void Should_sort_by_size_descending_then_by_path()
    {
        using var root = new TempRoot();

        var candidates = new[]
        {
            SelectorTestData.File(root.Combine("z.bin"), 100, Now),
            SelectorTestData.File(root.Combine("m.bin"), 200, Now),
            SelectorTestData.File(root.Combine("a.bin"), 100, Now)
        };

        var selected = new LargeFileSelector(minSizeBytes: 1).Select(
            SelectorTestData.Item(LargeFileSelector.ItemId),
            candidates,
            new WindowsFileSystem(),
            new RecordingLogSink());

        Assert.Equal(
            new[] { "m.bin", "a.bin", "z.bin" },
            selected.Select(file => Path.GetFileName(file.Path)).ToArray());
    }

    [Fact]
    public void Should_never_change_the_item_or_its_checked_state()
    {
        // 需求 1.2-9 / 5.4-1：大文件是展示型条目，收窄器只决定"列出哪些"，
        // 绝不替用户勾选（DefaultChecked 必须保持 false）。
        using var root = new TempRoot();
        var item = SelectorTestData.Item(LargeFileSelector.ItemId);
        var candidates = new[] { SelectorTestData.File(root.Combine("big.bin"), LargeFileSelector.MinSizeBytes, Now) };

        var selected = new LargeFileSelector().Select(item, candidates, new WindowsFileSystem(), new RecordingLogSink());

        Assert.False(item.DefaultChecked);
        Assert.False(item.AutoRegenerated);
        Assert.Equal(CleanActionKind.Quarantine, selected[0].Action);
    }
}
