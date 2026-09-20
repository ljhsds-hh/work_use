using SpaceMaid.Core.Abstractions;

namespace SpaceMaid.Core.Logging;

/// <summary>什么都不做的日志实现：供单测与"日志目录不可用"的降级路径使用。</summary>
public sealed class SilentLogSink : ILogSink
{
    public static readonly SilentLogSink Instance = new();

    private SilentLogSink()
    {
    }

    public void Info(string message)
    {
    }

    public void Warn(string message)
    {
    }

    public void Error(string message, Exception? exception = null)
    {
    }
}
