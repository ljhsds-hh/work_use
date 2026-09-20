namespace SpaceMaid.Core.Cli;

/// <summary>命令行模式。</summary>
public enum CliMode
{
    /// <summary>没有命令行参数：正常启动界面。</summary>
    None,

    /// <summary>只做扫描并导出清单（只读，绝不删除）。</summary>
    DryRun,

    /// <summary>对已有清单目录做执行后复核，输出复核报告。</summary>
    Report,

    /// <summary>打印帮助。</summary>
    Help
}

/// <summary>
/// 命令行参数解析（需求 9.1：只读 CLI，用于批量复核）。
///
/// **设计上就不存在"命令行删除"这种东西**（不变量 I-6 / 需求 3.9）：
/// 这里不识别任何触发清理的开关，遇到 <c>--clean</c> / <c>--force</c> / <c>--yes</c> 之类一律报错，
/// 并明确告诉用户删除动作只能在界面里经确认后执行。
/// </summary>
public sealed record CliOptions(CliMode Mode, string? OutputDirectory, string? ManifestDirectory, string Message)
{
    /// <summary>
    /// 是否是"用法错误"（识别不了的参数、缺值、或用了明确禁止的开关）。
    ///
    /// 为什么要单独一个属性：`Mode == None` 有两种含义——"没给参数，正常开界面"和"参数不合法"。
    /// 调用方（App 启动分支）必须能区分：**带了参数却不合法时绝不能静默回落到开界面**，
    /// 否则一次手滑的 `--clean` 会变成"打开工具并顺带做启动维护（删到期的隔离批次）"。
    /// </summary>
    public bool IsUsageError => Mode == CliMode.None && !string.IsNullOrEmpty(Message);

    /// <summary>本次是否是一次命令行调用（给了参数就算）。</summary>
    public static bool IsCommandLineInvocation(IReadOnlyList<string>? args) => args is { Count: > 0 };

    /// <summary>本工具**刻意不提供**的命令行开关（出现即拒绝，并给出解释）。</summary>
    public static readonly IReadOnlyList<string> ForbiddenSwitches = new[]
    {
        "--clean", "--delete", "--remove", "--execute", "--force", "--yes", "-y", "--all", "--silent"
    };

    /// <summary>帮助文本。</summary>
    public static string HelpText =>
        """
        SpaceMaid C 盘空间管家

        用法：
          SpaceMaid                      打开界面（清理动作必须在这里经你确认后执行）
          SpaceMaid --dry-run [--out 目录]   只扫描并导出清单（md + csv），不做任何删除
          SpaceMaid --report 清单目录       对已有清单做执行后复核，输出复核报告
          SpaceMaid --help                  显示本帮助

        说明：
          * 本工具**不提供**任何命令行删除能力；清理永远由你在界面里确认后执行。
          * --out 缺省时使用设置里的报告目录（默认 D:\logs\SpaceMaid\清单）。
        """;

    /// <summary>
    /// 解析参数。失败时返回 <see cref="CliMode.None"/> 并把原因写进 <c>Message</c>（不抛异常）。
    /// </summary>
    public static CliOptions Parse(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0)
        {
            return new CliOptions(CliMode.None, null, null, string.Empty);
        }

        string? outputDirectory = null;
        string? manifestDirectory = null;
        var mode = CliMode.None;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];

            switch (arg.ToLowerInvariant())
            {
                case "--help":
                case "-h":
                case "/?":
                    return new CliOptions(CliMode.Help, null, null, HelpText);

                case "--dry-run":
                    if (mode == CliMode.Report)
                    {
                        return Reject("--dry-run 与 --report 不能同时使用");
                    }

                    mode = CliMode.DryRun;
                    break;

                case "--report":
                    if (mode == CliMode.DryRun)
                    {
                        return Reject("--dry-run 与 --report 不能同时使用");
                    }

                    if (++i >= args.Count)
                    {
                        return Reject("--report 需要指定清单目录");
                    }

                    manifestDirectory = args[i];
                    mode = CliMode.Report;
                    break;

                case "--out":
                    if (++i >= args.Count)
                    {
                        return Reject("--out 需要指定输出目录");
                    }

                    outputDirectory = args[i];
                    break;

                default:
                    if (ForbiddenSwitches.Contains(arg, StringComparer.OrdinalIgnoreCase))
                    {
                        return Reject($"本工具不提供命令行清理能力（{arg}）：清理动作必须由你在界面里确认后执行");
                    }

                    return Reject($"无法识别的参数：{arg}");
            }
        }

        return new CliOptions(mode, outputDirectory, manifestDirectory, string.Empty);
    }

    private static CliOptions Reject(string message) => new(CliMode.None, null, null, message);
}
