using System.Text.Json.Serialization;

namespace CodeMemo.Models;

/// <summary>一条命令条目：标题、命令全文、备注、所属分类与子分组、使用统计。</summary>
public sealed class CommandEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>短标题（列表主显示），如"撤销未推送的提交"。</summary>
    public string Title { get; set; } = "";

    /// <summary>命令全文，可替换参数统一用成对尖括号标注，如 &lt;branch-name&gt;。</summary>
    public string Command { get; set; } = "";

    /// <summary>备注：用途说明、适用场景、避坑提示。</summary>
    public string Note { get; set; } = "";

    /// <summary>大分类名，见 Catalog.Categories（v1：PowerShell / Git）。</summary>
    public string Category { get; set; } = "";

    /// <summary>大分类下的子分组名，见 Catalog.GroupsOf。</summary>
    public string Group { get; set; } = "";

    /// <summary>复制（使用）次数，用于"使用最多"排序。</summary>
    public int UseCount { get; set; }

    /// <summary>最近一次复制时间，用于"最近使用"排序。</summary>
    public DateTime? LastUsedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>命令库持久化数据结构（commands.json）。</summary>
public sealed class CommandLibraryData
{
    /// <summary>
    /// 数据结构版本：0 表示文件未标注版本（手写文件），加载时按当前版本处理并提示；
    /// 写出时由 LibraryStore 统一写成 LibraryStore.CurrentSchemaVersion。
    /// </summary>
    [JsonPropertyOrder(-1)]
    public int SchemaVersion { get; set; }

    public List<CommandEntry> Commands { get; set; } = [];
}
