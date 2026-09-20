namespace SpaceMaid.Core.Abstractions;

/// <summary>
/// 可注入的时钟。
/// 为什么需要抽象：惰性释放（Task 8，7 天到期）、日志滚动、Windows.old 10 天窗口
/// 都依赖"现在几点"，用假时钟才能写出确定性的到期/未到期用例（设计文档 §11）。
/// </summary>
public interface IClock
{
    /// <summary>当前本地时间（带时区偏移，便于写入映射表 map.json 的 ISO 文本）。</summary>
    DateTimeOffset Now { get; }
}
