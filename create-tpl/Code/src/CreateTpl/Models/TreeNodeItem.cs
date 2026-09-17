using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CreateTpl.Models;

/// <summary>
/// 骨架拓扑预览的树节点：支持按文件夹层级展开/折叠（工程根 → 一级条目 → Docs 下文件）。
/// </summary>
public class TreeNodeItem : INotifyPropertyChanged
{
    private bool _isExpanded;

    public TreeNodeItem(string name, bool isFolder = false, bool isProjectRoot = false, string path = "")
    {
        Name = name;
        IsFolder = isFolder;
        IsProjectRoot = isProjectRoot;
        Path = path;
        Children = new ObservableCollection<TreeNodeItem>();
    }

    /// <summary>节点显示名（工程根目录带尾部 / ）。</summary>
    public string Name { get; }

    /// <summary>是否为文件夹节点（决定图标：文件夹琥珀色 / 文件天蓝）。</summary>
    public bool IsFolder { get; }

    /// <summary>是否为工程根节点（渲染 Initialized 徽标）。</summary>
    public bool IsProjectRoot { get; }

    /// <summary>节点对应路径（Tooltip 用）。</summary>
    public string Path { get; }

    /// <summary>是否展开（工程根节点默认展开，其余默认折叠）。</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>子节点。</summary>
    public ObservableCollection<TreeNodeItem> Children { get; }

    /// <summary>供 UI Automation（屏幕阅读器）读取的节点名，避免暴露类型全名。</summary>
    public override string ToString() => Name;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
