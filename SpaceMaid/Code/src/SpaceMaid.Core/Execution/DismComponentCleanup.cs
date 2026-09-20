using System.Globalization;
using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Logging;
using SpaceMaid.Core.Models;

namespace SpaceMaid.Core.Execution;

/// <summary>
/// 组件存储清理（需求 2.4 / 7 章验收）。
///
/// 只走 DISM 官方命令：<c>dism.exe /Online /Cleanup-Image /StartComponentCleanup</c>。
/// **绝不出现重置基线开关**（那会让所有已安装更新都无法卸载回滚），参数是常量、不接受外部拼装；
/// 绝不直接删除 WinSxS 目录。命令耗时可长达十几分钟且不宜中断，超时按失败处理且不重试。
/// </summary>
public sealed class DismComponentCleanup : ISpecialItemHandler
{
    /// <summary>固定可执行文件。</summary>
    public const string DismExecutable = "dism.exe";

    /// <summary>固定参数（不含任何重置基线开关）。</summary>
    public const string Arguments = "/Online /Cleanup-Image /StartComponentCleanup";

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(30);

    private readonly ICommandRunner _runner;
    private readonly ILogSink _log;

    public DismComponentCleanup(ICommandRunner runner, ILogSink? log = null)
    {
        _runner = runner;
        _log = log ?? SilentLogSink.Instance;
    }

    /// <inheritdoc />
    public bool CanHandle(CleanActionKind kind) => kind == CleanActionKind.DismComponentCleanup;

    /// <inheritdoc />
    public ItemExecutionResult Handle(CleanPlanItem item, ExecutionOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = _runner.Run(DismExecutable, Arguments, CommandTimeout);
        if (result.ExitCode != 0)
        {
            var reason = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut.Trim() : result.StdErr.Trim();
            _log.Warn($"组件清理失败（exit={result.ExitCode}）：{reason}");
            return new ItemExecutionResult(
                item.ItemId,
                item.DisplayName,
                CleanActionKind.DismComponentCleanup,
                0,
                0,
                1,
                $"组件清理失败（退出码 {result.ExitCode}）：{Truncate(reason)}");
        }

        var freed = TryParseFreedBytes(result.StdOut);
        var note = freed > 0
            ? "组件清理完成。注意：清理后近期更新的卸载能力可能受限"
            : "组件清理完成；DISM 未给出可解析的释放量，释放体积未知";

        _log.Info($"组件清理完成，解析到的释放量：{freed} 字节");
        return new ItemExecutionResult(
            item.ItemId,
            item.DisplayName,
            CleanActionKind.DismComponentCleanup,
            1,
            freed,
            0,
            note);
    }

    /// <summary>尽力从 DISM 输出里解析释放量（解析不到就返回 0，不猜）。</summary>
    internal static long TryParseFreedBytes(string stdOut)
    {
        if (string.IsNullOrWhiteSpace(stdOut))
        {
            return 0;
        }

        var lines = stdOut.Split('\n');
        foreach (var line in lines)
        {
            var index = 0;
            while (index < line.Length)
            {
                var start = index;
                while (start < line.Length && (char.IsDigit(line[start]) || line[start] == '.' || line[start] == ','))
                {
                    start++;
                }

                if (start >= line.Length)
                {
                    break;
                }

                var numberText = line[index..start];
                if (numberText.Length > 0)
                {
                    var unitIndex = start;
                    while (unitIndex < line.Length && line[unitIndex] == ' ')
                    {
                        unitIndex++;
                    }

                    var unitLength = 0;
                    while (unitIndex + unitLength < line.Length && char.IsLetter(line[unitIndex + unitLength]))
                    {
                        unitLength++;
                    }

                    var unit = unitLength > 0 ? line.Substring(unitIndex, unitLength).ToUpperInvariant() : string.Empty;
                    var multiplier = unit switch
                    {
                        "TB" => 1024L * 1024 * 1024 * 1024,
                        "GB" => 1024L * 1024 * 1024,
                        "MB" => 1024L * 1024,
                        "KB" => 1024L,
                        "B" => 1L,
                        _ => 0L
                    };

                    if (multiplier > 0
                        && double.TryParse(numberText.Replace(",", string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    {
                        return (long)(value * multiplier);
                    }
                }

                index = start + 1;
            }
        }

        return 0;
    }

    private static string Truncate(string text) =>
        string.IsNullOrEmpty(text) ? "无输出" : (text.Length > 200 ? text[..200] + "…" : text);
}
