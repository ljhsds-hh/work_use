using SpaceMaid.Core.Models;
using SpaceMaid.Core.Reporting;
using SpaceMaid.Core.Scanning;

namespace SpaceMaid.Core.Cli;

/// <summary>命令行执行结果。</summary>
/// <param name="ExitCode">0 = 成功；1 = 执行失败；2 = 参数用法错误。</param>
/// <param name="Message">可直接打印/展示的中文摘要。</param>
/// <param name="Artifacts">本次产出的文件路径（清单、复核报告）。</param>
public sealed record CliResult(int ExitCode, string Message, IReadOnlyList<string> Artifacts);

/// <summary>
/// 只读命令行执行器（需求 9.1）。
///
/// 它能做的两件事都**不会删除任何文件**：
/// 1. <c>--dry-run</c>：扫描 + 导出清单（md + csv）；
/// 2. <c>--report</c>：读回清单 + 重新扫描现状 + 输出复核报告。
///
/// 删除动作不在本类的能力范围内——它只在界面里由用户确认后触发（不变量 I-6）。
/// </summary>
public sealed class CliRunner
{
    private readonly CoreServices _services;

    public CliRunner(CoreServices services) => _services = services;

    /// <summary>执行命令行请求。</summary>
    public CliResult Run(CliOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Mode switch
        {
            CliMode.Help => new CliResult(0, CliOptions.HelpText, Array.Empty<string>()),
            CliMode.DryRun => DryRun(options, cancellationToken),
            CliMode.Report => Report(options, cancellationToken),
            _ => new CliResult(2, string.IsNullOrEmpty(options.Message) ? "没有可执行的命令，用 --help 查看用法" : options.Message, Array.Empty<string>())
        };
    }

    private CliResult DryRun(CliOptions options, CancellationToken cancellationToken)
    {
        // 只读模式：不做任何删除类维护（账本自检/到期释放/日志滚动都由界面启动时负责）
        var preparation = _services.Prepare();

        // 回收站也要进清单（需求 3.9-1 要求按 L1/L2/L3/回收站分组）：
        // 是否含"非系统盘的回收站"由设置决定，这里只决定清单里有没有"回收站"这一组。
        var request = new ScanRequest(_services.Catalog, IncludeRecycleBin: true);
        var scan = _services.Scanner.ScanAsync(request, null, cancellationToken).GetAwaiter().GetResult();
        var plan = _services.BuildPlan(scan);

        var outputRoot = string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? _services.Settings.ReportDirectory
            : options.OutputDirectory;

        ManifestPaths paths;
        try
        {
            paths = _services.Manifest.Write(plan, outputRoot);
        }
        catch (Exception ex) when (!string.IsNullOrWhiteSpace(outputRoot))
        {
            // 目录不可写时的回退（设计文档 8.2）：换到 %LocalAppData%，别让只读命令直接抛异常
            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SpaceMaid",
                "reports");
            _services.Log.Warn($"清单目录不可写（{outputRoot}：{ex.Message}），已回退到 {fallback}");

            try
            {
                paths = _services.Manifest.Write(plan, fallback);
            }
            catch (Exception fallbackError)
            {
                return new CliResult(1, $"清单导出失败：{ex.Message}；回退目录也失败：{fallbackError.Message}", Array.Empty<string>());
            }
        }

        var message = string.Join(Environment.NewLine, new[]
        {
            $"扫描完成：{scan.Entries.Count(e => e.Available)} 个可用清理项，共 {plan.PlannedFileCount} 个文件，{VolumeTextFormatter.FormatBytes(plan.PlannedBytes)}",
            $"清单已导出（只读，未删除任何文件）：",
            $"  {paths.MarkdownPath}",
            $"  {paths.CsvPath}",
            plan.PlannedFileCount == 0
                ? "提示：本次没有发现可处理的内容。"
                : "下一步：打开清单人工审阅；执行清理必须在界面里由你确认后操作。",
            preparation.QuarantineUsable ? string.Empty : $"注意：隔离区当前不可用 —— {preparation.QuarantineMessage}"
        }.Where(line => line.Length > 0));

        return new CliResult(0, message, new[] { paths.MarkdownPath, paths.CsvPath });
    }

    private CliResult Report(CliOptions options, CancellationToken cancellationToken)
    {
        var manifestDirectory = options.ManifestDirectory ?? string.Empty;
        var index = ManifestReader.TryRead(manifestDirectory, out var error);
        if (index is null)
        {
            return new CliResult(1, error, Array.Empty<string>());
        }

        var maps = _services.Quarantine.ReadMaps(_services.QuarantineRoot);
        var systemVolume = _services.Volumes.GetVolumeOf(_services.Environment.SystemDrive + Path.DirectorySeparatorChar);
        var quarantinVolume = _services.Volumes.GetVolumeOf(_services.QuarantineRoot);
        var sameVolume = systemVolume.Equals(quarantinVolume, StringComparison.OrdinalIgnoreCase);

        // 复核报告里的"实际效果"不能永远是 0 B（对抗式评审 F-16）：
        // 按"清单里已经不在原位的文件"求和，才与执行前的清单形成差值对照（需求 3.9-4）。
        var releasedBytes = index.Rows
            .Where(row => !_services.FileSystem.FileExists(row.OriginalPath))
            .Sum(row => row.SizeBytes);

        var report = _services.Reviewer.Review(index, maps, manifestDirectory, releasedBytes, sameVolume);

        var message = string.Join(Environment.NewLine, new[]
        {
            $"复核完成：清单 {index.Rows.Count} 条 —— 已清理 {report.CleanedCount}，未清理 {report.SkippedCount}，异常 {report.UnknownCount}",
            $"实际效果：{VolumeTextFormatter.DescribeProcessed(releasedBytes, sameVolume)}",
            report.UnknownCount > 0 ? "存在异常项，请查看复核报告逐条确认（这些项既不在原位、也没有隔离记录）。" : string.Empty,
            $"复核报告：{report.MarkdownPath}"
        }.Where(line => line.Length > 0));

        return new CliResult(0, message, new[] { report.MarkdownPath });
    }
}
