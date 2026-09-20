using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using SpaceMaid.Core.Abstractions;

namespace SpaceMaid.Core.Platform;

/// <summary>
/// <see cref="ICommandRunner"/> 的 Windows 实现（基于 <see cref="Process"/>）。
/// <para>
/// 关键契约（Task 11 的休眠闸门/DISM 依赖）：
/// ① 重定向 stdout/stderr 且 UTF-8 解码，中文输出可读；
/// ② <c>CreateNoWindow</c> + <c>UseShellExecute=false</c>，绝不在用户桌面弹出黑窗；
/// ③ 超时返回 <c>ExitCode = -1</c> 且 <c>StdErr</c> 含 <c>timeout</c>，并杀**整棵进程树**
///    （dism/powercfg 都可能派生工作进程，只杀父进程会留下孤儿）。
/// </para>
/// </summary>
public sealed class ProcessCommandRunner : ICommandRunner
{
    /// <summary>超时标记：上层以 <c>StdErr.Contains("timeout")</c> 判定"超时失败"。</summary>
    public const string TimeoutMarker = "timeout";

    /// <inheritdoc />
    public CommandResult Run(string exe, string arguments, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(exe))
        {
            // 刻意不包含 timeout 字样：该标记只用于表达"真的超时了"，否则上层会误判。
            return new CommandResult(-1, string.Empty, "未指定要执行的程序。");
        }

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // 先开重定向再设编码，否则 StandardOutputEncoding 会抛 InvalidOperationException。
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process is null)
            {
                return new CommandResult(-1, string.Empty, "无法启动进程：" + exe);
            }

            // 必须先建立读取任务再等待退出，否则输出填满管道缓冲区会死锁。
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)Math.Max(0, timeout.TotalMilliseconds)))
            {
                KillTree(process);
                // 进程被杀后管道关闭，这里大概率立刻完成；给一个上限避免极端情况挂死。
                string partialOut = Drain(stdoutTask);
                _ = stderrTask; // stderr 内容对超时判定无价值，等它自然结束即可。
                GC.KeepAlive(stderrTask);

                return new CommandResult(
                    -1,
                    partialOut,
                    "命令执行 " + TimeoutMarker + "（超过 " + timeout.TotalSeconds.ToString("0.#") + " 秒），已终止进程树：" + exe + " " + arguments);
            }

            string stdout = Drain(stdoutTask);
            string stderr = Drain(stderrTask);
            return new CommandResult(process.ExitCode, stdout, stderr);
        }
        catch (Win32Exception exception)
        {
            // 程序不存在 / 没有权限：这是"命令失败"的业务事实，不向上抛。
            return new CommandResult(-1, string.Empty, "无法启动进程 " + exe + "：" + exception.Message);
        }
        catch (Exception exception)
        {
            return new CommandResult(-1, string.Empty, "执行命令出现异常 " + exe + "：" + exception.GetType().Name + ":" + exception.Message);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception)
        {
            // 进程可能已自行退出（竞态）：忽略，不影响"超时"这一判定结论。
        }
    }

    private static string Drain(Task<string> task)
    {
        try
        {
            return task.Wait(TimeSpan.FromSeconds(5)) ? task.Result : string.Empty;
        }
        catch (Exception)
        {
            // 管道被强杀打断时读取会失败：输出不完整不影响退出码与超时标记的判定。
            return string.Empty;
        }
    }
}
