using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Platform;

namespace SpaceMaid.Core.Tests.Abstractions;

/// <summary>
/// Task 2 Step 1：进程命令执行器的行为契约。
/// 为什么重要：休眠闸门（Task 11）与 DISM（Task 11）都靠它落地，超时必须是可判定的失败值而不是异常。
/// </summary>
public class ProcessCommandRunnerTests
{
    [Fact]
    public void Run_should_capture_stdout_and_exit_code()
    {
        ICommandRunner runner = new ProcessCommandRunner();

        CommandResult result = runner.Run("cmd.exe", "/c echo hello", TimeSpan.FromSeconds(30));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.StdOut);
    }

    [Fact]
    public void Run_should_return_minus_one_and_timeout_marker_on_timeout()
    {
        ICommandRunner runner = new ProcessCommandRunner();

        // ping 回环地址默认 4 次约 3 秒以上，500ms 必然超时；不依赖任何外部网络。
        CommandResult result = runner.Run("ping.exe", "127.0.0.1 -n 6", TimeSpan.FromMilliseconds(500));

        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("timeout", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Run_should_not_throw_when_executable_is_missing()
    {
        ICommandRunner runner = new ProcessCommandRunner();

        CommandResult result = runner.Run(
            "spacemaid-definitely-missing-exe.exe",
            "/nonsense",
            TimeSpan.FromSeconds(5));

        Assert.Equal(-1, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StdErr));
    }
}
