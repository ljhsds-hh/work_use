using CodeMemo.Models;

namespace CodeMemo.ViewModels;

/// <summary>列表行视图模型：包一层条目，暴露列表展示所需字段。</summary>
public sealed class CommandRowViewModel : Helpers.ViewModelBase
{
    public CommandEntry Entry { get; }

    public CommandRowViewModel(CommandEntry entry) => Entry = entry;

    public string Title => Entry.Title;

    public string CommandText => Entry.Command;

    /// <summary>列表单行展示的命令文本：压平换行。</summary>
    public string CommandOneLine => Entry.Command.Replace("\n", " ; ");

    public string Group => Entry.Group;

    public string Category => Entry.Category;

    /// <summary>分组角标文字：大分类 · 子分组。</summary>
    public string GroupBadge => $"{Entry.Category} · {Entry.Group}";

    public string UseCountText => Entry.UseCount > 0 ? $"{Entry.UseCount} 次使用" : "";

    /// <summary>最近一次复制时间的简短展示，从未使用为空。</summary>
    public string LastUsedText => Entry.LastUsedAt is { } t ? t.ToString("yyyy-MM-dd HH:mm") : "";

    /// <summary>复制后使用统计变化，通知列表行与详情页刷新（否则次数要等下次重建列表才更新）。</summary>
    public void NotifyUsageChanged()
    {
        OnPropertyChanged(nameof(UseCountText));
        OnPropertyChanged(nameof(LastUsedText));
    }
}
