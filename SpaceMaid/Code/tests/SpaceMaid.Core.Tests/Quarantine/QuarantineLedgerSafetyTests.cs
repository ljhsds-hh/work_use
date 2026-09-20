using SpaceMaid.Core.Models;
using SpaceMaid.Core.Platform;
using SpaceMaid.Core.Quarantine;
using SpaceMaid.Core.Tests.Abstractions;

namespace SpaceMaid.Core.Tests.Quarantine;

/// <summary>
/// 隔离区账本（map.json）**是不可信输入**——它位于用户可写目录、还被刻意设计成可读可改。
///
/// 对抗式评审指出：如果直接拿账本里的 <c>StoredAs</c> 拼路径去删除/移动，就等于把
/// "任意文件删除"的能力交给任何能写这个文件的进程（伪造一条 <c>storedAs=..\..\..\Windows\System32\...</c>
/// 的账目，用户下次以管理员启动时即被以管理员权限删除）。本文件就是这条防线的回归测试。
/// </summary>
public class QuarantineLedgerSafetyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 14, 30, 12, TimeSpan.FromHours(8));

    private static QuarantineService CreateService(out QuarantineStore store, DateTimeOffset? now = null)
    {
        var fileSystem = new WindowsFileSystem();
        var volumes = new MappedVolumeProbe();
        var clock = new QuarantineFakeClock(now ?? Now);
        store = new QuarantineStore(fileSystem, volumes, clock);
        return new QuarantineService(store, fileSystem, volumes, new WindowsEnvironmentProbe(), clock);
    }

    private static QuarantineMapEntry Entry(string storedAs, string originalPath) => new()
    {
        ItemId = "l1.user-temp",
        Category = CleanCategory.L1OneClick,
        OriginalPath = originalPath,
        SourceVolume = @"C:\",
        SizeBytes = 1,
        LastWrite = Now.AddDays(-1),
        StoredAs = storedAs,
        Status = MapEntryStatus.Stored
    };

    [Theory]
    [InlineData(@"..\..\..\Windows\System32\drivers\etc\hosts")]
    [InlineData(@"payload\..\..\escape.txt")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"\absolute.txt")]
    [InlineData("../payload/x.txt")]
    public void Should_reject_unsafe_stored_paths(string storedAs)
    {
        Assert.False(QuarantineMapStore.IsSafeStoredAs(storedAs));
    }

    [Theory]
    [InlineData("payload/0001_a.tmp")]
    [InlineData(@"payload\0001_a.tmp")]
    public void Should_accept_normal_stored_paths(string storedAs)
    {
        Assert.True(QuarantineMapStore.IsSafeStoredAs(storedAs));
    }

    [Fact]
    public void Should_treat_ledger_with_escaping_entry_as_unreadable()
    {
        using var root = new TempRoot();
        var quarantine = root.Combine("quarantine");
        var batchDirectory = Path.Combine(quarantine, "20260920-143012-000");
        Directory.CreateDirectory(Path.Combine(batchDirectory, "payload"));

        var map = new QuarantineMap
        {
            BatchId = "20260920-143012-000",
            CreatedAt = Now,
            ExpiresAt = Now.AddDays(-1),
            QuarantineRoot = quarantine,
            Pending = false,
            Entries = new[] { Entry(@"..\..\..\victim\important.dat", root.Combine(@"victim\important.dat")) }
        };
        Assert.True(QuarantineMapStore.TryWrite(batchDirectory, map, out var error), error);

        Assert.Null(QuarantineMapStore.TryRead(batchDirectory));
    }

    [Fact]
    public void Escaping_ledger_must_not_delete_anything_outside_the_batch()
    {
        using var root = new TempRoot();
        var quarantine = root.Combine("quarantine");
        var victim = root.WriteFile(@"victim\important.dat", "do-not-delete");

        var batchDirectory = Path.Combine(quarantine, "20260920-143012-000");
        Directory.CreateDirectory(Path.Combine(batchDirectory, "payload"));

        // 越界指向 quarantine\victim\important.dat（用相对路径爬出批次目录）
        var escaping = @"..\..\victim\important.dat";
        var map = new QuarantineMap
        {
            BatchId = "20260920-143012-000",
            CreatedAt = Now,
            ExpiresAt = Now.AddDays(-1),           // 已到期，会被惰性释放
            QuarantineRoot = quarantine,
            Pending = false,
            Entries = new[] { Entry(escaping, victim) }
        };
        Assert.True(QuarantineMapStore.TryWrite(batchDirectory, map, out var error), error);

        var service = CreateService(out var store);

        // 惰性释放与立即清空都不允许碰到那个文件
        service.ReleaseExpired(quarantine);
        service.ClearAll(quarantine);

        Assert.True(File.Exists(victim), "伪造的越界账目不得导致任何删除");
        Assert.Equal(0, store.Inspect(quarantine).TotalBytes);
    }

    [Fact]
    public void Should_refuse_restoring_into_denylisted_path()
    {
        using var root = new TempRoot();
        var source = root.WriteFile(@"source\a.tmp", "content");
        var quarantine = root.Combine("quarantine");
        Directory.CreateDirectory(quarantine);

        var service = CreateService(out var store);
        var stored = service.Store(quarantine, 7, new[]
        {
            new PlannedFile("l1.user-temp", CleanCategory.L1OneClick, source, 7, Now.AddDays(-1))
        });

        // 篡改账本：把还原目标改成系统目录（模拟"用户可改账本"这一前提）
        var batchDirectory = stored.BatchDirectory;
        var map = QuarantineMapStore.TryRead(batchDirectory);
        Assert.NotNull(map);

        var systemTarget = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "spacemaid-should-never-write-here.dat");

        var tampered = map! with { Entries = map.Entries.Select(e => e with { OriginalPath = systemTarget }).ToArray() };
        Assert.True(QuarantineMapStore.TryWrite(batchDirectory, tampered, out var writeError), writeError);

        var result = service.Restore(quarantine, stored.BatchId);

        Assert.Equal(0, result.RestoredCount);
        Assert.Contains(result.Failures, f => f.Reason.Contains("禁止清单"));
        Assert.False(File.Exists(systemTarget), "绝不能把文件还原到系统目录");

        // 被拒绝的批次不该被清掉：隔离副本必须还在（账目会标成 Unknown，以便复核报告把它当异常列出）
        var payloadPath = Path.Combine(batchDirectory, stored.StoredEntries.Single().StoredAs);
        Assert.True(File.Exists(payloadPath), "被拒绝的还原不得删除隔离副本");
    }

    [Fact]
    public void Should_refuse_restoring_when_stored_path_escapes()
    {
        using var root = new TempRoot();
        var quarantine = root.Combine("quarantine");
        var batchDirectory = Path.Combine(quarantine, "20260920-143012-000");
        Directory.CreateDirectory(Path.Combine(batchDirectory, "payload"));

        var target = root.Combine(@"victim\out.dat");
        var map = new QuarantineMap
        {
            BatchId = "20260920-143012-000",
            CreatedAt = Now,
            ExpiresAt = Now.AddDays(7),
            QuarantineRoot = quarantine,
            Pending = false,
            Entries = new[] { Entry(@"..\..\victim\out.dat", target) }
        };
        Assert.True(QuarantineMapStore.TryWrite(batchDirectory, map, out var error), error);

        var service = CreateService(out _);
        var result = service.Restore(quarantine, "20260920-143012-000");

        Assert.Equal(0, result.RestoredCount);
        Assert.False(File.Exists(target));
    }
}
