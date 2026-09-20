using SpaceMaid.Core.Abstractions;

namespace SpaceMaid.Core.Platform;

/// <summary>
/// 什么都不做的日志汇。
/// 为什么需要它：测试与纯逻辑单测不需要落盘，同时给组合根一个明确的"日志被关闭"实现，
/// 避免到处判空（<c>log?.Info(...)</c>）。
/// </summary>
public sealed class NullLogSink : ILogSink
{
    /// <inheritdoc />
    public void Info(string message)
    {
    }

    /// <inheritdoc />
    public void Warn(string message)
    {
    }

    /// <inheritdoc />
    public void Error(string message, Exception? exception = null)
    {
    }
}
