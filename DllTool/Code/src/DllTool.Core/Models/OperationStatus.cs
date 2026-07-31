namespace DllTool.Core.Models;

/// <summary>
/// 单个文件条目的处理结果状态。
/// </summary>
public enum OperationStatus
{
    /// <summary>操作成功完成。</summary>
    Success,

    /// <summary>操作失败（权限不足、文件占用等）。</summary>
    Failed,

    /// <summary>匹配失败，未进入处理流程（模式B清单中目标目录不存在的条目）。</summary>
    Skipped,

    /// <summary>备份成功，但覆盖未执行（覆盖确认弹窗点击取消）。</summary>
    BackupOnly
}
