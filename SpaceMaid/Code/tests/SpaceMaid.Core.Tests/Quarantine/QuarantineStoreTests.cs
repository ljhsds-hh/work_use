using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Platform;
using SpaceMaid.Core.Quarantine;
using SpaceMaid.Core.Tests.Abstractions;

namespace SpaceMaid.Core.Tests.Quarantine;

public class QuarantineStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 14, 30, 12, TimeSpan.FromHours(8));

    private static PlannedFile Planned(string path, long size = 0) =>
        new("test.item", CleanCategory.L1OneClick, path, size, Now.AddDays(-1));

    private static QuarantineStore CreateStore(IFileSystem fs, IVolumeProbe volumes, DateTimeOffset? now = null) =>
        new(fs, volumes, new QuarantineFakeClock(now ?? Now));

    [Fact]
    public void Should_move_file_and_delete_source_when_same_volume()
    {
        using var root = new TempRoot();
        var source = root.WriteFile(@"source\a.tmp", new string('a', 512));
        var quarantine = root.Combine("quarantine");
        Directory.CreateDirectory(quarantine);

        var fs = new WindowsFileSystem();
        var store = CreateStore(fs, new MappedVolumeProbe());

        var result = store.StoreCore(quarantine, 7, new[] { Planned(source, 512) }, null, CancellationToken.None);

        Assert.Equal(1, result.StoredCount);
        Assert.Equal(512, result.StoredBytes);
        Assert.False(result.Cancelled);
        Assert.False(File.Exists(source));                                  // 源已不在原位
        var entry = result.StoredEntries.Single();
        Assert.True(File.Exists(Path.Combine(result.BatchDirectory, entry.StoredAs)));  // 副本在隔离区
        Assert.Equal(MapEntryStatus.Stored, entry.Status);
    }

    [Fact]
    public void Should_copy_verify_then_delete_when_cross_volume()
    {
        using var root = new TempRoot();
        var source = root.WriteFile(@"source\b.tmp", new string('b', 1024));
        var quarantine = root.Combine("quarantine");
        Directory.CreateDirectory(quarantine);

        var volumes = new MappedVolumeProbe();
        volumes.MapTo(root.Combine("source"), @"C:\");
        volumes.MapTo(quarantine, @"X:\");

        var store = CreateStore(new WindowsFileSystem(), volumes);

        var result = store.StoreCore(quarantine, 7, new[] { Planned(source, 1024) }, null, CancellationToken.None);

        Assert.Equal(1, result.StoredCount);
        Assert.False(File.Exists(source));
        var payload = Path.Combine(result.BatchDirectory, result.StoredEntries.Single().StoredAs);
        Assert.Equal(new string('b', 1024), File.ReadAllText(payload));
    }

    [Fact]
    public void Should_keep_source_when_copy_fails()
    {
        using var root = new TempRoot();
        var source = root.WriteFile(@"source\c.tmp", "content");
        var quarantine = root.Combine("quarantine");
        Directory.CreateDirectory(quarantine);

        var volumes = new MappedVolumeProbe();
        volumes.MapTo(root.Combine("source"), @"C:\");
        volumes.MapTo(quarantine, @"X:\");

        var fs = new FailingCopyFileSystem(new WindowsFileSystem()) { FailCopy = true };
        var store = CreateStore(fs, volumes);

        var result = store.StoreCore(quarantine, 7, new[] { Planned(source, 7) }, null, CancellationToken.None);

        Assert.Equal(0, result.StoredCount);
        Assert.True(File.Exists(source));                     // 源文件必须保留
        var skipped = Assert.Single(result.Skipped);
        Assert.Contains("复制失败", skipped.Reason);
        Assert.Empty(result.StoredEntries);                   // 失败项不记账
    }

    [Fact]
    public void Should_preserve_original_path_mapping_for_tricky_names()
    {
        using var root = new TempRoot();
        var source = root.WriteFile(@"source\中文 文件 (1).tmp", "x");
        var quarantine = root.Combine("quarantine");
        Directory.CreateDirectory(quarantine);

        var store = CreateStore(new WindowsFileSystem(), new MappedVolumeProbe());
        var result = store.StoreCore(quarantine, 7, new[] { Planned(source, 1) }, null, CancellationToken.None);

        var entry = result.StoredEntries.Single();
        Assert.Equal(source, entry.OriginalPath);
        Assert.Contains("中文 文件 (1).tmp", entry.StoredAs);

        // 账本落盘后仍可读回，且中文未被转义成 \uXXXX
        var mapJson = File.ReadAllText(Path.Combine(result.BatchDirectory, QuarantineMapStore.FileName));
        Assert.Contains("中文 文件", mapJson);
        var map = QuarantineMapStore.TryRead(result.BatchDirectory);
        Assert.NotNull(map);
        Assert.False(map!.Pending);
        Assert.Equal(source, map.Entries.Single().OriginalPath);
    }

    [Fact]
    public void Should_mark_unprocessed_files_as_skipped_when_cancelled()
    {
        using var root = new TempRoot();
        var first = root.WriteFile(@"source\1.tmp", "1");
        var second = root.WriteFile(@"source\2.tmp", "2");
        var quarantine = root.Combine("quarantine");
        Directory.CreateDirectory(quarantine);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var store = CreateStore(new WindowsFileSystem(), new MappedVolumeProbe());
        var result = store.StoreCore(quarantine, 7, new[] { Planned(first, 1), Planned(second, 1) }, null, cts.Token);

        Assert.True(result.Cancelled);
        Assert.Equal(0, result.StoredCount);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.Equal(2, result.Skipped.Count);
        Assert.All(result.Skipped, s => Assert.Contains("取消", s.Reason));
    }

    [Fact]
    public void Should_write_pending_ledger_before_any_move()
    {
        using var root = new TempRoot();
        var source = root.WriteFile(@"source\d.tmp", "d");
        var quarantine = root.Combine("quarantine");
        Directory.CreateDirectory(quarantine);

        // 用一个"移动前就崩"的方式观察：预取消 → 账本必须已存在且已收尾为 Pending=false（空账）
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var store = CreateStore(new WindowsFileSystem(), new MappedVolumeProbe());
        var result = store.StoreCore(quarantine, 7, new[] { Planned(source, 1) }, null, cts.Token);

        var mapPath = Path.Combine(result.BatchDirectory, QuarantineMapStore.FileName);
        Assert.True(File.Exists(mapPath), "账本必须在搬运之前就落盘");
        var map = QuarantineMapStore.TryRead(result.BatchDirectory);
        Assert.NotNull(map);
        Assert.False(map!.Pending);
        Assert.Empty(map.Entries);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void Should_report_quarantine_info_with_expiry()
    {
        using var root = new TempRoot();
        var source = root.WriteFile(@"source\e.tmp", new string('e', 100));
        var quarantine = root.Combine("quarantine");
        Directory.CreateDirectory(quarantine);

        var store = CreateStore(new WindowsFileSystem(), new MappedVolumeProbe());
        store.StoreCore(quarantine, 7, new[] { Planned(source, 100) }, null, CancellationToken.None);

        // 同一天：未到期
        var fresh = store.Inspect(quarantine);
        Assert.Single(fresh.Batches);
        Assert.Equal(100, fresh.TotalBytes);
        Assert.Equal(0, fresh.ExpiredBatchCount);

        // 8 天后：到期
        var later = CreateStore(new WindowsFileSystem(), new MappedVolumeProbe(), Now.AddDays(8));
        var expired = later.Inspect(quarantine);
        Assert.Equal(1, expired.ExpiredBatchCount);
    }

    [Fact]
    public void Should_have_empty_info_for_missing_quarantine_root()
    {
        using var root = new TempRoot();
        var store = CreateStore(new WindowsFileSystem(), new MappedVolumeProbe());

        var info = store.Inspect(root.Combine("never-created"));

        Assert.Empty(info.Batches);
        Assert.Equal(0, info.TotalBytes);
    }
}
