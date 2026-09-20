namespace SpaceMaid.Core.Models;

/// <summary>
/// 清单条目：清单是执行的唯一输入（设计决策 D-7 / 需求 3.9）。
/// </summary>
public sealed record CleanPlanItem(
    string ItemId,
    CleanCategory Category,
    string DisplayName,
    string ActionText,
    IReadOnlyList<ScanFile> Files,
    long TotalBytes,
    bool DefaultChecked,
    bool UserChecked)
{
    public string ActionNote { get; init; } = string.Empty;

    public string RestoreHint { get; init; } = string.Empty;

    public string SideEffect { get; init; } = string.Empty;

    public ItemRisk Risk { get; init; }

    public CleanActionKind ActionKind { get; init; }

    /// <summary>保留不处理的文件（例如最近一次蓝屏转储），用于清单里"保留："一行。</summary>
    public IReadOnlyList<ScanFile> Kept { get; init; } = Array.Empty<ScanFile>();

    public string? UnavailableReason { get; init; }
}

/// <summary>清理计划（清单的数据形态）。</summary>
public sealed record CleanPlan(
    string PlanId,
    DateTimeOffset CreatedAt,
    ScanReport Scan,
    IReadOnlyList<CleanPlanItem> Items,
    long QuarantineRequiredBytes,
    bool SameVolumeAsSource)
{
    public IEnumerable<CleanPlanItem> Checked => Items.Where(i => i.UserChecked);

    /// <summary>
    /// 会真正执行动作的条目。信息项（页面文件，需求 2.4）**永远不执行**——`CleanExecutor` 直接跳过它、
    /// 界面上连勾选框都没有、`Denylist` 也硬拦着 `pagefile.sys`——因此它不属于任何"将处理"的口径。
    /// </summary>
    public IEnumerable<CleanPlanItem> Actionable =>
        Items.Where(i => i.ActionKind != CleanActionKind.InformationalOnly);

    /// <summary>只展示、不执行的条目（页面文件）。</summary>
    public IEnumerable<CleanPlanItem> Informational =>
        Items.Where(i => i.ActionKind == CleanActionKind.InformationalOnly);

    /// <summary>本次计划处理的文件数（不含信息项）。</summary>
    public int PlannedFileCount => Actionable.Sum(i => i.Files.Count);

    /// <summary>本次计划处理的体积之和（不含信息项）。</summary>
    public long PlannedBytes => Actionable.Sum(i => i.TotalBytes);

    /// <summary>
    /// 清单里出现的文件行数（**含**信息项那一行），与导出的 csv 数据行数一一对应。
    /// 清单完整性/复核基准比对必须用它；用 <see cref="PlannedFileCount"/> 会因为信息项永远差一行
    /// （真机清单：39134 行 = 39133 个可处理 + 1 个页面文件）。
    /// </summary>
    public int ManifestFileCount => Items.Sum(i => i.Files.Count);

    /// <summary>仅展示、不执行的体积之和。单独说明给用户看，**不并入** <see cref="PlannedBytes"/>。</summary>
    public long InformationalBytes => Informational.Sum(i => i.TotalBytes);
}

/// <summary>执行选项。</summary>
public sealed record ExecutionOptions(
    string QuarantinePath,
    int RetentionDays,
    bool QuarantineOnly = true)
{
    /// <summary>隔离区实际存储根：&lt;用户选择的目录&gt;\SpaceMaid\Quarantine（需求 3.4-2 不污染用户目录结构）。</summary>
    public string QuarantineRoot =>
        System.IO.Path.Combine(QuarantinePath, "SpaceMaid", "Quarantine");

    /// <summary>
    /// 用户是否已通过休眠项的**授权闸门**（需求 3.8）。
    /// 默认 false：没有显式授权时，休眠项一律不执行——本字段不由任何配置文件驱动。
    /// </summary>
    public bool AuthorizeHibernate { get; init; }
}

/// <summary>单项执行结果。</summary>
public sealed record ItemExecutionResult(
    string ItemId,
    string DisplayName,
    CleanActionKind ActionKind,
    int MovedCount,
    long MovedBytes,
    int SkippedCount,
    string? Note);

/// <summary>被跳过的文件与原因（不得静默忽略）。</summary>
public sealed record SkippedFile(string ItemId, string Path, string Reason);

/// <summary>执行报告。释放口径分两种：跨卷=已释放，同卷=已移入隔离区（设计决策 D-6）。</summary>
public sealed record ExecutionReport(
    string PlanId,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyList<ItemExecutionResult> Items,
    long MovedBytes,
    bool SameVolume,
    IReadOnlyList<SkippedFile> Skipped,
    string? BatchId = null)
{
    public int MovedFileCount => Items.Sum(i => i.MovedCount);

    public int SkippedFileCount => Skipped.Count;
}
