using System.Collections.ObjectModel;
using CodeMemo.Helpers;
using CodeMemo.Models;
using CodeMemo.Services;

namespace CodeMemo.ViewModels;

/// <summary>命令新增 / 编辑窗口视图模型：字段校验后回调保存。</summary>
public sealed class CommandEditorViewModel : ViewModelBase
{
    private readonly CommandEntry? _original;
    private readonly IDialogService _dialogs;

    /// <summary>确定后回调：新增时传新条目，编辑时传原条目。</summary>
    public Action<CommandEntry>? Confirmed { get; set; }

    public CommandEditorViewModel(CommandEntry? original, string? defaultCategory, string? defaultGroup, IDialogService dialogs)
    {
        _original = original;
        _dialogs = dialogs;
        _category = original?.Category ?? defaultCategory ?? Catalog.CategoryNames[0];
        _group = original?.Group ?? defaultGroup ?? "";
        _title = original?.Title ?? "";
        _command = original?.Command ?? "";
        _note = original?.Note ?? "";
        IsNew = original is null;
        ReloadGroupCandidates();
    }

    public bool IsNew { get; }

    /// <summary>窗口标题由界面据此生成。</summary>
    public string EditorTitle => IsNew ? "新增命令" : "编辑命令";

    /// <summary>大分类候选（固定两大分类，有序）。</summary>
    public IReadOnlyList<string> Categories => Catalog.CategoryNames;

    public string Category
    {
        get => _category;
        set
        {
            if (SetProperty(ref _category, value))
            {
                ReloadGroupCandidates();
                // 切换大分类后原分组大概率不适用，清空待选
                Group = "";
            }
        }
    }
    private string _category = "";

    /// <summary>子分组候选：当前大分类下的预置分组（可编辑下拉）。</summary>
    public ObservableCollection<string> GroupCandidates { get; } = [];

    private void ReloadGroupCandidates()
    {
        GroupCandidates.Clear();
        foreach (var group in Catalog.GroupsOf(Category))
        {
            GroupCandidates.Add(group);
        }
    }

    /// <summary>子分组（可编辑下拉：既可从候选选，也可直接输入新分组）。</summary>
    public string Group
    {
        get => _group;
        set => SetProperty(ref _group, value);
    }
    private string _group = "";

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }
    private string _title = "";

    public string Command
    {
        get => _command;
        set => SetProperty(ref _command, value);
    }
    private string _command = "";

    public string Note
    {
        get => _note;
        set => SetProperty(ref _note, value);
    }
    private string _note = "";

    /// <summary>校验提示：标题 / 命令 / 分类 / 分组必填。</summary>
    public string ValidationMessage
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Title))
            {
                return "请填写命令标题";
            }
            if (string.IsNullOrWhiteSpace(Command))
            {
                return "请填写命令内容";
            }
            if (!Catalog.Categories.ContainsKey(Category))
            {
                return "请选择大分类";
            }
            if (string.IsNullOrWhiteSpace(Group))
            {
                return "请填写子分组";
            }
            return "";
        }
    }

    /// <summary>确定：校验通过则回调保存，否则弹出提示。窗口据此决定是否关闭。</summary>
    public void Confirm()
    {
        var message = ValidationMessage;
        if (message != "")
        {
            _dialogs.ShowWarning(message, "信息不完整");
            return;
        }
        var entry = _original ?? new CommandEntry();
        entry.Title = Title.Trim();
        entry.Command = Command.Trim();
        entry.Note = Note.Trim();
        entry.Category = Category;
        entry.Group = Group.Trim();
        Confirmed?.Invoke(entry);
    }
}
