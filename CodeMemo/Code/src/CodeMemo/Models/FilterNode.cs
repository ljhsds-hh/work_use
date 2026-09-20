namespace CodeMemo.Models;

/// <summary>
/// 左侧分类树节点（纯数据，由 Services/CommandTreeBuilder 构建）：
/// 根（全部命令）→ 大分类 → 子分组 三层。
/// Category / Group 同为 null 表示该节点不限定分类 / 分组；
/// Group 为空字符串表示「未分组」节点，用于筛出手工改库产生的空分组条目。
/// </summary>
public sealed class FilterNode
{
    /// <summary>显示文字。</summary>
    public string Label { get; init; } = "";

    /// <summary>大分类名；根节点为 null，空字符串表示未分类条目。</summary>
    public string? Category { get; init; }

    /// <summary>子分组名；根与大分类节点为 null，空字符串表示未分组条目。</summary>
    public string? Group { get; init; }

    /// <summary>该范围内条目数（角标）；恒等于其子节点 Count 之和。</summary>
    public int Count { get; set; }

    /// <summary>子节点：根节点下是大分类，大分类下是子分组。</summary>
    public List<FilterNode> Children { get; } = [];

    /// <summary>树节点是否展开：根与大分类默认展开，子分组默认收起。</summary>
    public bool IsExpanded { get; set; }

    public override string ToString() => Label;
}
