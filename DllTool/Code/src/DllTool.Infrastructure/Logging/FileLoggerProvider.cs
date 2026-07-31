using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;

namespace DllTool.Infrastructure.Logging;

/// <summary>
/// 基于 <see cref="ILoggerProvider"/> 的异步文件日志提供程序。
/// 日志写入 D:\logs\&lt;工程名&gt;\ 目录，按日期切分文件，带滚动保留。
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    /// <summary>日志存储目录（完整路径）。</summary>
    public string LogDirectory { get; }

    private readonly int _retainDays;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);
    private readonly object _syncRoot = new();
    private string _currentDate;
    private string _currentFile;
    private StreamWriter? _writer;

    public FileLoggerProvider(string logDirectory, int retainDays = 30)
    {
        LogDirectory = logDirectory;
        _retainDays = retainDays;
        _currentDate = DateTime.Now.ToString("yyyy-MM-dd");
        _currentFile = Path.Combine(LogDirectory, $"log_{_currentDate}.txt");
        Directory.CreateDirectory(LogDirectory);
        PruneOldLogs();
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(this, name));

    /// <summary>写入一行日志。线程安全；自动按日期滚动文件。日志失败不向上抛出，避免拖垮主流程。</summary>
    public void Write(string category, LogLevel level, string message)
    {
        try
        {
            lock (_syncRoot)
            {
                string date = DateTime.Now.ToString("yyyy-MM-dd");
                if (!string.Equals(date, _currentDate, StringComparison.Ordinal))
                {
                    _writer?.Flush();
                    _writer = null;
                    _currentFile = Path.Combine(LogDirectory, $"log_{date}.txt");
                    _currentDate = date;
                }

                _writer ??= CreateWriter(_currentFile);
                _writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level.ToString().ToUpperInvariant()}] [{category}] {message}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or ObjectDisposedException)
        {
            // 日志写入失败时静默降级，绝不中断业务。
        }
    }

    /// <summary>
    /// 以共享读写方式打开日志文件，允许其他进程（如多实例运行）同时追加。
    /// </summary>
    private static StreamWriter CreateWriter(string filePath)
    {
        var stream = new FileStream(
            filePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.SequentialScan);

        return new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
    }

    /// <summary>将缓冲区内容刷入磁盘。</summary>
    public void Flush()
    {
        try
        {
            lock (_syncRoot)
            {
                _writer?.Flush();
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }
    }

    private void PruneOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-_retainDays);
            foreach (var file in Directory.EnumerateFiles(LogDirectory, "log_*.txt"))
            {
                var info = new FileInfo(file);
                if (info.LastWriteTime < cutoff)
                {
                    try
                    {
                        info.Delete();
                    }
                    catch (IOException)
                    {
                        // 文件被占用时跳过清理。
                    }
                }
            }
        }
        catch (IOException)
        {
            // 日志目录不可用时静默忽略清理。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        try
        {
            lock (_syncRoot)
            {
                _writer?.Flush();
                _writer?.Dispose();
                _writer = null;
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }
        _loggers.Clear();
        GC.SuppressFinalize(this);
    }
}
