using System.IO;
using System.Text;

namespace QuietRemind.Services;

public enum LogLevel
{
    Info,
    Warn,
    Error,
}

/// <summary>
/// 全局持久化日志（需求 10 章）：D:\logs\QuietRemind\ 按日分文件，保留最近 30 天，
/// 写失败静默（绝不拖垮主流程）。线程安全。
/// </summary>
public sealed class LogService : IDisposable
{
    private const int RetentionDays = 30;

    private readonly string _dir;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private string? _currentDate;
    private bool _disposed;

    public LogService(string directory)
    {
        _dir = directory;
        Directory.CreateDirectory(_dir);
    }

    public void Info(string message) => Write(LogLevel.Info, message);

    public void Warn(string message) => Write(LogLevel.Warn, message);

    public void Error(string message, Exception? ex = null)
    {
        if (ex is null)
        {
            Write(LogLevel.Error, message);
        }
        else
        {
            Write(LogLevel.Error, $"{message}{Environment.NewLine}{ex}");
        }
    }

    private void Write(LogLevel level, string message)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            lock (_lock)
            {
                var now = DateTime.Now;
                var date = now.ToString("yyyy-MM-dd");
                if (_writer is null || _currentDate != date)
                {
                    EnsureDateChanged(date);
                }
                _writer!.WriteLine($"[{now:HH:mm:ss.fff}] [{level.ToString().ToUpperInvariant()}] {message}");
                _writer.Flush();
            }
        }
        catch
        {
            // 日志写入失败静默吞掉，日志功能不允许拖垮提醒主流程
        }
    }

    private void EnsureDateChanged(string date)
    {
        _currentDate = date;
        _writer?.Dispose();
        var path = Path.Combine(_dir, $"{date}.log");
        _writer = new StreamWriter(path, append: true, Encoding.UTF8);
        CleanupOldLogs();
    }

    /// <summary>滚动清理：删除 30 天前的日志文件（需求 10.3）。</summary>
    private void CleanupOldLogs()
    {
        var cutoff = DateTime.Now.Date.AddDays(-RetentionDays);
        try
        {
            foreach (var file in Directory.EnumerateFiles(_dir, "*.log"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (DateTime.TryParseExact(name, "yyyy-MM-dd", null,
                        System.Globalization.DateTimeStyles.None, out var date)
                    && date < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (IOException)
        {
            // 清理失败不影响主流程
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }
}
