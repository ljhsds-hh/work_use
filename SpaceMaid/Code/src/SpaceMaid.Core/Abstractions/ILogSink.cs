namespace SpaceMaid.Core.Abstractions;

/// <summary>
/// 日志汇（只记录，不抛异常）。
/// 为什么需要抽象：日志可能写到不可写目录（U 盘拔出、权限不足），
/// 此时必须静默降级为 no-op，绝不能因为"记不了日志"而打断清理流程（设计文档 §10）。
/// </summary>
public interface ILogSink
{
    /// <summary>记录普通信息。</summary>
    void Info(string message);

    /// <summary>记录警告（会影响结果但可继续）。</summary>
    void Warn(string message);

    /// <summary>记录错误；可附异常，实现需自行提取异常类型与消息。</summary>
    void Error(string message, Exception? exception = null);
}
