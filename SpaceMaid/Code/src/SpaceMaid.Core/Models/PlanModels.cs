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

    public int PlannedFileCount => Items.Sum(i => i.Files.Count);

    public long PlannedBytes => Items.Sum(i => i.TotalBytes);
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
