using SpaceMaid.Core.Models;
using SpaceMaid.Core.Reporting;
using SpaceMaid.Core.Settings;
using SpaceMaid.Core.Tests.Abstractions;

namespace SpaceMaid.Core.Tests.Reporting;

/// <summary>
/// 清单读回（复核的数据来源）。重点：写出去的清单必须能被**另一个进程**读回来。
/// </summary>
public class ManifestReaderTests
{
    private static CleanPlan BuildPlan()
    {
        var l1 = PlanFixtures.Definition("l1.user-temp", CleanCategory.L1OneClick);
        var l3 = PlanFixtures.Definition(
            "l3.chat-cache",
            CleanCategory.L3Cautious,
            risk: ItemRisk.Dangerous,
            actionNote: "可能包含你要保留的聊天文件",
            restoreHint: "恢复：从隔离区还原");

        return PlanFixtures.Plan(
            new[]
            {
                PlanFixtures.PlanItem(l1, true,
                    PlanFixtures.File(@"C:\Temp\a.tmp", 10),
                    PlanFixtures.File(@"C:\Temp\带逗号,的名字.tmp", 20)),
                PlanFixtures.PlanItem(l3, false, PlanFixtures.File(@"C:\Chat\c.dat", 30))
            },
            Array.Empty<ScanEntry>());
    }

    [Fact]
    public void Should_roundtrip_written_manifest()
    {
        using var root = new TempRoot();
        var plan = BuildPlan();
        var paths = new ManifestWriter().Write(plan, root.Combine("reports"));

        var index = ManifestReader.TryRead(paths.Directory, out var error);

        Assert.NotNull(index);
        Assert.Equal(string.Empty, error);
        Assert.Equal(plan.PlannedFileCount, index!.Rows.Count);
        Assert.Contains(index.Rows, r => r.OriginalPath == @"C:\Temp\带逗号,的名字.tmp");
        Assert.Contains(index.Rows, r => r.Note == "本次未勾选");
        Assert.Contains(index.Rows, r => r.Restorable);
    }

    [Fact]
    public void Should_fail_when_directory_missing()
    {
        using var root = new TempRoot();

        var index = ManifestReader.TryRead(root.Combine("nope"), out var error);

        Assert.Null(index);
        Assert.Contains("不存在", error);
    }

    [Fact]
    public void Should_fail_when_manifest_was_modified()
    {
        using var root = new TempRoot();
        var paths = new ManifestWriter().Write(BuildPlan(), root.Combine("reports"));

        // 手工把某个数据行的列数改坏：凭据被改过就必须报错，不能猜
        var content = File.ReadAllLines(paths.CsvPath);
        content[1] = content[1] + ",多余的一列";
        File.WriteAllLines(paths.CsvPath, content);

        var index = ManifestReader.TryRead(paths.Directory, out var error);

        Assert.Null(index);
        Assert.Contains("列数不符", error);
    }

    [Fact]
    public void Should_read_quoted_and_escaped_fields()
    {
        using var root = new TempRoot();
        var directory = root.Combine("reports");
        Directory.CreateDirectory(directory);

        // 手工写一份带引号/逗号/转义引号的清单：读回必须原样还原（走公共 API，不碰内部解析器）
        var lines = new[]
        {
            ManifestWriter.CsvHeader,
            "L1 一键直清,l1.user-temp,用户临时目录,\"C:\\Temp\\带,逗号\"\"引号\"\".tmp\",10,2026-09-20 14:30:00,移入隔离区,是,"
        };
        File.WriteAllLines(Path.Combine(directory, ManifestReader.CsvFileName), lines);

        var index = ManifestReader.TryRead(directory, out var error);

        Assert.NotNull(index);
        Assert.Equal(string.Empty, error);
        var row = Assert.Single(index!.Rows);
        Assert.Equal(@"C:\Temp\带,逗号""引号"".tmp", row.OriginalPath);
        Assert.Equal(10, row.SizeBytes);
        Assert.True(row.Restorable);
    }

    [Fact]
    public void Settings_defaults_should_place_reports_under_log_directory()
    {
        // 复核时要按默认值找到清单目录：默认报告目录必须与默认日志目录同源
        var settings = new AppSettings();

        Assert.StartsWith(settings.LogDirectory, settings.ReportDirectory, StringComparison.OrdinalIgnoreCase);
    }
}
