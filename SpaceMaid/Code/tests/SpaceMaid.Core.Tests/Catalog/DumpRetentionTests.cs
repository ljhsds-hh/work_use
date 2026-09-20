using SpaceMaid.Core.Catalog;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Platform;
using SpaceMaid.Core.Scanning;
using SpaceMaid.Core.Tests.Abstractions;
using SpaceMaid.Core.Tests.Execution;
using SpaceMaid.Core.Tests.Quarantine;
using SpaceMaid.Core.Tests.Scanning;

namespace SpaceMaid.Core.Tests.Catalog;

/// <summary>
/// "保留最近一次蓝屏转储"这条需求的回归测试（对抗式评审 F-6）。
///
/// 两个曾经的缺陷：
/// 1. `LiveKernelReports` 与 MEMORY.DMP / Minidump 混在**同一个条目**里做"保留最新一份"，
///    于是最新的实时内核报告会把真正该保留的 `MEMORY.DMP` 挤掉（违反需求 2.2）；
/// 2. `ScanningRules` 算出的"保留项"在扫描 → 计划 → 清单这条链路上被丢弃，
///    用户永远看不到清单里那行"保留：&lt;路径&gt;"。
/// </summary>
public class DumpRetentionTests
{
    private static ScanFile Dump(string path, DateTimeOffset lastWrite, long size = 1024) =>
        new(path, size, lastWrite, CleanActionKind.Quarantine);

    [Fact]
    public void Dump_item_should_not_mix_in_live_kernel_reports()
    {
        var dumps = CleanItemCatalog.ById("l1.dumps");
        var liveReports = CleanItemCatalog.ById("l1.live-kernel-reports");

        Assert.NotNull(dumps);
        Assert.NotNull(liveReports);

        Assert.True(dumps!.KeepsNewest);
        Assert.DoesNotContain(dumps.Targets, rule => rule.Path.Contains("LiveKernelReports", StringComparison.OrdinalIgnoreCase));

        // 实时内核报告单独一项、且不需要"保留最新一份"
        Assert.False(liveReports!.KeepsNewest);
        Assert.Contains(liveReports.Targets, rule => rule.Path.Contains("LiveKernelReports", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Scan_should_expose_the_kept_dump()
    {
        using var root = new TempRoot();
        var old = root.WriteFile(@"Minidump\old.dmp", "old");
        var newest = root.WriteFile(@"MEMORY.DMP", "newest");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-10));
        File.SetLastWriteTimeUtc(newest, DateTime.UtcNow.AddHours(-2));

        var item = new CleanItemDefinition
        {
            Id = "test.dumps",
            Category = CleanCategory.L1OneClick,
            DisplayName = "转储",
            Risk = ItemRisk.Safe,
            ActionKind = CleanActionKind.Quarantine,
            Targets = new[]
            {
                TargetRule.File(Path.Combine(root.Path, "MEMORY.DMP")),
                TargetRule.Glob(Path.Combine(root.Path, "Minidump"), "*.dmp")
            },
            AutoRegenerated = true,
            DefaultChecked = true,
            KeepsNewest = true
        };

        var engine = new ScanEngine(new WindowsFileSystem(), new WindowsEnvironmentProbe(), new ScanFakeVolumeProbe(), new FakeClock(DateTimeOffset.Now));
        var report = await engine.ScanAsync(new ScanRequest(new[] { item }, false), null, CancellationToken.None);

        var entry = report.Entries.Single();
        var kept = Assert.Single(entry.Kept);
        Assert.Equal(newest, kept.Path);
        Assert.Single(entry.Files);
        Assert.Equal(old, entry.Files[0].Path);
    }

    [Fact]
    public void BuildPlan_should_carry_kept_files_into_the_manifest_data()
    {
        var services = CoreServices.Create(
            new SpaceMaid.Core.Settings.AppSettings
            {
                QuarantineBasePath = @"D:\Quarantine",
                LogDirectory = @"D:\logs\SpaceMaid",
                ReportDirectory = @"D:\logs\SpaceMaid\清单"
            },
            fileSystem: new ScriptedFileSystem(),
            clock: new FakeClock(DateTimeOffset.Now),
            volumes: new MappedVolumeProbe(),
            environment: new ExecutionFakeEnvironment(),
            commandRunner: new FakeCommandRunner(),
            log: SpaceMaid.Core.Logging.SilentLogSink.Instance);

        var definition = new CleanItemDefinition
        {
            Id = "l1.dumps",
            Category = CleanCategory.L1OneClick,
            DisplayName = "内核与蓝屏转储",
            Risk = ItemRisk.Safe,
            ActionKind = CleanActionKind.Quarantine,
            Targets = new[] { TargetRule.Glob(@"%SystemRoot%\Minidump", "*.dmp") },
            AutoRegenerated = true,
            DefaultChecked = true,
            KeepsNewest = true
        };

        var keptFile = Dump(@"C:\Windows\MEMORY.DMP", DateTimeOffset.Now.AddHours(-1), 2048);
        var entry = new ScanEntry(
            definition,
            1024,
            1,
            0,
            new[] { Dump(@"C:\Windows\Minidump\old.dmp", DateTimeOffset.Now.AddDays(-5)) },
            true,
            null)
        {
            Kept = new[] { keptFile }
        };

        var scan = new ScanReport(
            new[] { entry },
            new VolumeSnapshot(@"C:\", 500L * 1024 * 1024 * 1024, 20L * 1024 * 1024 * 1024),
            DateTimeOffset.Now);

        var plan = services.BuildPlan(scan);
        var planItem = Assert.Single(plan.Items);

        var keptInPlan = Assert.Single(planItem.Kept);
        Assert.Equal(keptFile.Path, keptInPlan.Path);
    }
}
