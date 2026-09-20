using SpaceMaid.App.Helpers;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Reporting;

namespace SpaceMaid.App.ViewModels;

/// <summary>
/// 清单里的一项（一行）。它只负责"怎么显示"与"用户勾没勾"，**不做任何清理判定**：
/// 动作性质、后果、恢复方式、风险等级全部来自内核的 <see cref="CleanPlanItem"/>（设计决策 D-8）。
/// </summary>
public sealed class CleanItemViewModel : ViewModelBase
{
    private bool _isChecked;

    public CleanItemViewModel(CleanPlanItem item, Action? onCheckedChanged = null)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        _isChecked = item.UserChecked;
        OnCheckedChanged = onCheckedChanged;
    }

    /// <summary>内核计划条目（只读真相来源；只用来取值，不在这里改它）。</summary>
    public CleanPlanItem Item { get; }

    public Action? OnCheckedChanged { get; }

    public string ItemId => Item.ItemId;

    public CleanCategory Category => Item.Category;

    public string DisplayName => Item.DisplayName;

    /// <summary>需求 5.4-3：风险项显示为 <c>名称（动作性质）</c>；安全项就是原名。</summary>
    public string DisplayNameWithNote =>
        Risk == ItemRisk.Safe || string.IsNullOrWhiteSpace(ActionNote)
            ? DisplayName
            : $"{DisplayName}（{ActionNote}）";

    /// <summary>需求 5.4：风险文案用红色文字单点强调（模板里绑 <c>DangerBrush</c>）。</summary>
    public bool IsDangerous => Risk != ItemRisk.Safe;

    /// <summary>风险等级对应的中文标签（列表里的一枚小标记）。</summary>
    public string RiskText => Risk switch
    {
        ItemRisk.Dangerous => "危险",
        ItemRisk.Caution => "注意",
        _ => "安全"
    };

    /// <summary>
    /// 风险色调（"safe" / "caution" / "danger"），给界面挑状态色标用。
    ///
    /// 为什么不直接把颜色写在视图模型里：需求 5.3-1 要求颜色只来自设计令牌，
    /// 而需求 5.4-3 要求"风险文案红色强调"——两边结合的做法就是视图模型只给**语义**（tone），
    /// 具体色值由 XAML 的 DataTrigger 映射到 HC 语义色（安全/Danger/Warning）。
    /// </summary>
    public string RiskTone => Risk switch
    {
        ItemRisk.Dangerous => "danger",
        ItemRisk.Caution => "caution",
        _ => "safe"
    };

    public ItemRisk Risk => Item.Risk;

    public CleanActionKind ActionKind => Item.ActionKind;

    public string ActionNote => Item.ActionNote;

    /// <summary>人话后果说明（需求 5.2-3：不许写"可能影响系统稳定性"这类空话）。</summary>
    public string SideEffect => Item.SideEffect;

    /// <summary>恢复方式；L3 必须非空并在界面上就地显示（需求 5.4-4）。</summary>
    public string RestoreHint => Item.RestoreHint;

    public string ActionText => Item.ActionText;

    /// <summary>可还原 = 走隔离区；命令型动作与仅展示项不可还原（复核报告口径一致）。</summary>
    public bool IsRestorable => ActionKind == CleanActionKind.Quarantine;

    public string RestorableText => IsRestorable ? "可从隔离区还原（保留期内）" : "不可还原";

    public int FileCount => Item.Files.Count;

    public long TotalBytes => Item.TotalBytes;

    public string SizeText => VolumeTextFormatter.FormatBytes(TotalBytes);

    public string FileCountText => $"{FileCount} 个文件";

    /// <summary>路径样本：前两条 + "其余 N 个见清单 csv"（需求 3.3-2 要求看得见路径）。</summary>
    public string PathsSample
    {
        get
        {
            if (Item.Files.Count == 0)
            {
                return "本项没有文件目标（命令行动作或仅展示）";
            }

            var head = string.Join("、", Item.Files.Take(2).Select(f => f.Path));
            return Item.Files.Count > 2 ? $"{head} 等 {Item.Files.Count} 个位置" : head;
        }
    }

    /// <summary>
    /// 保留不处理的文件（例如最近一次蓝屏转储）。
    /// 需求 7.4 要求清单里明确写出"保留：xxx"，界面同样要看得见。
    /// </summary>
    public string KeptText => Item.Kept.Count == 0
        ? string.Empty
        : "保留：" + string.Join("、", Item.Kept.Select(k => $"{k.Path}（{VolumeTextFormatter.FormatBytes(k.Size)}）"));

    public bool HasKept => Item.Kept.Count > 0;

    /// <summary>不可用/需要额外说明的提示（例如 Windows.old 的 10 天窗口）。</summary>
    public string? Note => string.IsNullOrWhiteSpace(Item.UnavailableReason) ? null : Item.UnavailableReason;

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    /// <summary>
    /// L1 没有勾选框（它随"一键清理"执行，需求 2.2）；
    /// **信息项（页面文件）也没有勾选框**——需求 2.4 明确它是"仅展示、不可清理"，
    /// 给它一个勾选框会让用户以为能清、并且把它算进"本次可处理"的体积里（对抗式评审 F-14）；
    /// 其余 L2 / L3 / 回收站必须可勾选（需求 3.3-1）。
    /// </summary>
    public bool ShowCheckBox => Category != CleanCategory.L1OneClick && !IsInformationalOnly;

    /// <summary>命令型动作（休眠 / DISM）：不是搬文件，界面上要能区分。</summary>
    public bool IsCommandAction => ActionKind is CleanActionKind.HibernateOff or CleanActionKind.DismComponentCleanup;

    /// <summary>仅展示项（页面文件）：明确声明"不可清理"，界面据此说明而不是假装能清。</summary>
    public string ExecutionText => IsInformationalOnly
        ? "本项只展示，不会执行任何清理动作"
        : IsCommandAction
            ? "本项执行的是系统命令，不是搬文件"
            : "本项按清单移入隔离区（保留期内可还原）";

    public bool IsInformationalOnly => ActionKind == CleanActionKind.InformationalOnly;

    /// <summary>勾选状态。L3 / 回收站等风险项默认不勾（保守原则，需求 5.4-1）。</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            // 没有勾选框的项（L1、以及"只展示不可清理"的信息项）不允许被勾选：
            // 否则它会悄悄计入"已勾选 N 项 / 合计 X"，让确认框承诺一个永远不会被清理的体积（F-14）。
            var effective = ShowCheckBox && value;
            if (SetProperty(ref _isChecked, effective))
            {
                OnCheckedChanged?.Invoke();
            }
        }
    }

    /// <summary>每次重新扫描后回到条目自己的默认值（需求 5.4-2：不记忆上次勾选）。</summary>
    public bool DefaultChecked => Item.DefaultChecked;

    /// <summary>回默认，不触发外部回调（重扫时批量重置用）。</summary>
    internal void ResetToDefault()
    {
        _isChecked = Item.DefaultChecked;
        OnPropertyChanged(nameof(IsChecked));
    }

    /// <summary>
    /// 本项是否需要二次确认：L3 一律要（需求 3.3-4），任何分级的不可还原动作同样要（需求 4.3-1）。
    /// **不要求已勾选**——判断"这一项本身危不危险"与当前勾选无关，是否真的执行由调用方筛。
    /// </summary>
    public bool RequiresConfirmation =>
        Category == CleanCategory.L3Cautious || !IsRestorable;

    /// <summary>本项是否必须走休眠授权闸门（需求 3.8）。</summary>
    public bool RequiresHibernateAuthorization =>
        ActionKind == CleanActionKind.HibernateOff;
}
