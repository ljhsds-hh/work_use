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
/// 日志文件的跨进程写锁：主实例、守护进程、重复启动实例可能同时追加同一个日志文件，
/// 没有跨进程锁时 StreamWriter 的多次写入会互相截断（实际出现过半行乱码）。
/// Global 命名空间与主程序单实例 Mutex 一致，可在用户会话/计划任务进程间共享。
/// </summary>
internal static class LogFileMutex
{
    private static readonly Mutex? Shared = Create();

    /// <summary>返回 false 表示等待锁超时，本次日志应跳过而不是并发写入。</summary>
    public static bool TryEnter(out bool acquired)
    {
        acquired = false;
        if (Shared is null)
        {
            return true; // 锁不可用时保持可写（单进程场景仍能记日志）
        }
        try
        {
            acquired = Shared.WaitOne(2000);
            return acquired;
        }
        catch (AbandonedMutexException)
        {
            // 上一个持有者异常退出：内核已把所有权交给当前线程
            acquired = true;
            return true;
        }
        catch (Exception)
        {
            // 锁异常不应导致日志功能整体失效
            return true;
        }
    }

    public static void Exit(bool acquired)
    {
        if (!acquired || Shared is null)
        {
            return;
        }
        try
        {
            Shared.ReleaseMutex();
        }
        catch (Exception)
        {
            // 释放失败不影响主流程
        }
    }

    private static Mutex? Create()
    {
        foreach (var name in new[] { @"Global\QuietRemind.Log", @"Local\QuietRemind.Log" })
        {
            try
            {
                return new Mutex(false, name);
            }
            catch (Exception)
            {
                // 该命名空间不可用，尝试下一个
            }
        }
        return null;
    }
}

/// <summary>
/// 全局持久化日志（需求 10 章）：D:\logs\QuietRemind\ 按日分文件，保留最近 30 天，
/// 写失败静默（绝不拖垮主流程）。线程安全。
/// </summary>
public sealed class LogService : IDisposable
{
    private const int RetentionDays = 30;
    private const string StartupErrorPrefix = "startup-error-";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _dir;
    private readonly object _lock = new();
    private string? _currentDate;
    private bool _disposed;

    public LogService(string directory)
    {
        try
        {
            _dir = directory;
            Directory.CreateDirectory(_dir);
        }
        catch (Exception)
        {
            // 主日志目录不可用（如无 D 盘）：回退到用户数据目录下的 logs，日志功能不可静默失效。
            // 回退目录也创建失败时不再抛出：日志失败绝不能阻断提醒主流程（Write 内部会静默吞掉）。
            _dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "QuietRemind", "logs");
            try
            {
                Directory.CreateDirectory(_dir);
            }
            catch (Exception)
            {
                // 两处均不可用时保持路径，写入失败由 Write 静默处理
            }
        }
    }

    /// <summary>实际使用的日志目录（可能已回退到 %AppData%），供诊断界面展示/打开。</summary>
    public string LogDirectory => _dir;

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
                if (_disposed)
                {
                    return;
                }
                // 进程内 _lock 已串行化本进程写入；命名 Mutex 再串行化跨进程写入，
                // 避免两行日志在文件里互相截断
                if (!LogFileMutex.TryEnter(out var acquired))
                {
                    return;
                }
                try
                {
                    var now = DateTime.Now;
                    var date = now.ToString("yyyy-MM-dd");
                    if (_currentDate != date)
                    {
                        _currentDate = date;
                        CleanupOldLogs();
                    }
                    var path = Path.Combine(_dir, $"{date}.log");
                    AppendLine(path, $"[{now:HH:mm:ss.fff}] [{level.ToString().ToUpperInvariant()}] {message}");
                }
                finally
                {
                    LogFileMutex.Exit(acquired);
                }
            }
        }
        catch
        {
            // 日志写入失败静默吞掉，日志功能不允许拖垮提醒主流程
        }
    }

    /// <summary>
    /// 轻量早期日志（次实例在 LogService 创建前退出时使用）。与实例写入共用跨进程锁，
    /// 避免与主实例的日志行互相截断（需求 10.1 的可回溯性）。
    /// </summary>
    public static void AppendEarly(string directory, string message, string level = "INFO")
    {
        try
        {
            Directory.CreateDirectory(directory);
            if (!LogFileMutex.TryEnter(out var acquired))
            {
                return;
            }
            try
            {
                var now = DateTime.Now;
                var path = Path.Combine(directory, $"{now:yyyy-MM-dd}.log");
                AppendLine(path, $"[{now:HH:mm:ss.fff}] [{level}] {message}");
            }
            finally
            {
                LogFileMutex.Exit(acquired);
            }
        }
        catch (Exception)
        {
            // 早期日志失败不影响实例退出
        }
    }

    /// <summary>
    /// 每次写入都重新打开文件并按字节一次写入：避免长期持有的 StreamWriter 缓存位置在
    /// 多进程追加时覆盖其他实例刚写入的内容；UTF8 无 BOM 防止在文件中间插入 BOM。
    /// </summary>
    private static void AppendLine(string path, string line)
    {
        var bytes = Utf8NoBom.GetBytes(line + "\r\n");
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    /// <summary>滚动清理：删除 30 天前的日志文件（需求 10.3），含启动失败日志。</summary>
    private void CleanupOldLogs()
    {
        var cutoff = DateTime.Now.Date.AddDays(-RetentionDays);
        try
        {
            foreach (var file in Directory.EnumerateFiles(_dir, "*.log"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var dateText = name.StartsWith(StartupErrorPrefix, StringComparison.OrdinalIgnoreCase)
                    ? name[StartupErrorPrefix.Length..]
                    : name;
                if (DateTime.TryParseExact(dateText, "yyyy-MM-dd", null,
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
        }
    }
}
