using SpaceMaid.Core.Abstractions;

namespace SpaceMaid.Core.Platform;

/// <summary>
/// <see cref="IClock"/> 的生产实现：直接读系统本地时间。
/// 为什么用本地时间而非 UTC：界面与日志要给用户看"本地几点"，映射表用带偏移的 ISO 文本
/// （如 <c>2026-09-20T14:30:12.118+08:00</c>）即可无损还原（设计文档 §6.2）。
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset Now => DateTimeOffset.Now;
}
