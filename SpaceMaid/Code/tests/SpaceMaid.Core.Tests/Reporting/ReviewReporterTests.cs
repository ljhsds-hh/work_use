using SpaceMaid.Core.Models;
using SpaceMaid.Core.Platform;
using SpaceMaid.Core.Reporting;
using SpaceMaid.Core.Tests.Abstractions;

namespace SpaceMaid.Core.Tests.Reporting;

public class ReviewReporterTests
{
    private static QuarantineMap MapWith(params string[] storedPaths) => new()
    {
        BatchId = "20260920-143000-000",
        CreatedAt = PlanFixtures.Now,
        ExpiresAt = PlanFixtures.Now.AddDays(7),
        QuarantineRoot = @"D:\Quarantine",
        SameVolumeAsSource = false,
        Pending = false,
        Entries = storedPaths.Select(path => new QuarantineMapEntry
        {
            ItemId = "l1.user-temp",
            Category = CleanCategory.L1OneClick,
            OriginalPath = path,
            SourceVolume = @"C:\",
            SizeBytes = 10,
            LastWrite = PlanFixtures.Now.AddDays(-1),
            StoredAs = Path.Combine("payload", "0001_x.tmp"),
            Status = MapEntryStatus.Stored
        }).ToArray()
    };

    [Fact]
    public void Should_report_cleaned_skipped_and_unknown()
    {
        using var root = new TempRoot();
        var cleanedPath = root.WriteFile(@"watch\cleaned.tmp", "x");
        var skippedPath = root.WriteFile(@"watch\skipped.tmp", "y");
        var vanishedPath = root.Combine(@"watch\vanished.tmp");   // 不存在，且账本里也没有

        // 模拟"已清理"：源文件被搬走（删除），账本里有记录
        File.Delete(cleanedPath);

        var index = new ManifestRowIndex(root.Combine("reports"), new[]
        {
            new ManifestRow("L1 一键直清", "l1.user-temp", "用户临时目录", cleanedPath, 10, PlanFixtures.Now, "移入隔离区", true, string.Empty),
            new ManifestRow("L1 一键直清", "l1.user-temp", "用户临时目录", skippedPath, 10, PlanFixtures.Now, "移入隔离区", true, string.Empty),
            new ManifestRow("L1 一键直清", "l1.user-temp", "用户临时目录", vanishedPath, 10, PlanFixtures.Now, "移入隔离区", true, string.Empty)
        });

        var reporter = new ReviewReporter(new WindowsFileSystem(), new ReportingFakeClock(PlanFixtures.Now));
        var report = reporter.Review(index, new[] { MapWith(cleanedPath) }, root.Combine("reports"), releasedBytes: 10, sameVolume: false);

        Assert.Equal(1, report.CleanedCount);
        Assert.Equal(1, report.SkippedCount);
        Assert.Equal(1, report.UnknownCount);
        Assert.Contains(report.Items, i => i.OriginalPath == vanishedPath && i.Status == ReviewStatus.Unknown);

        var markdown = File.ReadAllText(report.MarkdownPath);
        Assert.Contains("## 异常（必须逐条确认）（1）", markdown);
        Assert.Contains(vanishedPath, markdown);
        Assert.Contains("已清理：1", markdown);
        Assert.Contains("未清理：1", markdown);
        Assert.Contains("异常：1", markdown);
    }

    [Fact]
    public void Should_flag_unknown_when_file_exists_in_both_places()
    {
        using var root = new TempRoot();
        var bothPath = root.WriteFile(@"watch\both.tmp", "z");

        var index = new ManifestRowIndex(root.Combine("reports"), new[]
        {
            new ManifestRow("L1 一键直清", "l1.user-temp", "用户临时目录", bothPath, 10, PlanFixtures.Now, "移入隔离区", true, string.Empty)
        });

        var reporter = new ReviewReporter(new WindowsFileSystem(), new ReportingFakeClock(PlanFixtures.Now));
        var report = reporter.Review(index, new[] { MapWith(bothPath) }, root.Combine("reports"), 0, sameVolume: true);

        Assert.Equal(1, report.UnknownCount);
        Assert.Contains("同时存在", report.Items.Single().Detail);
    }

    [Fact]
    public void Should_use_same_volume_wording_in_report()
    {
        using var root = new TempRoot();
        var index = new ManifestRowIndex(root.Combine("reports"), Array.Empty<ManifestRow>());

        var reporter = new ReviewReporter(new WindowsFileSystem(), new ReportingFakeClock(PlanFixtures.Now));
        var report = reporter.Review(index, Array.Empty<QuarantineMap>(), root.Combine("reports"), releasedBytes: 1024L * 1024 * 1024, sameVolume: true);

        var markdown = File.ReadAllText(report.MarkdownPath);
        Assert.Contains("已移入隔离区", markdown);
        Assert.DoesNotContain("实际效果：已释放", markdown);
    }

    [Fact]
    public void Should_write_report_into_manifest_directory_when_output_root_empty()
    {
        using var root = new TempRoot();
        var manifestDirectory = root.Combine("reports");
        var index = new ManifestRowIndex(manifestDirectory, Array.Empty<ManifestRow>());

        var reporter = new ReviewReporter(new WindowsFileSystem(), new ReportingFakeClock(PlanFixtures.Now));
        var report = reporter.Review(index, Array.Empty<QuarantineMap>(), string.Empty, 0, sameVolume: true);

        Assert.Equal(Path.Combine(manifestDirectory, "复核报告.md"), report.MarkdownPath);
        Assert.True(File.Exists(report.MarkdownPath));
    }
}
