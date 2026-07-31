using Microsoft.Extensions.Logging;

namespace DllTool.Infrastructure.Logging;

/// <summary>
/// 将日志写入 <see cref="FileLoggerProvider"/> 的日志记录器实现。
/// </summary>
public sealed class FileLogger : ILogger
{
    private readonly FileLoggerProvider _provider;
    private readonly string _category;

    public FileLogger(FileLoggerProvider provider, string category)
    {
        _provider = provider;
        _category = category;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        string message = formatter(state, exception);
        if (exception is not null)
        {
            message += Environment.NewLine + exception;
        }

        _provider.Write(_category, logLevel, message);
    }
}
