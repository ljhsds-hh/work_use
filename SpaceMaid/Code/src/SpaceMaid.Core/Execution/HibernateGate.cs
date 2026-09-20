using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Logging;
using SpaceMaid.Core.Models;

namespace SpaceMaid.Core.Execution;

/// <summary>休眠功能当前状态。</summary>
public enum HibernateState
{
    Unknown,

    /// <summary>休眠可用（存在 hiberfil.sys）。</summary>
    Enabled,

    /// <summary>休眠已关闭。</summary>
    Disabled,

    /// <summary>系统不支持休眠（powercfg 调用失败）。</summary>
    NotSupported
}

/// <summary>一次闸门执行的结果。</summary>
public sealed record GateResult(bool Executed, string Reason, long FreedBytes);

/// <summary>
/// 休眠授权闸门（需求 3.8，用户强制要求）。
///
/// 三条铁律：
/// 1. **执行方式不是"删文件"，而是"关功能"**：唯一动作是 <c>powercfg /h off</c>，
///    绝不把 <c>hiberfil.sys</c> 当作删除目标（它已被列进禁止清单）；
/// 2. **未授权就不执行**：<see cref="ExecutionOptions.AuthorizeHibernate"/> 为 false 时
///    连一条命令都不会发出（单测断言命令执行次数为 0）；
/// 3. **失败不降级**：命令返回非 0 或校验不到文件消失，只如实报告，绝不改成"直接删文件"。
/// </summary>
public sealed class HibernateGate : ISpecialItemHandler
{
    /// <summary>休眠文件固定路径（只读探测用，永不作为删除目标）。</summary>
    public const string HibernateFileName = "hiberfil.sys";

    /// <summary>关闭休眠的命令与参数（固定，不接受外部拼装）。</summary>
    public const string PowerCfgExecutable = "powercfg";

    /// <summary>关闭休眠的参数：这条命令本身就是"关闭 + 释放文件"的原子动作。</summary>
    public const string DisableArguments = "/h off";

    private readonly ICommandRunner _runner;
    private readonly IFileSystem _fileSystem;
    private readonly IEnvironmentProbe _environment;
    private readonly ILogSink _log;

    public HibernateGate(ICommandRunner runner, IFileSystem fileSystem, IEnvironmentProbe environment, ILogSink? log = null)
    {
        _runner = runner;
        _fileSystem = fileSystem;
        _environment = environment;
        _log = log ?? SilentLogSink.Instance;
    }

    /// <inheritdoc />
    public bool CanHandle(CleanActionKind kind) => kind == CleanActionKind.HibernateOff;

    /// <summary>休眠文件路径（%SystemDrive%\hiberfil.sys）。</summary>
    public string HibernateFilePath =>
        Path.Combine(_environment.SystemDrive + Path.DirectorySeparatorChar, HibernateFileName);

    /// <summary>
    /// 探测休眠状态。用 <c>powercfg /a</c> 判断系统是否支持，用"文件是否存在"判断是否已开启——
    /// 后者与系统语言无关，比解析本地化输出可靠。
    /// </summary>
    public HibernateState Probe()
    {
        var result = _runner.Run(PowerCfgExecutable, "/a", TimeSpan.FromSeconds(30));
        if (result.ExitCode != 0)
        {
            return HibernateState.NotSupported;
        }

        return _fileSystem.FileExists(HibernateFilePath) ? HibernateState.Enabled : HibernateState.Disabled;
    }

    /// <summary>执行闸门。</summary>
    public GateResult Disable(bool userAuthorized, CancellationToken cancellationToken = default)
    {
        if (!userAuthorized)
        {
            // 关键：未授权时**不发出任何命令**
            _log.Info("休眠项：用户未授权关闭休眠，本项跳过");
            return new GateResult(false, "未授权关闭休眠，已跳过", 0);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var sizeBefore = _fileSystem.FileExists(HibernateFilePath) ? _fileSystem.GetFileSize(HibernateFilePath) : 0;

        var result = _runner.Run(PowerCfgExecutable, DisableArguments, TimeSpan.FromMinutes(2));
        if (result.ExitCode != 0)
        {
            var reason = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut.Trim() : result.StdErr.Trim();
            _log.Warn($"关闭休眠失败（exit={result.ExitCode}）：{reason}");
            return new GateResult(false, $"关闭休眠失败（退出码 {result.ExitCode}）：{Truncate(reason)}", 0);
        }

        if (_fileSystem.FileExists(HibernateFilePath))
        {
            _log.Warn("关闭休眠命令返回成功，但 hiberfil.sys 仍然存在");
            return new GateResult(false, "命令已执行，但休眠文件仍然存在，请重启后再试", 0);
        }

        _log.Info($"已关闭休眠，释放 {sizeBefore} 字节");
        return new GateResult(true, "已关闭休眠并释放休眠文件", sizeBefore);
    }

    /// <inheritdoc />
    public ItemExecutionResult Handle(CleanPlanItem item, ExecutionOptions options, CancellationToken cancellationToken)
    {
        var result = Disable(options.AuthorizeHibernate, cancellationToken);

        // 报告里必须能直接看到"怎么恢复"（需求 5.4）
        var note = result.Executed
            ? $"{result.Reason}；恢复方式：powercfg /h on（不影响睡眠与正常关机）"
            : result.Reason;

        return new ItemExecutionResult(
            item.ItemId,
            item.DisplayName,
            CleanActionKind.HibernateOff,
            result.Executed ? 1 : 0,
            result.FreedBytes,
            result.Executed ? 0 : 1,
            note);
    }

    private static string Truncate(string text) =>
        string.IsNullOrEmpty(text) ? "无输出" : (text.Length > 200 ? text[..200] + "…" : text);
}
