using System.Text;
using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Logging;
using SpaceMaid.Core.Models;

namespace SpaceMaid.Core.Reporting;

/// <summary>
/// 执行后复核（需求 3.9-4）：重新确认清单里每个文件"到底去哪了"，产出一份**审核完成的凭据**。
///
/// 三种结论（缺一不可，异常项必须逐条列出）：
/// 1. 已清理：原位已不存在，且隔离区账本里有它的记录；
/// 2. 未清理：文件仍在原位（未处理或被跳过）；
/// 3. 异常：清单里有、但状态无法解释（原位没了账本也没记录，或两边同时存在）——必须原样列出，绝不静默吞掉。
/// </summary>
public sealed class ReviewReporter
{
    private readonly IFileSystem _fileSystem;
    private readonly IClock _clock;
    private readonly ILogSink _log;

    public ReviewReporter(IFileSystem fileSystem, IClock clock, ILogSink? log = null)
    {
        _fileSystem = fileSystem;
        _clock = clock;
        _log = log ?? SilentLogSink.Instance;
    }

    /// <summary>执行复核并写出 <c>复核报告.md</c>。</summary>
    public ReviewReport Review(
        ManifestRowIndex before,
        IReadOnlyList<QuarantineMap> quarantineMaps,
        string outputRoot,
        long releasedBytes,
        bool sameVolume)
    {
        ArgumentNullException.ThrowIfNull(before);

        var directory = string.IsNullOrWhiteSpace(outputRoot) ? before.Directory : outputRoot;
        Directory.CreateDirectory(directory);

        var items = new List<ReviewItem>();
        foreach (var row in before.Rows)
        {
            var existsInPlace = _fileSystem.FileExists(row.OriginalPath);
            var hasQuarantineRecord = quarantineMaps.Any(map => map.Entries.Any(entry =>
                entry.Status == MapEntryStatus.Stored
                && entry.OriginalPath.Equals(row.OriginalPath, StringComparison.OrdinalIgnoreCase)));

            if (!existsInPlace && hasQuarantineRecord)
            {
                items.Add(new ReviewItem(row.ItemId, row.OriginalPath, ReviewStatus.Cleaned, "已按计划移入隔离区"));
            }
            else if (existsInPlace && !hasQuarantineRecord)
            {
                items.Add(new ReviewItem(row.ItemId, row.OriginalPath, ReviewStatus.Skipped, "仍在原位：未处理或被跳过"));
            }
            else if (!existsInPlace)
            {
                items.Add(new ReviewItem(row.ItemId, row.OriginalPath, ReviewStatus.Unknown, "原位已不存在，但隔离区账本中没有它的记录"));
            }
            else
            {
                items.Add(new ReviewItem(row.ItemId, row.OriginalPath, ReviewStatus.Unknown, "原位置与隔离区同时存在该文件，需人工确认"));
            }
        }

        var reviewedAt = _clock.Now;
        var report = new ReviewReport(directory, reviewedAt, items, releasedBytes, sameVolume, string.Empty);
        var markdownPath = Path.Combine(directory, "复核报告.md");
        File.WriteAllText(markdownPath, BuildMarkdown(before, report), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        _log.Info($"复核完成：已清理 {report.CleanedCount}，未清理 {report.SkippedCount}，异常 {report.UnknownCount}");

        return report with { MarkdownPath = markdownPath };
    }

    private static string BuildMarkdown(ManifestRowIndex before, ReviewReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# SpaceMaid 复核报告 · {Path.GetFileName(before.Directory)}");
        builder.AppendLine();
        builder.AppendLine($"- 复核时间：{report.ReviewedAt:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"- 清单目录：`{before.Directory}`");
        builder.AppendLine($"- 清单文件数：{before.Rows.Count}");
        builder.AppendLine($"- 已清理：{report.CleanedCount}");
        builder.AppendLine($"- 未清理：{report.SkippedCount}");
        builder.AppendLine($"- 异常：{report.UnknownCount}");
        builder.AppendLine($"- 实际效果：{VolumeTextFormatter.DescribeProcessed(report.ReleasedBytes, report.SameVolume)}");
        builder.AppendLine();
        builder.AppendLine("> 只有本报告确认清单内项目全部按预期处理（或未处理原因已明确解释），本次清理才算审核通过。");
        builder.AppendLine();

        AppendSection(builder, "异常（必须逐条确认）", report.Items.Where(i => i.Status == ReviewStatus.Unknown));
        AppendSection(builder, "未清理 / 跳过", report.Items.Where(i => i.Status == ReviewStatus.Skipped));
        AppendSection(builder, "已清理", report.Items.Where(i => i.Status == ReviewStatus.Cleaned));

        return builder.ToString();
    }

    private static void AppendSection(StringBuilder builder, string title, IEnumerable<ReviewItem> items)
    {
        var list = items.ToList();
        builder.AppendLine($"## {title}（{list.Count}）");
        builder.AppendLine();

        if (list.Count == 0)
        {
            builder.AppendLine("无。");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("| 项目 Id | 原始路径 | 说明 |");
        builder.AppendLine("| --- | --- | --- |");
        foreach (var item in list)
        {
            builder.AppendLine($"| {item.ItemId} | `{item.OriginalPath}` | {item.Detail} |");
        }

        builder.AppendLine();
    }
}
