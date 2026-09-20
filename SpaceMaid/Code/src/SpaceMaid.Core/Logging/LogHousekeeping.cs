using System.Text.RegularExpressions;
using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Logging;
using SpaceMaid.Core.Settings;

namespace SpaceMaid.Core.Logging;

/// <summary>
/// 日志滚动（需求 6 章）。**惰性执行**：只在启动时调用一次，不依赖任何后台任务或计划任务。
/// </summary>
public static partial class LogHousekeeping
{
    /// <summary>日志文件名形如 <c>spacemaid-20260920.log</c>。</summary>
    [GeneratedRegex(@"^spacemaid-(\d{8})\.log$", RegexOptions.IgnoreCase)]
    private static partial Regex LogFilePattern();

    /// <summary>
    /// 删除超过保留天数的日志文件，返回删除数量。
    /// 解析不出日期的文件一律**不动**（可能是用户自己放进去的东西）。
    /// </summary>
    public static int PruneOldLogs(
        string logDirectory,
        int retentionDays,
        IClock clock,
        IFileSystem fileSystem,
        ILogSink? log = null)
    {
        var sink = log ?? SilentLogSink.Instance;

        if (string.IsNullOrWhiteSpace(logDirectory) || !fileSystem.DirectoryExists(logDirectory))
        {
            return 0;
        }

        var cutoff = clock.Now.Date.AddDays(-Math.Max(1, retentionDays));
        var removed = 0;

        foreach (var file in fileSystem.EnumerateFiles(logDirectory, "spacemaid-*.log", recurse: false))
        {
            var match = LogFilePattern().Match(Path.GetFileName(file));
            if (!match.Success)
            {
                continue;
            }

            if (!DateTime.TryParseExact(
                    match.Groups[1].Value,
                    "yyyyMMdd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var logDate))
            {
                continue;
            }

            if (logDate.Date >= cutoff)
            {
                continue;
            }

            if (fileSystem.TryDeleteFile(file, out var error))
            {
                removed++;
            }
            else
            {
                sink.Warn($"清理旧日志失败：{file}（{error}）");
            }
        }

        if (removed > 0)
        {
            sink.Info($"已清理 {removed} 个超过 {retentionDays} 天的日志文件");
        }

        return removed;
    }
}
