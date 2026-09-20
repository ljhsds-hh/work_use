namespace SpaceMaid.Core.Models;

/// <summary>扫描请求。</summary>
public sealed record ScanRequest(IReadOnlyList<CleanItemDefinition> Items, bool IncludeRecycleBin);

/// <summary>扫描进度（界面进度条用）。</summary>
public sealed record ScanProgress(string ItemId, string DisplayName, long BytesSoFar, int FilesSoFar);

/// <summary>扫描到的单个文件。</summary>
public sealed record ScanFile(string Path, long Size, DateTimeOffset LastWrite, CleanActionKind Action);

/// <summary>单个清理项的扫描结果。</summary>
public sealed record ScanEntry(
    CleanItemDefinition Item,
    long TotalBytes,
    int FileCount,
    int SkippedCount,
    IReadOnlyList<ScanFile> Files,
    bool Available,
    string? UnavailableReason)
{
    public bool HasContent => FileCount > 0;
}

/// <summary>卷快照（总容量 / 可用空间）。</summary>
public sealed record VolumeSnapshot(string Drive, long TotalBytes, long FreeBytes);

/// <summary>扫描报告（只读结果，不含任何写操作）。</summary>
public sealed record ScanReport(
    IReadOnlyList<ScanEntry> Entries,
    VolumeSnapshot Volume,
    DateTimeOffset ScannedAt)
{
    public long TotalBytes => Entries.Sum(e => e.TotalBytes);

    public long TotalFiles => Entries.Sum(e => (long)e.FileCount);

    public ScanEntry? Find(string itemId) => Entries.FirstOrDefault(e => e.Item.Id == itemId);
}
