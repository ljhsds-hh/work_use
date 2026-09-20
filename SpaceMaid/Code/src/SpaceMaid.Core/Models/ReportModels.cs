namespace SpaceMaid.Core.Models;

/// <summary>清单三件套的落地路径。</summary>
public sealed record ManifestPaths(string Directory, string MarkdownPath, string CsvPath);

/// <summary>清单 csv 的一行（穷尽性：每个将被处理的文件一行）。</summary>
public sealed record ManifestRow(
    string Category,
    string ItemId,
    string ItemName,
    string OriginalPath,
    long SizeBytes,
    DateTimeOffset LastWrite,
    string Action,
    bool Restorable,
    string Note);

/// <summary>执行前清单的索引，供复核比对（执行器与复核共同使用）。</summary>
public sealed record ManifestRowIndex(string Directory, IReadOnlyList<ManifestRow> Rows);

/// <summary>复核状态。</summary>
public enum ReviewStatus
{
    /// <summary>已按计划处理（原位已不存在，或已移入隔离区）。</summary>
    Cleaned,

    /// <summary>未处理/跳过（含原因）。</summary>
    Skipped,

    /// <summary>异常：清单中有，但现状既不在原位也无法确认（必须逐条列出）。</summary>
    Unknown
}

public sealed record ReviewItem(
    string ItemId,
    string OriginalPath,
    ReviewStatus Status,
    string Detail);

/// <summary>复核报告：审核完成的凭据（需求 3.9-4）。</summary>
public sealed record ReviewReport(
    string ManifestDirectory,
    DateTimeOffset ReviewedAt,
    IReadOnlyList<ReviewItem> Items,
    long ReleasedBytes,
    bool SameVolume,
    string MarkdownPath)
{
    public int CleanedCount => Items.Count(i => i.Status == ReviewStatus.Cleaned);

    public int SkippedCount => Items.Count(i => i.Status == ReviewStatus.Skipped);

    public int UnknownCount => Items.Count(i => i.Status == ReviewStatus.Unknown);
}
