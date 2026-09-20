using System.Text;
using SpaceMaid.Core.Models;

namespace SpaceMaid.Core.Reporting;

/// <summary>
/// 清单导出（需求 3.9-1）。产出两份：
/// <c>清单.md</c> 给人审阅（按分级分组、含动作性质与恢复方式），<c>清单.csv</c> 给机器复核（逐文件穷尽）。
///
/// 两条硬性质：
/// 1. **穷尽性**：计划里每一个将被处理的文件都在 csv 里占一行——绝不允许"清单里没有、执行时却删了"；
/// 2. **零写操作**：导出过程只写清单文件本身，不碰任何被扫描目录。
/// </summary>
public sealed class ManifestWriter
{
    /// <summary>csv 表头（顺序固定，复核脚本按列名读取）。</summary>
    public const string CsvHeader = "分级,项目Id,项目名,原始路径,体积字节,最后修改,动作,是否可还原,备注";

    /// <summary>写出清单，返回三件套路径。</summary>
    public ManifestPaths Write(CleanPlan plan, string outputRoot)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var directory = Path.Combine(outputRoot, Sanitize(plan.PlanId));
        Directory.CreateDirectory(directory);

        var markdownPath = Path.Combine(directory, "清单.md");
        var csvPath = Path.Combine(directory, "清单.csv");

