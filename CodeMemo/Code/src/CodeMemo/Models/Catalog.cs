namespace CodeMemo.Models;

/// <summary>
/// 分类目录定义（需求 2.1）：v1 固定 PowerShell / Git 两大分类，
/// 每个大分类下再分子分组。分类与子分组顺序即界面展示顺序。
/// 预置命令库与编辑窗口的分组下拉均以此为准。
/// </summary>
public static class Catalog
{
    /// <summary>大分类 → 子分组（有序）。</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Categories = new Dictionary<string, IReadOnlyList<string>>
    {
        ["PowerShell"] = new[]
        {
            "文件与目录",
            "文本与内容",
            "进程与服务",
            "网络诊断",
            "系统信息",
            "压缩与归档",
            "脚本与安全",
        },
        ["Git"] = new[]
        {
            "配置与初始化",
            "基础操作",
            "分支操作",
            "合并与变基",
            "撤销回退",
            "远程仓库",
            "查看历史",
            "标签",
            "暂存 stash",
            "实用技巧",
        },
    };

    /// <summary>全部大分类名（有序）。</summary>
    public static IReadOnlyList<string> CategoryNames { get; } = [.. Categories.Keys];

    /// <summary>指定大分类下的子分组（有序）；未知分类返回空。</summary>
    public static IReadOnlyList<string> GroupsOf(string category)
        => Categories.TryGetValue(category, out var groups) ? groups : [];

    /// <summary>分类、子分组是否合法（数据校验用）。</summary>
    public static bool IsValid(string category, string group)
        => GroupsOf(category).Contains(group);
}
