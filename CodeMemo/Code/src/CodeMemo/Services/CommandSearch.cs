using System.Text;
using CodeMemo.Models;

namespace CodeMemo.Services;

public enum SortMode
{
    /// <summary>默认顺序：按分类、子分组、标题。</summary>
    Default = 0,

    /// <summary>最近使用：LastUsedAt 降序，从未使用的排后面。</summary>
    RecentlyUsed = 1,

    /// <summary>使用最多：UseCount 降序。</summary>
    MostUsed = 2,
}

/// <summary>
/// 命令搜索过滤（纯函数，可单测）：
/// 关键词按空白拆分后逐个匹配（AND），匹配范围 = 标题 + 命令全文 + 备注，忽略大小写。
/// 有搜索词时忽略分类/分组限定，跨库显示匹配结果（需求 4.2）。
/// </summary>
public static class CommandSearch
{
    public static IReadOnlyList<CommandEntry> Filter(
        IEnumerable<CommandEntry> entries,
        string? query,
        string? category = null,
        string? group = null,
        SortMode sort = SortMode.Default)
    {
        var tokens = SplitTokens(query);

        IEnumerable<CommandEntry> matched = entries;
        if (tokens.Count > 0)
        {
            // 有搜索词：跨分类全库匹配
            matched = matched.Where(e => MatchesAll(e, tokens));
        }
        else
        {
            // 无搜索词：按左侧树选中的分类/分组过滤。
            // null 表示「该节点不限定这一层」；空字符串表示「未分类 / 未分组」条目（树里的兜底节点）。
            if (category is not null)
            {
                matched = matched.Where(e => (e.Category ?? "") == category);
            }
            if (group is not null)
            {
                matched = matched.Where(e => (e.Group ?? "") == group);
            }
        }

        return [.. matched.Sort(sort)];
    }

    private static IEnumerable<CommandEntry> Sort(this IEnumerable<CommandEntry> entries, SortMode mode)
        => mode switch
        {
            SortMode.RecentlyUsed => entries.OrderByDescending(e => e.LastUsedAt ?? DateTime.MinValue),
            SortMode.MostUsed => entries
                .OrderByDescending(e => e.UseCount)
                .ThenByDescending(e => e.LastUsedAt ?? DateTime.MinValue),
            _ => entries
                .OrderBy(e => Array.IndexOf(Catalog.CategoryNames.ToArray(), e.Category))
                .ThenBy(e => GroupOrder(e.Category, e.Group))
                .ThenBy(e => e.Title, StringComparer.CurrentCulture),
        };

    private static int GroupOrder(string category, string group)
    {
        var groups = Catalog.GroupsOf(category);
        var idx = groups.ToList().IndexOf(group);
        return idx < 0 ? int.MaxValue : idx;
    }

    internal static List<string> SplitTokens(string? query)
        => string.IsNullOrWhiteSpace(query)
            ? []
            : [.. query.Split(' ', '\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static bool MatchesAll(CommandEntry e, List<string> tokens)
    {
        // 拼合为单 haystack，逐 token 判断，避免多次 ToLower 分配过大
        var haystack = string.Concat(e.Title, '\n', e.Command, '\n', e.Note).ToLowerInvariant();
        foreach (var token in tokens)
        {
            if (!haystack.Contains(token.ToLowerInvariant(), StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }
}
