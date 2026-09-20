using SpaceMaid.Core.Models;

namespace SpaceMaid.Core.Scanning;

/// <summary>
/// 扫描引擎契约（设计文档 §5）。只读、可中断、可报告进度。
/// </summary>
public interface IScanEngine
{
    Task<ScanReport> ScanAsync(ScanRequest request, IProgress<ScanProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// 回收站扫描器（Task 11 的 <c>RecycleBinTargets</c> 实现）。
/// 单独抽出是为了让扫描引擎不直接依赖注册表/回收站内部结构，便于单测。
/// </summary>
public interface IRecycleBinScanner
{
    /// <summary>枚举回收站中可清理的实体文件（成对识别 $I/$R）。</summary>
    /// <param name="includeOtherDrives">是否包含非系统盘的回收站（需求 2.5：默认仅 C 盘）。</param>
    IReadOnlyList<ScanFile> Scan(bool includeOtherDrives);
}

/// <summary>
/// 卷容量探针（总量 + 可用）。与 <c>IVolumeProbe</c> 分开是因为后者已经冻结为"卷/空间/介质"四件事，
/// 而扫描报告里的"总容量"只在界面展示用，允许缺省（缺省记 0）。
/// </summary>
public interface IVolumeCapacityProbe
{
    /// <summary>返回卷的总字节数；卷不存在时返回 0。</summary>
    long GetTotalBytes(string volume);
}
