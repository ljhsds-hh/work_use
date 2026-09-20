using CodeMemo.Models;

namespace CodeMemo.Services;

/// <summary>
/// 常用 / 最近榜单（纯函数，可单测）：给「常用 Top10」快捷面板提供排序。
/// 只收录真正用过的条目（复制过才会有统计），没用过的条目上榜没有意义。
/// </summary>
public static class CommandRanking
{
    public const int DefaultSize = 10;

    /// <summary>常用榜：使用次数降序，次数相同的按最近使用、再按标题。</summary>
    public static IReadOnlyList<CommandEntry> TopUsed(IEnumerable<CommandEntry> entries, int count = DefaultSize)
        => [.. entries
            .Where(e => e.UseCount > 0)
            .OrderByDescending(e => e.UseCount)
            .ThenByDescending(e => e.LastUsedAt ?? DateTime.MinValue)
            .ThenBy(e => e.Title, StringComparer.CurrentCulture)
            .Take(Math.Max(0, count))];

    /// <summary>最近榜：最近一次复制时间降序。</summary>
    public static IReadOnlyList<CommandEntry> TopRecent(IEnumerable<CommandEntry> entries, int count = DefaultSize)
        => [.. entries
            .Where(e => e.LastUsedAt is not null)
            .OrderByDescending(e => e.LastUsedAt)
            .ThenBy(e => e.Title, StringComparer.CurrentCulture)
            .Take(Math.Max(0, count))];
}
