namespace DllTool.Core.Models;

/// <summary>
/// 一次完整操作（备份，含可选覆盖）的汇总结果。
/// </summary>
public sealed class OperationResult
{
    /// <summary>全部条目记录（备份、覆盖、失败、跳过均在此）。</summary>
    public List<OperationEntry> Entries { get; } = [];

    /// <summary>备份阶段成功复制的文件数量。</summary>
    public int BackupSucceeded => Entries.Count(e => e.Action == OperationActions.Backup && e.Status == OperationStatus.Success);

    /// <summary>备份阶段失败的文件数量。</summary>
    public int BackupFailed => Entries.Count(e => e.Action == OperationActions.Backup && e.Status == OperationStatus.Failed);

    /// <summary>覆盖阶段成功覆盖的文件数量。</summary>
    public int OverwriteSucceeded => Entries.Count(e => e.Action == OperationActions.Overwrite && e.Status == OperationStatus.Success);

    /// <summary>覆盖阶段失败的文件数量。</summary>
    public int OverwriteFailed => Entries.Count(e => e.Action == OperationActions.Overwrite && e.Status == OperationStatus.Failed);

    /// <summary>未匹配（跳过）的清单条目数量。</summary>
    public int SkippedCount => Entries.Count(e => e.Status == OperationStatus.Skipped);

    /// <summary>本次专属备份目录的完整路径（未生成时为 null）。</summary>
    public string? BackupDirectory { get; set; }

    /// <summary>备份清单文件的完整路径（未生成时为 null）。</summary>
    public string? ManifestPath { get; set; }

    /// <summary>是否执行了覆盖阶段。</summary>
    public bool OverwriteExecuted { get; set; }

    /// <summary>当前模式名称（用于界面标题展示）。</summary>
    public required string ModeName { get; init; }

    public IEnumerable<OperationEntry> FailedOverwrites => Entries.Where(e => e.Action == OperationActions.Overwrite && e.Status == OperationStatus.Failed);

    public IEnumerable<OperationEntry> FailedBackups => Entries.Where(e => e.Action == OperationActions.Backup && e.Status == OperationStatus.Failed);
}

/// <summary>操作类型常量。</summary>
public static class OperationActions
{
    public const string Backup = "备份";
    public const string Overwrite = "覆盖";
}
