using SpaceMaid.Core.Models;
using SpaceMaid.Core.Reporting;
using SpaceMaid.Core.Tests.Abstractions;

namespace SpaceMaid.Core.Tests.Reporting;

/// <summary>
/// 信息项（页面文件，需求 2.4）的体积口径。
///
/// 为什么单独立一组用例：信息项在界面上"占一个位置给人看体积"，但**永远不会被执行**
/// （`CleanExecutor` 直接跳过、界面上连勾选框都没有、`Denylist` 也硬拦着 `pagefile.sys`）。
/// 因此它一旦被算进"本次计划处理"，清单就是在承诺一件永远不会发生的事。
///
/// 这不是假想问题：真机 dry-run 的清单头部写着"39134 个文件，共 21.32 GB"，
/// 其中 15 GB 是 `C:\pagefile.sys`；扣掉它之后真正会被处理的是 6.32 GB。
/// </summary>
public sealed class InformationalItemVolumeTests
{
    private const long PageFileBytes = 16L * 1024 * 1024 * 1024;

    private readonly ManifestWriter _writer = new();

    private static CleanPlan BuildPlan()
    {
        var normal = PlanFixtures.Definition("l1.user-temp", CleanCategory.L1OneClick);
        var info = PlanFixtures.Definition(
            "l3.pagefile",
            CleanCategory.L3Cautious,
            CleanActionKind.InformationalOnly,
            ItemRisk.Caution,
            actionNote: "仅展示，不可清理");

        var normalFiles = new[]
        {
            PlanFixtures.File(@"C:\Temp\a.tmp", 1024, action: CleanActionKind.Quarantine),
            PlanFixtures.File(@"C:\Temp\b.tmp", 2048, action: CleanActionKind.Quarantine)
        };
        var infoFiles = new[] { PlanFixtures.File(@"C:\pagefile.sys", PageFileBytes, action: CleanActionKind.InformationalOnly) };

        return PlanFixtures.Plan(
            new[]
            {
                PlanFixtures.PlanItem(normal, userChecked: true, normalFiles),
                PlanFixtures.PlanItem(info, userChecked: false, infoFiles)
            },
            new[]
            {
                PlanFixtures.Entry(normal, normalFiles),
                PlanFixtures.Entry(info, infoFiles)
            });
    }

    [Fact]
    public void Planned_bytes_and_files_should_exclude_informational_items()
    {
        var plan = BuildPlan();

        Assert.Equal(3072, plan.PlannedBytes);
        Assert.Equal(2, plan.PlannedFileCount);
        Assert.Equal(PageFileBytes, plan.InformationalBytes);
    }

    [Fact]
    public void Manifest_file_count_should_cover_every_row_including_informational()
    {
        var plan = BuildPlan();

        // 清单穷尽性口径：csv 行数（含信息项那一行）= ManifestFileCount。
        // 它**不能**用 PlannedFileCount——那个数字故意不含信息项，用来做完整性校验会永远对不上。
        Assert.Equal(3, plan.ManifestFileCount);
    }

    [Fact]
    public void Csv_should_keep_one_row_per_file_and_label_the_informational_one()
    {
        using var root = new TempRoot();
        var plan = BuildPlan();

        var paths = _writer.Write(plan, root.Combine("reports"));
        var lines = File.ReadAllLines(paths.CsvPath);

        Assert.Equal(plan.ManifestFileCount + 1, lines.Length); // +1 是表头
        Assert.Contains(lines, l => l.Contains(@"C:\pagefile.sys") && l.Contains("仅展示，不执行"));
        Assert.Contains("不计入", File.ReadAllText(paths.CsvPath));
    }

    [Fact]
    public void Markdown_planned_total_should_not_include_informational_items()
    {
        using var root = new TempRoot();
        var plan = BuildPlan();

        var paths = _writer.Write(plan, root.Combine("plans"));
        var markdown = File.ReadAllText(paths.MarkdownPath);

        Assert.Contains("本次计划处理：2 个文件，共 3 KB", markdown);
        Assert.Contains("16 GB", markdown);                         // 页面文件的体积单独说明，不藏起来
        Assert.DoesNotContain("本次计划处理：3 个文件", markdown);    // 更不能把它算进计划处理总量
    }

    [Fact]
    public void Markdown_should_say_informational_item_is_never_executed()
    {
        using var root = new TempRoot();
        var plan = BuildPlan();

        var paths = _writer.Write(plan, root.Combine("plans"));
        var markdown = File.ReadAllText(paths.MarkdownPath);

        Assert.Contains("仅展示，不执行", markdown);
        Assert.DoesNotContain("未勾选，本次不处理", markdown); // 信息项不是"这次没勾"，而是"永远不执行"
    }

    [Fact]
    public void Index_row_count_should_equal_manifest_file_count()
    {
        using var root = new TempRoot();
        var plan = BuildPlan();

        var paths = _writer.Write(plan, root.Combine("plans"));
        var index = ManifestWriter.BuildIndex(plan, paths.Directory);

        Assert.Equal(plan.ManifestFileCount, index.Rows.Count);
    }
}
