using SpaceMaid.Core.Models;
using SpaceMaid.Core.Platform;
using SpaceMaid.Core.Scanning;
using SpaceMaid.Core.Tests.Abstractions;

namespace SpaceMaid.Core.Tests.Scanning;

public class ScanEngineTests
{
    private static CleanItemDefinition Item(params TargetRule[] rules) => new()
    {
        Id = "test.item",
        Category = CleanCategory.L1OneClick,
        DisplayName = "测试项",
        Risk = ItemRisk.Safe,
        ActionKind = CleanActionKind.Quarantine,
        Targets = rules,
        AutoRegenerated = true,
        DefaultChecked = true
    };

    private static ScanEngine CreateEngine(FakeClock clock) =>
        new(new WindowsFileSystem(), new WindowsEnvironmentProbe(), new ScanFakeVolumeProbe(), clock, new ScanFakeCapacityProbe());

    [Fact]
    public async Task Should_report_sizes_and_counts()
    {
        using var root = new TempRoot();
        root.WriteFile(@"cache\a.bin", new string('a', 1024));
        root.WriteFile(@"cache\b.bin", new string('b', 2048));
        root.WriteFile(@"cache\c.bin", new string('c', 3072));

        var item = Item(TargetRule.Contents(root.Combine("cache")));
        var clock = new FakeClock(DateTimeOffset.Now);

        var report = await CreateEngine(clock).ScanAsync(new ScanRequest(new[] { item }, false), null, CancellationToken.None);

        var entry = report.Entries.Single();
        Assert.True(entry.Available);
        Assert.Equal(6144, entry.TotalBytes);
        Assert.Equal(3, entry.FileCount);
        Assert.Equal(root.Combine("cache"), Path.GetDirectoryName(entry.Files[0].Path));
    }

    [Fact]
    public async Task Should_be_cancellable()
    {
        using var root = new TempRoot();
        root.WriteFile(@"cache\a.bin", "x");

        var item = Item(TargetRule.Contents(root.Combine("cache")));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateEngine(new FakeClock(DateTimeOffset.Now)).ScanAsync(new ScanRequest(new[] { item }, false), null, cts.Token));
    }

    [Fact]
    public async Task Should_mark_missing_targets_unavailable()
    {
        using var root = new TempRoot();
        var missing = root.Combine("not-exists");
        var item = Item(TargetRule.Contents(missing));

        var report = await CreateEngine(new FakeClock(DateTimeOffset.Now))
            .ScanAsync(new ScanRequest(new[] { item }, false), null, CancellationToken.None);

        var entry = report.Entries.Single();
        Assert.False(entry.Available);
        Assert.NotNull(entry.UnavailableReason);
        Assert.Contains("不存在", entry.UnavailableReason);
    }

    [Fact]
    public async Task Should_not_write_anything()
    {
        using var root = new TempRoot();
        root.WriteFile(@"cache\a.bin", new string('a', 128));
        root.WriteFile(@"cache\b.bin", new string('b', 64));

        var before = Snapshot(root.Combine("cache"));
        var item = Item(TargetRule.Tree(root.Combine("cache")));

        await CreateEngine(new FakeClock(DateTimeOffset.Now))
            .ScanAsync(new ScanRequest(new[] { item }, false), null, CancellationToken.None);

        var after = Snapshot(root.Combine("cache"));
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Should_apply_windows_old_grace_window_on_real_directory()
    {
        using var root = new TempRoot();
        root.WriteFile(@"windows.old\a.dat", new string('a', 32));

        var item = new CleanItemDefinition
        {
            Id = ScanningRules.WindowsOldItemId,
            Category = CleanCategory.L2Recommended,
            DisplayName = "旧系统残留",
            Risk = ItemRisk.Safe,
            ActionKind = CleanActionKind.Quarantine,
            Targets = new[] { TargetRule.Tree(root.Combine("windows.old")) },
            AutoRegenerated = true,
            DefaultChecked = true
        };

        var report = await CreateEngine(new FakeClock(DateTimeOffset.Now))
            .ScanAsync(new ScanRequest(new[] { item }, false), null, CancellationToken.None);

        var entry = report.Entries.Single();
        Assert.False(entry.Item.DefaultChecked);      // 刚刚创建的目录 = 升级窗口还开着
        Assert.NotNull(entry.UnavailableReason);
        Assert.Contains("回退窗口", entry.UnavailableReason);
    }

    [Fact]
    public async Task Should_report_volume_snapshot()
    {
        using var root = new TempRoot();
        root.WriteFile(@"cache\a.bin", "x");
        var item = Item(TargetRule.Contents(root.Combine("cache")));

        var report = await CreateEngine(new FakeClock(DateTimeOffset.Now))
            .ScanAsync(new ScanRequest(new[] { item }, false), null, CancellationToken.None);

        Assert.Equal(new ScanFakeCapacityProbe().TotalBytes, report.Volume.TotalBytes);
        Assert.True(report.Volume.FreeBytes > 0);
    }

    private static string Snapshot(string directory)
    {
        var lines = Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(p => $"{Path.GetFileName(p)}|{new FileInfo(p).Length}|{File.GetLastWriteTimeUtc(p):O}");
        return string.Join("\n", lines);
    }
}
