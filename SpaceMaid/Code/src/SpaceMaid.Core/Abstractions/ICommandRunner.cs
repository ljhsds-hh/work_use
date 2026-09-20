namespace SpaceMaid.Core.Abstractions;

/// <summary>
/// 单次外部命令执行的结果。
/// 为什么用 record 而不是抛异常：命令执行（休眠闸门、DISM）的失败是**业务事实**而非程序缺陷，
/// 内核必须把失败当成可展示、可记录、可复核的返回值向上传递（设计文档 §7.1）。
/// </summary>
/// <param name="ExitCode">进程退出码；超时统一为 <c>-1</c>，无法启动进程同样为 <c>-1</c>。</param>
/// <param name="StdOut">标准输出全文（UTF-8 解码）。</param>
/// <param name="StdErr">标准错误全文；超时时必定包含 <c>timeout</c> 标记，供上层判定。</param>
public sealed record CommandResult(int ExitCode, string StdOut, string StdErr);

/// <summary>
/// 外部进程执行器的抽象。
/// 为什么需要抽象：Task 11 的休眠闸门必须能被单测断言"未授权时命令调用次数为 0"，
/// 只有把进程启动隔离在接口后面，测试才能注入计数用的假实现（设计文档 §11 假实现测试）。
/// </summary>
public interface ICommandRunner
{
    /// <summary>
    /// 同步执行一条外部命令。
    /// 契约：**不抛异常**——超时、进程启动失败、非 0 退出码全部通过返回值表达。
    /// </summary>
    /// <param name="exe">可执行文件路径或名称（如 <c>powercfg</c>、<c>dism.exe</c>）。</param>
    /// <param name="arguments">命令行参数原文（不含 exe 本身）。</param>
    /// <param name="timeout">最长等待时间；超时后杀进程树并返回 <c>ExitCode = -1</c>。</param>
    /// <returns>命令执行结果。</returns>
    CommandResult Run(string exe, string arguments, TimeSpan timeout);
}
