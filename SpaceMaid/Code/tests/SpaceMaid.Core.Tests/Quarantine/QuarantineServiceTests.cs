using SpaceMaid.Core.Models;
using SpaceMaid.Core.Platform;
using SpaceMaid.Core.Quarantine;
using SpaceMaid.Core.Tests.Abstractions;

namespace SpaceMaid.Core.Tests.Quarantine;

public class QuarantineServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 14, 30, 12, TimeSpan.FromHours(8));

    private static (QuarantineService Service, QuarantineStore Store, MappedVolumeProbe Volumes) CreateService(DateTimeOffset? now = null)
    {
        var volumes = new MappedVolumeProbe();
        var clock = new QuarantineFakeClock(now ?? Now);
        var fileSystem = new WindowsFileSystem();
        var store = new QuarantineStore(fileSystem, volumes, clock);
        return (new QuarantineService(store, fileSystem, volumes, new SpaceMaid.Core.Platform.WindowsEnvironmentProbe(), clock), store, volumes);
    }

    private static PlannedFile Planned(string path, long size) =>
        new("l1.user-temp", CleanCategory.L1OneClick, path, size, Now.AddDays(-1));

    [Fact]
    public void Should_restore_file_to_original_path()
    {
        using var root = new TempRoot();
        var source = root.WriteFile(@"source\a.tmp", "hello-隔离");
        var quarantine = root.Combine("quarantine");
        Directory.CreateDirectory(quarantine);

        var (service, _, _) = CreateService();
        var stored = service.Store(quarantine, 7, new[] { Planned(source, 11) });
        Assert.False(File.Exists(source));

        var restore = service.Restore(quarantine, stored.BatchId);

        Assert.Equal(1, restore.RestoredCount);
        Assert.Empty(restore.Conflicts);
        Assert.Empty(restore.Failures);
        Assert.True(File.Exists(source));
        Assert.Equal("hello-隔离", File.ReadAllText(source));
    }

    [Fact]
    public void Should_not_overwrite_existing_file_on_restore()
    {
        using var root = new TempRoot();
        var source = root.WriteFile(@"source\b.tmp", "original");
        var quarantine = root.Combine("quarantine");
        Directory.CreateDirectory(quarantine);

        var (service, _, _) = CreateService();
        var stored = service.Store(quarantine, 7, new[] { Planned(source, 8) });

        // 模拟"用户在原位置又放了一个同名文件"
        File.WriteAllText(source, "user-new-content");

        var restore = service.Restore(quarantine, stored.BatchId);

        Assert.Equal(0, restore.RestoredCount);
        var conflict = Assert.Single(restore.Conflicts);
        Assert.Equal(source, conflict.OriginalPath);
        Assert.Equal("user-new-content", File.ReadAllText(source));   // 用户文件绝不被覆盖
    }

    [Fact]
    public void Should_release_only_expired_batches()
    {
        using var root = new TempRoot();
        var source = root.WriteFile(@"source\c.tmp", new string('c', 256));
        var quarantine = root.Combine("quarantine");
        Directory.CreateDirectory(quarantine);

        var (service, _, _) = CreateService();
        var stored = service.Store(quarantine, 7, new[] { Planned(source, 256) });

        // 第 3 天：还没到期
        var (earlyService, _, _) = CreateService(Now.AddDays(3));
        var early = earlyService.ReleaseExpired(quarantine);
        Assert.Equal(0, early.ReleasedBatches);
        Assert.Single(earlyService.Inspect(quarantine).Batches);

        // 第 8 天：到期释放
        var (lateService, _, _) = CreateService(Now.AddDays(8));
        var late = lateService.ReleaseExpired(quarantine);

        Assert.Equal(1, late.ReleasedBatches);
        Assert.Equal(1, late.ReleasedFiles);
        Assert.Equal(256, late.ReleasedBytes);
        Assert.Empty(lateService.Inspect(quarantine).Batches);
        Assert.False(Directory.Exists(stored.BatchDirectory));
    }

    [Fact]
    public void Should_be_lazy_not_time_based()
    {
        using var root = new TempRoot();
        var source = root.WriteFile(@"source\d.tmp", "d");
        var quarantine = root.Combine("quarantine");
        Directory.CreateDirectory(quarantine);

        var (service, store, _) = CreateService();
        var stored = service.Store(quarantine, 7, new[] { Planned(source, 1) });

        // 时间过去 30 天，但没有人调用 ReleaseExpired —— 隔离文件必须还在（本工具无常驻进程）
        var (laterService, _, _) = CreateService(Now.AddDays(30));
        var info = laterService.Inspect(quarantine);

        Assert.Equal(1, info.ExpiredBatchCount);
        Assert.Single(info.Batches);
        var payloadPath = Path.Combine(stored.BatchDirectory, stored.StoredEntries.Single().StoredAs);
        Assert.True(File.Exists(payloadPath), "没有调用惰性释放时，文件不应被自动删除");

        // 一旦调用，才真正释放
        laterService.ReleaseExpired(quarantine);
        Assert.False(File.Exists(payloadPath));
        Assert.Empty(store.Inspect(quarantine).Batches);
    }

    [Fact]
    public void Should_clear_everything_on_demand()
    {
        using var root = new TempRoot();
        var quarantine = root.Combine("quarantine");
        Directory.CreateDirectory(quarantine);

        var (service, _, _) = CreateService();
        service.Store(quarantine, 7, new[] { Planned(root.WriteFile(@"source\1.tmp", "1"), 1) });
        service.Store(quarantine, 7, new[] { Planned(root.WriteFile(@"source\2.tmp", "2"), 1) });
        Assert.Equal(2, service.Inspect(quarantine).Batches.Count);

        var result = service.ClearAll(quarantine);

        Assert.Equal(2, result.ReleasedBatches);
        Assert.Equal(2, result.ReleasedFiles);
        Assert.Empty(service.Inspect(quarantine).Batches);
    }

    [Fact]
    public void Should_remove_orphan_payload_files_with_the_batch()
    {
        using var root = new TempRoot();
        var source = root.WriteFile(@"source\e.tmp", "e");
        var quarantine = root.Combine("quarantine");
        Directory.CreateDirectory(quarantine);

        var (service, _, _) = CreateService();
        var stored = service.Store(quarantine, 7, new[] { Planned(source, 1) });

        // 手工塞一个账本里没有的孤儿文件
        var orphan = Path.Combine(stored.BatchDirectory, "payload", "9999_orphan.tmp");
        File.WriteAllText(orphan, "orphan");

        var (laterService, _, _) = CreateService(Now.AddDays(8));
        laterService.ReleaseExpired(quarantine);

        Assert.False(File.Exists(orphan), "孤儿文件应随批次目录一起释放");
    }

    [Fact]
    public void Should_report_restore_failure_for_missing_payload()
    {
        using var root = new TempRoot();
        var source = root.WriteFile(@"source\f.tmp", "f");
        var quarantine = root.Combine("quarantine");
        Directory.CreateDirectory(quarantine);

        var (service, _, _) = CreateService();
        var stored = service.Store(quarantine, 7, new[] { Planned(source, 1) });

        // 手工删掉隔离副本，模拟"隔离区被外部清理"
        File.Delete(Path.Combine(stored.BatchDirectory, stored.StoredEntries.Single().StoredAs));

        var restore = service.Restore(quarantine, stored.BatchId);

        Assert.Equal(0, restore.RestoredCount);
        var failure = Assert.Single(restore.Failures);
        Assert.Contains("副本已不存在", failure.Reason);
    }
}
