namespace CreateTpl.Models;

/// <summary>单个工程的创建结果状态（需求 3.6 结果反馈）。</summary>
public enum CreateStatus
{
    /// <summary>创建成功。</summary>
    Success,

    /// <summary>工程已存在，跳过未覆盖。</summary>
    Skipped,

    /// <summary>创建失败（含原因）。</summary>
    Failed
}