        File.WriteAllText(markdownPath, BuildMarkdown(plan), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.WriteAllText(csvPath, BuildCsv(plan), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        return new ManifestPaths(directory, markdownPath, csvPath);
    }

    /// <summary>从计划构建清单索引（复核阶段拿它与执行后的现状比对）。</summary>
    public static ManifestRowIndex BuildIndex(CleanPlan plan, string directory)
    {
        var rows = new List<ManifestRow>();
        foreach (var item in plan.Items)
        {
            foreach (var file in item.Files)
            {
                rows.Add(new ManifestRow(
                    CategoryText(item.Category),
                    item.ItemId,
                    item.DisplayName,
                    file.Path,
                    file.Size,
                    file.LastWrite,
                    ActionText(item.ActionKind),
                    Restorable: item.ActionKind is CleanActionKind.Quarantine,
                    Note: NoteText(item)));
            }
        }

        return new ManifestRowIndex(directory, rows);
    }

    private string BuildCsv(CleanPlan plan)
    {
        var builder = new StringBuilder();
        builder.AppendLine(CsvHeader);

        foreach (var item in plan.Items)
        {
            foreach (var file in item.Files)
            {
                var note = NoteText(item);
                builder.AppendLine(string.Join(',',
                    Csv(CategoryText(item.Category)),
                    Csv(item.ItemId),
                    Csv(item.DisplayName),
                    Csv(file.Path),
                    file.Size.ToString(),
                    Csv(file.LastWrite.ToString("yyyy-MM-dd HH:mm:ss")),
                    Csv(ActionText(item.ActionKind)),
                    item.ActionKind is CleanActionKind.Quarantine ? "是" : "否",
                    Csv(note)));
            }
        }

        return builder.ToString();
    }

    private string BuildMarkdown(CleanPlan plan)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# SpaceMaid 清理清单 · {plan.PlanId}");
        builder.AppendLine();
        builder.AppendLine($"- 生成时间：{plan.CreatedAt:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"- 扫描时间：{plan.Scan.ScannedAt:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"- 系统盘：{plan.Scan.Volume.Drive}（总容量 {VolumeTextFormatter.FormatBytes(plan.Scan.Volume.TotalBytes)}，可用 {VolumeTextFormatter.FormatBytes(plan.Scan.Volume.FreeBytes)}）");
        builder.AppendLine($"- 本次计划处理：{plan.PlannedFileCount} 个文件，共 {VolumeTextFormatter.FormatBytes(plan.PlannedBytes)}");

        // 信息项（页面文件）必须单独说：它占空间但永不执行，混进"计划处理"就是虚报
        // （真机清单曾经写成"39134 个文件，共 21.32 GB"，其中 15 GB 是 C:\pagefile.sys）。
        if (plan.InformationalBytes > 0)
        {
            var described = string.Join('、', plan.Informational.Select(i => $"{i.DisplayName} {VolumeTextFormatter.FormatBytes(i.TotalBytes)}"));
            builder.AppendLine($"- 仅展示、不执行：{described}（不计入上面的可处理体积）");
        }
        builder.AppendLine($"- 隔离区口径：{(plan.SameVolumeAsSource ? "与源文件同卷——清理后 C 盘空间不会立刻释放" : "位于其他盘——文件移出后立即释放")}");
        builder.AppendLine();
        builder.AppendLine("> 本清单只用于审阅。执行清理是一个单独的人工动作，导出清单本身不做任何删除或移动。");
        builder.AppendLine();

        foreach (var category in new[] { CleanCategory.L1OneClick, CleanCategory.L2Recommended, CleanCategory.L3Cautious, CleanCategory.RecycleBin })
        {
            var items = plan.Items.Where(i => i.Category == category).ToList();
            if (items.Count == 0)
            {
                continue;
            }

            builder.AppendLine($"## {CategoryText(category)}");
            builder.AppendLine();

            foreach (var item in items)
            {
                builder.AppendLine($"### {item.DisplayName}{(string.IsNullOrWhiteSpace(item.ActionNote) ? string.Empty : $"（{item.ActionNote}）")}");
                builder.AppendLine();
                builder.AppendLine($"- 项目 Id：`{item.ItemId}`");
                builder.AppendLine($"- 本次状态：{StatusText(item)}");
                builder.AppendLine($"- 动作：{ActionText(item.ActionKind)}；可还原：{(item.ActionKind is CleanActionKind.Quarantine ? "是（隔离区保留期内）" : "否")}");
                builder.AppendLine($"- 体积：{VolumeTextFormatter.FormatBytes(item.TotalBytes)}（{item.Files.Count} 个文件）");

                if (!string.IsNullOrWhiteSpace(item.SideEffect))
                {
                    builder.AppendLine($"- 后果说明：{item.SideEffect}");
                }

                if (!string.IsNullOrWhiteSpace(item.RestoreHint))
                {
                    builder.AppendLine($"- 恢复方式：{item.RestoreHint}");
                }

                if (!string.IsNullOrWhiteSpace(item.UnavailableReason))
                {
                    builder.AppendLine($"- 提示：{item.UnavailableReason}");
                }

                foreach (var kept in item.Kept)
                {
                    builder.AppendLine($"- 保留：{kept.Path}（{VolumeTextFormatter.FormatBytes(kept.Size)}，不处理）");
                }

                if (item.Files.Count > 0)
                {
                    builder.AppendLine();
                    builder.AppendLine("| # | 文件 | 体积 | 最后修改 |");
                    builder.AppendLine("| --- | --- | --- | --- |");
                    foreach (var (file, index) in item.Files.Take(50).Select((f, i) => (f, i + 1)))
                    {
                        builder.AppendLine($"| {index} | `{file.Path}` | {VolumeTextFormatter.FormatBytes(file.Size)} | {file.LastWrite:yyyy-MM-dd HH:mm:ss} |");
                    }

                    if (item.Files.Count > 50)
                    {
                        builder.AppendLine();
                        builder.AppendLine($"> 其余 {item.Files.Count - 50} 个文件见 `清单.csv`（csv 才是穷尽清单）。");
                    }
                }

                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    internal static string CategoryText(CleanCategory category) => category switch
    {
        CleanCategory.L1OneClick => "L1 一键直清",
        CleanCategory.L2Recommended => "L2 推荐清理",
        CleanCategory.L3Cautious => "L3 谨慎清理",
        CleanCategory.RecycleBin => "回收站",
        _ => category.ToString()
    };

    internal static string ActionText(CleanActionKind kind) => kind switch
    {
        CleanActionKind.Quarantine => "移入隔离区",
        CleanActionKind.HibernateOff => "执行命令：关闭休眠",
        CleanActionKind.DismComponentCleanup => "执行命令：DISM 组件清理",
        CleanActionKind.InformationalOnly => "仅展示，不执行",
        _ => kind.ToString()
    };

    /// <summary>
    /// 清单里的"本次状态"。信息项必须与普通项区分开：它不是"这次没勾选"，而是**永远不执行**——
    /// 写成"未勾选，本次不处理"会让人以为下次勾上就能清掉。
    /// </summary>
    internal static string StatusText(CleanPlanItem item) =>
        item.ActionKind == CleanActionKind.InformationalOnly
            ? "仅展示，不执行（本项不会清理任何文件）"
            : item.UserChecked ? "已勾选，会被处理" : "未勾选，本次不处理";

    /// <summary>csv/索引里每行的备注（复核脚本按这一列判断该行是否参与执行）。</summary>
    internal static string NoteText(CleanPlanItem item) =>
        item.ActionKind == CleanActionKind.InformationalOnly
            ? "仅展示项，不计入本次可处理体积"
            : item.UserChecked ? string.Empty : "本次未勾选";

    private static string Csv(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string Sanitize(string name)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "plan" : name;
    }
}
