using CodeMemo.Models;

namespace CodeMemo.Services;

/// <summary>导入方式。</summary>
public enum ImportMode
{
    /// <summary>整体替换现有命令库（老行为）。</summary>
    Replace = 0,

    /// <summary>与现有命令库合并，重复条目自动跳过。</summary>
    Merge = 1,
}

/// <summary>合并结果：合并后的条目列表 + 计数（用于提示）。</summary>
public sealed record MergeResult(List<CommandEntry> Commands, int Added, int Updated, int Skipped);

/// <summary>
/// 命令库合并（纯函数，可单测）：
/// - 现有条目在前，导入进来的新条目按原顺序追加；
/// - 同 Id 视为同一条命令：内容不同则**更新描述字段**（标题/命令/备注/分类/分组），但保留本机的使用统计与创建时间；
/// - 内容指纹相同（分类/分组/标题忽略大小写与首尾空白，命令忽略首尾空白与换行差异）视为重复，跳过。
/// </summary>
public static class LibraryMerger
{
    public static MergeResult Merge(IReadOnlyList<CommandEntry> existing, IReadOnlyList<CommandEntry> incoming)
    {
        var result = new List<CommandEntry>(existing);
        var byId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var contentKeys = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < result.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(result[i].Id))
            {
                byId[result[i].Id] = i;
            }
            contentKeys.Add(ContentKey(result[i]));
        }

        var added = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var entry in incoming)
        {
            if (!string.IsNullOrWhiteSpace(entry.Id) && byId.TryGetValue(entry.Id, out var index))
            {
                var current = result[index];
                if (ContentKey(current) == ContentKey(entry))
                {
                    skipped++;
                    continue;
                }

                contentKeys.Remove(ContentKey(current));
                ApplyDescription(current, entry);
                contentKeys.Add(ContentKey(current));
                updated++;
                continue;
            }

            if (!contentKeys.Add(ContentKey(entry)))
            {
                skipped++;
                continue;
            }

            result.Add(entry);
            if (!string.IsNullOrWhiteSpace(entry.Id))
            {
                byId[entry.Id] = result.Count - 1;
            }
            added++;
        }

        return new MergeResult(result, added, updated, skipped);
    }

    /// <summary>
    /// 内容指纹：分类 / 分组 / 标题忽略大小写与首尾空白，命令额外忽略换行风格差异。
    /// 命令本身区分大小写（`-Force` 和 `-force` 不是一回事）。
    /// </summary>
    public static string ContentKey(CommandEntry entry) => string.Join(
        '\u0001',
        Normalize(entry.Category),
        Normalize(entry.Group),
        Normalize(entry.Title),
        NormalizeCommand(entry.Command));

    /// <summary>同 Id 合并时只覆盖描述字段，使用统计与创建时间留给本机。</summary>
    private static void ApplyDescription(CommandEntry target, CommandEntry source)
    {
        target.Title = source.Title;
        target.Command = source.Command;
        target.Note = source.Note;
        target.Category = source.Category;
        target.Group = source.Group;
        target.UpdatedAt = DateTime.Now;
    }

    private static string Normalize(string? value) => (value ?? "").Trim().ToLowerInvariant();

    private static string NormalizeCommand(string? command)
        => (command ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();
}
