using SpaceMaid.Core.Models;
using SpaceMaid.Core.Platform;
using SpaceMaid.Core.Scanning;
using SpaceMaid.Core.Scanning.Selectors;
using SpaceMaid.Core.Tests.Abstractions;

namespace SpaceMaid.Core.Tests.Scanning;

/// <summary>
/// 对抗式评审 F-12 的回归：扫描必须有界限。
/// ① 单项枚举有上限（畸形目录树不能让扫描假死），触发时要**如实标注"结果可能不完整"**；
/// ② 重复文件判定有体积上限（对几个 GB 的镜像做全量哈希会把界面挂住）。
/// </summary>
public class ScanBoundaryTests
{
    [Fact]
    public async Task Scan_should_stop_at_enumeration_cap_and_say_so()
    {
        using var root = new TempRoot();
        for (var i = 1; i <= 10; i++)
        {
            root.WriteFile($@"cache\file{i:D2}.tmp", "x");
        }

        var item = new CleanItemDefinition
        {
            Id = "test.cap",
            Category = CleanCategory.L1OneClick,
            DisplayName = "枚举上限测试",
            Risk = ItemRisk.Safe,
            ActionKind = CleanActionKind.Quarantine,
            Targets = new[] { TargetRule.Tree(root.Combine("cache")) },
            AutoRegenerated = true,
            DefaultChecked = true
        };

        var engine = new ScanEngine(
            new WindowsFileSystem(),
            new WindowsEnvironmentProbe(),
            new ScanFakeVolumeProbe(),
            new FakeClock(DateTimeOffset.Now),
            maxFilesPerItem: 4);

        var report = await engine.ScanAsync(new ScanRequest(new[] { item }, false), null, CancellationToken.None);
        var entry = report.Entries.Single();

        Assert.Equal(4, entry.Files.Count);
        Assert.True(entry.Available, "达到枚举上限不等于条目不可用");
        Assert.NotNull(entry.UnavailableReason);
        Assert.Contains("单项上限", entry.UnavailableReason);
        Assert.Contains("可能不完整", entry.UnavailableReason);
    }

    [Fact]
    public async Task Scan_should_stop_at_time_budget_and_say_so()
    {
        using var root = new TempRoot();
        for (var i = 1; i <= 5; i++)
        {
            root.WriteFile($@"cache\file{i}.tmp", "x");
        }

        var item = new CleanItemDefinition
        {
            Id = "test.budget",
            Category = CleanCategory.L1OneClick,
            DisplayName = "时间预算测试",
            Risk = ItemRisk.Safe,
            ActionKind = CleanActionKind.Quarantine,
            Targets = new[] { TargetRule.Tree(root.Combine("cache")) },
            AutoRegenerated = true,
            DefaultChecked = true
        };

        // 注入零预算：第一轮检查就触发"超出上限"，用来钉住"时间预算真的生效"这件事
        var engine = new ScanEngine(
            new WindowsFileSystem(),
            new WindowsEnvironmentProbe(),
            new ScanFakeVolumeProbe(),
            new FakeClock(DateTimeOffset.Now),
            itemTimeBudget: TimeSpan.Zero);

        var report = await engine.ScanAsync(new ScanRequest(new[] { item }, false), null, CancellationToken.None);
        var entry = report.Entries.Single();

        Assert.True(entry.Available, "超出时间预算不等于条目不可用");
        Assert.NotNull(entry.UnavailableReason);
        Assert.Contains("可能不完整", entry.UnavailableReason);
    }

    [Fact]
    public void Default_time_budget_should_be_seconds_not_minutes()
    {
        // 实测：内容寻址缓存（pnpm store 之类）单是枚举就能超过 20 秒，
        // 时间预算必须小到"任何单项都不会把整次扫描拖住"
        Assert.True(ScanEngine.DefaultItemTimeBudget <= TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Default_cap_should_bound_real_world_scan_time()
    {
        // 实测：20 万的上限在真机上会让单项扫描卡几分钟（开发缓存动辄十几万文件），
        // 因此把默认值压到 5 万——这是"覆盖绝大多数机器"与"别让用户等到怀疑人生"的折中。
        Assert.Equal(20_000, ScanEngine.MaxFilesPerItem);
    }

    [Fact]
    public void Duplicate_selector_should_skip_files_above_the_size_cap()
    {
        var fileSystem = new SpaceMaid.Core.Tests.Execution.ScriptedFileSystem();
        var huge = DuplicateFileSelector.MaxFileSizeBytes + 1;

        var candidates = new[]
        {
            new ScanFile(@"C:\data\image-a.iso", huge, DateTimeOffset.Now.AddDays(-2), CleanActionKind.Quarantine),
            new ScanFile(@"C:\data\image-b.iso", huge, DateTimeOffset.Now.AddDays(-1), CleanActionKind.Quarantine)
        };

        var selector = new DuplicateFileSelector();
        var selected = selector.Select(
            new CleanItemDefinition
            {
                Id = DuplicateFileSelector.ItemId,
                Category = CleanCategory.L3Cautious,
                DisplayName = "重复文件",
                Risk = ItemRisk.Caution,
                ActionKind = CleanActionKind.Quarantine,
                Targets = new[] { TargetRule.Tree(@"C:\data") }
            },
            candidates,
            fileSystem,
            SpaceMaid.Core.Logging.SilentLogSink.Instance);

        Assert.Empty(selected);
        Assert.Equal(512L * 1024 * 1024, DuplicateFileSelector.MaxFileSizeBytes);
    }
}
