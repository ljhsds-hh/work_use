using CodeMemo.Models;

namespace CodeMemo.Services;

/// <summary>
/// 左侧分类树构建（纯函数，可单测）。
///
/// 节点来源 = 预置分类/分组（Catalog，按定义顺序，分组的条目数为 0 也保留）
///          ∪ 数据中实际出现的自定义分类/分组（追加在后面，按名称排序）。
/// 这样编辑窗口里输入的新子分组不会「存下来却看不到」，同时保证
/// 任一节点的 Count 恒等于其子节点 Count 之和（父子计数一致）。
/// </summary>
public static class CommandTreeBuilder
{
    public const string AllLabel = "全部命令";
    public const string UngroupedLabel = "（未分组）";
    public const string UncategorizedLabel = "（未分类）";

    /// <summary>构建根节点（根 -> 大分类 -> 子分组）。</summary>
    public static FilterNode BuildRoot(IEnumerable<CommandEntry> entries)
    {
        var all = entries as IReadOnlyList<CommandEntry> ?? entries.ToList();

        var root = new FilterNode { Label = AllLabel, Count = all.Count, IsExpanded = true };

        foreach (var category in OrderedCategories(all))
        {
            var inCategory = all.Where(e => CategoryKey(e) == category).ToList();
            var categoryNode = new FilterNode
            {
                Label = category.Length == 0 ? UncategorizedLabel : category,
                Category = category,
                Count = inCategory.Count,
                IsExpanded = true,
            };

            foreach (var group in OrderedGroups(Catalog.GroupsOf(category), inCategory))
            {
                categoryNode.Children.Add(new FilterNode
                {
                    Label = group.Length == 0 ? UngroupedLabel : group,
                    Category = category,
                    Group = group,
                    Count = inCategory.Count(e => GroupKey(e) == group),
                });
            }

            root.Children.Add(categoryNode);
        }

        return root;
    }

    /// <summary>预置分类在前（Catalog 顺序），数据里多出来的自定义分类按名称追加。</summary>
    private static List<string> OrderedCategories(IReadOnlyList<CommandEntry> all)
    {
        var extras = all.Select(CategoryKey).Distinct()
            .Where(c => !Catalog.Categories.ContainsKey(c))
            .OrderBy(c => c, StringComparer.CurrentCulture);

        return [.. Catalog.CategoryNames, .. extras];
    }

    /// <summary>预置分组在前（Catalog 顺序），自定义分组按名称追加，未分组兜底在最后。</summary>
    private static List<string> OrderedGroups(IReadOnlyList<string> preset, IReadOnlyList<CommandEntry> inCategory)
    {
        var used = inCategory.Select(GroupKey).ToHashSet(StringComparer.Ordinal);

        var result = new List<string>();
        // 预置分组全部保留且顺序不变（当前没有条目的分组角标隐藏，点进去显示空态）
        result.AddRange(preset);
        result.AddRange(used.Where(g => g.Length > 0 && !preset.Contains(g))
            .OrderBy(g => g, StringComparer.CurrentCulture));
        if (used.Contains(""))
        {
            result.Add("");
        }
        return result;
    }

    private static string CategoryKey(CommandEntry entry) => entry.Category ?? "";

    private static string GroupKey(CommandEntry entry) => entry.Group ?? "";
}
