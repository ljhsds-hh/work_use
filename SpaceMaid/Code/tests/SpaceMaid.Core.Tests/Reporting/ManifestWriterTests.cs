using SpaceMaid.Core.Models;
using SpaceMaid.Core.Reporting;
using SpaceMaid.Core.Tests.Abstractions;

namespace SpaceMaid.Core.Tests.Reporting;

public class VolumeTextFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1024L * 1024 * 1024, "1 GB")]
    [InlineData(3L * 1024 * 1024 * 1024 / 2, "1.5 GB")]
    public void FormatBytes_should_be_human_readable(long bytes, string expected)
    {
        Assert.Equal(expected, VolumeTextFormatter.FormatBytes(bytes));
    }

    [Fact]
    public void Same_volume_should_say_moved_not_freed()
    {
        var text = VolumeTextFormatter.DescribeProcessed(1024L * 1024 * 1024, sameVolume: true);

        Assert.Contains("已移入隔离区", text);
        Assert.DoesNotContain("已释放", text);
    }

    [Fact]
    public void Cross_volume_should_say_freed()
    {
        var text = VolumeTextFormatter.DescribeProcessed(1024L * 1024 * 1024, sameVolume: false);

        Assert.Contains("已释放", text);
        Assert.DoesNotContain("已移入隔离区", text);
    }

    [Fact]
    public void Summary_should_carry_both_numbers()
    {
        var text = VolumeTextFormatter.DescribeSummary(2L * 1024 * 1024 * 1024, 1024L * 1024 * 1024, sameVolume: true);

        Assert.Contains("本次可处理 2 GB", text);
        Assert.Contains("已移入隔离区 1 GB", text);
    }
}

public class ManifestWriterTests
{
    private readonly ManifestWriter _writer = new();

    [Fact]
    public void Csv_should_list_every_file()
    {
        using var root = new TempRoot();
        var l1 = PlanFixtures.Definition("l1.user-temp", CleanCategory.L1OneClick);
        var l3 = PlanFixtures.Definition(
            "l3.chat-cache",
            CleanCategory.L3Cautious,
            risk: ItemRisk.Dangerous,
            actionNote: "可能包含你要保留的聊天文件",
            restoreHint: "恢复：从隔离区还原");

        var plan = PlanFixtures.Plan(
            new[]
            {
                PlanFixtures.PlanItem(l1, true, PlanFixtures.File(@"C:\Temp\a.tmp", 10), PlanFixtures.File(@"C:\Temp\b.tmp", 20)),
                PlanFixtures.PlanItem(l3, false, PlanFixtures.File(@"C:\Chat\c.dat", 30))
            },
            Array.Empty<ScanEntry>());

        var paths = _writer.Write(plan, root.Combine("reports"));
        var lines = File.ReadAllLines(paths.CsvPath);

        Assert.Equal(ManifestWriter.CsvHeader, lines[0]);
        Assert.Equal(plan.ManifestFileCount + 1, lines.Length);           // 穷尽性：每个文件一行（含信息项）
        Assert.Contains(lines, l => l.Contains(@"C:\Chat\c.dat") && l.Contains("本次未勾选"));
        Assert.All(lines.Skip(1), l => Assert.Equal(9, CountCsvFields(l)));
    }

    [Fact]
    public void Markdown_should_include_action_note_and_restore_hint()
    {
        using var root = new TempRoot();
        var l3 = PlanFixtures.Definition(
            "l3.hibernate",
            CleanCategory.L3Cautious,
            CleanActionKind.HibernateOff,
            ItemRisk.Dangerous,
            actionNote: "仅关闭功能，不删文件",
            sideEffect: "会连带关掉快速启动",
            restoreHint: "恢复：powercfg /h on");

        var plan = PlanFixtures.Plan(
            new[] { PlanFixtures.PlanItem(l3, false) },
            Array.Empty<ScanEntry>());

        var paths = _writer.Write(plan, root.Combine("reports"));
        var markdown = File.ReadAllText(paths.MarkdownPath);

        Assert.Contains("（仅关闭功能，不删文件）", markdown);
        Assert.Contains("恢复：powercfg /h on", markdown);
        Assert.Contains("会连带关掉快速启动", markdown);
        Assert.Contains("未勾选，本次不处理", markdown);
    }

    [Fact]
    public void Should_export_readonly()
    {
        using var root = new TempRoot();
        var scannedFile = root.WriteFile(@"scanned\a.tmp", new string('a', 64));
        var before = Snapshot(root.Combine("scanned"));

        var l1 = PlanFixtures.Definition("l1.user-temp", CleanCategory.L1OneClick);
        var plan = PlanFixtures.Plan(
            new[] { PlanFixtures.PlanItem(l1, true, PlanFixtures.File(scannedFile, 64)) },
            Array.Empty<ScanEntry>());

        _writer.Write(plan, root.Combine("reports"));

        Assert.Equal(before, Snapshot(root.Combine("scanned")));
        Assert.True(File.Exists(scannedFile));
    }

    [Fact]
    public void Should_record_kept_files_in_markdown()
    {
        using var root = new TempRoot();
        var l1 = PlanFixtures.Definition("l1.dumps", CleanCategory.L1OneClick);
        var planItem = PlanFixtures.PlanItem(l1, true, PlanFixtures.File(@"C:\Windows\Minidump\old.dmp", 100)) with
        {
            Kept = new[] { PlanFixtures.File(@"C:\Windows\MEMORY.DMP", 2048) }
        };

        var plan = PlanFixtures.Plan(new[] { planItem }, Array.Empty<ScanEntry>());
        var paths = _writer.Write(plan, root.Combine("reports"));
        var markdown = File.ReadAllText(paths.MarkdownPath);

        Assert.Contains("保留：", markdown);
        Assert.Contains("MEMORY.DMP", markdown);
    }

    [Fact]
    public void Should_build_index_with_same_row_count_as_plan()
    {
        using var root = new TempRoot();
        var l1 = PlanFixtures.Definition("l1.user-temp", CleanCategory.L1OneClick);
        var plan = PlanFixtures.Plan(
            new[] { PlanFixtures.PlanItem(l1, true, PlanFixtures.File(@"C:\Temp\a.tmp", 1), PlanFixtures.File(@"C:\Temp\b.tmp", 2)) },
            Array.Empty<ScanEntry>());

        var paths = _writer.Write(plan, root.Combine("reports"));
        var index = ManifestWriter.BuildIndex(plan, paths.Directory);

        Assert.Equal(plan.ManifestFileCount, index.Rows.Count);
    }

    private static int CountCsvFields(string line)
    {
        var count = 1;
        var inQuotes = false;
        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (ch == ',' && !inQuotes)
            {
                count++;
            }
        }

        return count;
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
