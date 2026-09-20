using SpaceMaid.Core.Models;
using SpaceMaid.Core.Platform;
using SpaceMaid.Core.Quarantine;
using SpaceMaid.Core.Tests.Abstractions;

namespace SpaceMaid.Core.Tests.Quarantine;

/// <summary>
/// 断电/强退后的账本自检（需求 3.2-6、3.4-4）：账本永远要能算出"文件到底在哪"。
/// </summary>
public class QuarantineCrashRecoveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 14, 30, 12, TimeSpan.FromHours(8));

    private static QuarantineStore CreateStore() =>
        new(new WindowsFileSystem(), new MappedVolumeProbe(), new QuarantineFakeClock(Now));

    /// <summary>手工造一个"搬到一半就断电"的批次目录。</summary>
    private static string BuildCrashBatch(
        TempRoot root,
        string batchId,
        string originalPath,
        bool payloadExists,
        bool sourceExists)
    {
        var batchDirectory = Path.Combine(root.Combine("quarantine"), batchId);
        var payloadRelative = Path.Combine("payload", "0001_x.tmp");
        Directory.CreateDirectory(Path.Combine(batchDirectory, "payload"));

        if (payloadExists)
        {
            File.WriteAllText(Path.Combine(batchDirectory, payloadRelative), "payload-content");
        }

        if (sourceExists)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(originalPath)!);
            File.WriteAllText(originalPath, "payload-content");
        }

        var map = new QuarantineMap
        {
            BatchId = batchId,
            CreatedAt = Now,
            ExpiresAt = Now.AddDays(7),
            QuarantineRoot = root.Combine("quarantine"),
            SameVolumeAsSource = true,
            Pending = true,
            Entries = new[]
            {
                new QuarantineMapEntry
                {
                    ItemId = "test.item",
                    Category = CleanCategory.L1OneClick,
                    OriginalPath = originalPath,
                    SourceVolume = @"C:\",
                    SizeBytes = 15,
                    LastWrite = Now.AddDays(-1),
                    StoredAs = payloadRelative,
                    Status = MapEntryStatus.Pending
                }
            }
        };

        Assert.True(QuarantineMapStore.TryWrite(batchDirectory, map, out var error), error);
        return batchDirectory;
    }

    [Fact]
    public void Should_recover_pending_entry_when_payload_exists_and_source_gone()
    {
        using var root = new TempRoot();
        var batchDirectory = BuildCrashBatch(root, "20260920-143012-000", root.Combine(@"source\gone.tmp"), payloadExists: true, sourceExists: false);

        var report = CreateStore().Recover(root.Combine("quarantine"));

        Assert.Equal(1, report.StoredRecovered);
        Assert.Equal(0, report.MarkedUnknown);
        var map = QuarantineMapStore.TryRead(batchDirectory);
        Assert.NotNull(map);
        Assert.False(map!.Pending);
        Assert.Equal(MapEntryStatus.Stored, map.Entries.Single().Status);
    }

    [Fact]
    public void Should_drop_pending_entry_and_remove_payload_when_source_still_exists()
    {
        using var root = new TempRoot();
        var sourcePath = root.Combine(@"source\still-here.tmp");
        var batchDirectory = BuildCrashBatch(root, "20260920-143012-001", sourcePath, payloadExists: true, sourceExists: true);

        var report = CreateStore().Recover(root.Combine("quarantine"));

        Assert.Equal(1, report.PendingCleared);
        Assert.Equal(0, report.StoredRecovered);
        Assert.True(File.Exists(sourcePath), "源文件必须保留");
        Assert.False(File.Exists(Path.Combine(batchDirectory, "payload", "0001_x.tmp")), "多余副本应被清理");

        var map = QuarantineMapStore.TryRead(batchDirectory);
        Assert.NotNull(map);
        Assert.Empty(map!.Entries);
    }

    [Fact]
    public void Should_mark_unknown_when_neither_source_nor_payload_exists()
    {
        using var root = new TempRoot();
        var batchDirectory = BuildCrashBatch(root, "20260920-143012-002", root.Combine(@"source\lost.tmp"), payloadExists: false, sourceExists: false);

        var report = CreateStore().Recover(root.Combine("quarantine"));

        Assert.Equal(1, report.MarkedUnknown);
        Assert.Contains(report.Notes, n => n.Contains("异常"));
        var map = QuarantineMapStore.TryRead(batchDirectory);
        Assert.NotNull(map);
        Assert.Equal(MapEntryStatus.Unknown, map!.Entries.Single().Status);
    }

    [Fact]
    public void Should_leave_completed_batches_untouched()
    {
        using var root = new TempRoot();
        var source = root.WriteFile(@"source\a.tmp", "content");
        var quarantine = root.Combine("quarantine");
        Directory.CreateDirectory(quarantine);

        var store = CreateStore();
        store.StoreCore(quarantine, 7, new[] { new PlannedFile("test.item", CleanCategory.L1OneClick, source, 7, Now.AddDays(-1)) }, null, CancellationToken.None);

        var report = store.Recover(quarantine);

        Assert.Equal(0, report.StoredRecovered);
        Assert.Equal(0, report.PendingCleared);
        Assert.Equal(0, report.MarkedUnknown);
        Assert.Single(store.Inspect(quarantine).Batches);
    }
}
