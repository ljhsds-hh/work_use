namespace DllTool.Core.Models;

/// <summary>
/// 一次操作中单个文件条目的处理记录。
/// </summary>
public sealed class OperationEntry
{
    /// <summary>文件相对目标目录根目录的相对路径（含目录层级，区分大小写）。</summary>
    public required string RelativePath { get; init; }

    /// <summary>操作类型。</summary>
    public required string Action { get; init; }

    /// <summary>处理结果。</summary>
    public OperationStatus Status { get; set; }

    /// <summary>失败原因描述（成功时为 null）。</summary>
    public string? Reason { get; set; }
}
