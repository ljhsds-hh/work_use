namespace SpaceMaid.Core.Models;

/// <summary>计划移入隔离区的单个文件。</summary>
public sealed record PlannedFile(
    string ItemId,
    CleanCategory Category,
    string OriginalPath,
    long SizeBytes,
    DateTimeOffset LastWrite);

/// <summary>一批隔离（一次清理 = 一批）。</summary>
public sealed record QuarantineBatch(
    string BatchId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string QuarantineRoot,
    string SourceVolume,
    bool SameVolumeAsSource);

/// <summary>映射表条目的状态（断电恢复靠它）。</summary>
public enum MapEntryStatus
{
    /// <summary>已登记但尚未移动完成（半残状态）。</summary>
    Pending,

    /// <summary>已安全移动进隔离区。</summary>
    Stored,

    /// <summary>已还原回原位。</summary>
    Restored,

    /// <summary>状态不明：既不在原位也不在 payload（复核报告须列为异常项）。</summary>
    Unknown
}

/// <summary>
/// 映射表条目：还原的唯一依据（需求 3.4-4）。
/// </summary>
public sealed record QuarantineMapEntry
{
    public required string ItemId { get; init; }
    public required CleanCategory Category { get; init; }
    public required string OriginalPath { get; init; }

    /// <summary>原文件所在卷（例如 "C:\"）。用于还原前判断"原卷是否还在"。</summary>
    public required string SourceVolume { get; init; }

    public required long SizeBytes { get; init; }
    public required DateTimeOffset LastWrite { get; init; }

    /// <summary>相对隔离批次目录的存放路径，例如 "payload\0001_a.tmp"。</summary>
    public required string StoredAs { get; init; }

    public MapEntryStatus Status { get; init; } = MapEntryStatus.Pending;

    public string? Note { get; init; }
}

/// <summary>一个批次的映射表（map.json）。</summary>
public sealed record QuarantineMap
{
    public int SchemaVersion { get; init; } = 1;
    public required string BatchId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required string QuarantineRoot { get; init; }
    public bool SameVolumeAsSource { get; init; }
    public bool Pending { get; init; }
    public IReadOnlyList<QuarantineMapEntry> Entries { get; init; } = Array.Empty<QuarantineMapEntry>();
}

/// <summary>单批摘要。</summary>
public sealed record BatchInfo(
    string BatchId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    long TotalBytes,
    int EntryCount,
    bool Expired);

/// <summary>
/// 一次隔离动作的汇总结果。失败的文件**不进映射表**，只出现在 <see cref="Skipped"/> 里，
/// 且源文件必须保持原样（需求 3.4-3：失败即保留源文件并记"未清理成功"）。
/// </summary>
public sealed record BatchStoreResult(
    string BatchId,
    string BatchDirectory,
    int StoredCount,
    long StoredBytes,
    IReadOnlyList<SkippedFile> Skipped,
    IReadOnlyList<QuarantineMapEntry> StoredEntries,
    bool Cancelled);

/// <summary>隔离区总体情况。</summary>
public sealed record QuarantineInfo(
    IReadOnlyList<BatchInfo> Batches,
    long TotalBytes,
    int ExpiredBatchCount);

/// <summary>落地结果（单个文件或单项）。失败不抛异常，只用 Reason 表达。</summary>
public sealed record StoreResult(bool Ok, string? Reason, long MovedBytes)
{
    public static StoreResult Success(long bytes) => new(true, null, bytes);
    public static StoreResult Failure(string reason) => new(false, reason, 0);
}

/// <summary>还原选项。覆盖默认关闭（需求 3.4-6：不静默覆盖）。</summary>
public sealed record RestoreOptions(bool Overwrite = false);

/// <summary>释放（到期自动释放 / 立即清空隔离区）的结果。</summary>
public sealed record ReleaseResult(
    int ReleasedBatches,
    int ReleasedFiles,
    long ReleasedBytes,
    IReadOnlyList<string> Notes);

public sealed record RestoreConflict(string OriginalPath, string Reason);

public sealed record RestoreFailure(string OriginalPath, string Reason);

public sealed record RestoreResult(
    int RestoredCount,
    long RestoredBytes,
    IReadOnlyList<RestoreConflict> Conflicts,
    IReadOnlyList<RestoreFailure> Failures);

/// <summary>启动自检（断电恢复）结果。</summary>
public sealed record RecoverReport(
    int StoredRecovered,
    int MarkedUnknown,
    int PendingCleared,
    IReadOnlyList<string> Notes);

/// <summary>隔离进度。</summary>
public sealed record CleanProgress(
    string ItemId,
    string DisplayName,
    int ProcessedFiles,
    int TotalFiles,
    long ProcessedBytes);
