using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Execution;
using SpaceMaid.Core.Models;

namespace SpaceMaid.Core.Tests.Execution;

public class HibernateGateTests
{
    private const string HibernateFile = @"C:\hiberfil.sys";

    private static (HibernateGate Gate, FakeCommandRunner Runner, ScriptedFileSystem FileSystem) CreateGate()
    {
        var runner = new FakeCommandRunner();
        var fileSystem = new ScriptedFileSystem();
        var gate = new HibernateGate(runner, fileSystem, new ExecutionFakeEnvironment());
        return (gate, runner, fileSystem);
    }

    private static CleanPlanItem Item() => new(
        "l3.hibernate",
        CleanCategory.L3Cautious,
        "休眠文件",
        "执行命令：关闭休眠",
        Array.Empty<ScanFile>(),
        0,
        DefaultChecked: false,
        UserChecked: true)
    {
        ActionKind = CleanActionKind.HibernateOff,
        ActionNote = "仅关闭功能，不删文件",
        RestoreHint = "恢复：powercfg /h on"
    };

    [Fact]
    public void Unauthorized_must_not_run_any_command()
    {
        var (gate, runner, _) = CreateGate();
        var options = new ExecutionOptions(@"D:\Quarantine", 7) { AuthorizeHibernate = false };

        var result = gate.Handle(Item(), options, CancellationToken.None);

        Assert.Equal(0, runner.CallCount);
        Assert.Equal(0, result.MovedCount);
        Assert.Contains("未授权", result.Note);
    }

    [Fact]
    public void Authorized_should_run_exact_powercfg_command()
    {
        var (gate, runner, fileSystem) = CreateGate();
        fileSystem.AddFile(HibernateFile, 4L * 1024 * 1024 * 1024);
        runner.Responder = (_, args) =>
        {
            if (args == HibernateGate.DisableArguments)
            {
                fileSystem.RemoveFile(HibernateFile);   // 模拟系统真的把文件收走了
            }

            return new CommandResult(0, string.Empty, string.Empty);
        };

        var options = new ExecutionOptions(@"D:\Quarantine", 7) { AuthorizeHibernate = true };
        var result = gate.Handle(Item(), options, CancellationToken.None);

        var call = Assert.Single(runner.Calls);
        Assert.Equal(HibernateGate.PowerCfgExecutable, call.Exe);
        Assert.Equal(HibernateGate.DisableArguments, call.Args);
        Assert.Equal(1, result.MovedCount);
        Assert.Equal(4L * 1024 * 1024 * 1024, result.MovedBytes);
        Assert.Contains("powercfg /h on", result.Note);
    }

    [Fact]
    public void Failure_must_not_fall_back_to_file_deletion()
    {
        var (gate, runner, fileSystem) = CreateGate();
        fileSystem.AddFile(HibernateFile, 1024);
        runner.Responder = (_, _) => new CommandResult(1, string.Empty, "拒绝访问");

        var options = new ExecutionOptions(@"D:\Quarantine", 7) { AuthorizeHibernate = true };
        var result = gate.Handle(Item(), options, CancellationToken.None);

        Assert.Equal(0, result.MovedCount);
        Assert.Contains("失败", result.Note);
        Assert.Empty(fileSystem.DeletedPaths);          // 绝不降级为删文件
        Assert.Empty(fileSystem.MovedPaths);
        Assert.Single(runner.Calls);                    // 也不重试
    }

    [Fact]
    public void Should_report_failure_when_file_still_exists_after_command()
    {
        var (gate, runner, fileSystem) = CreateGate();
        fileSystem.AddFile(HibernateFile, 1024);
        runner.Responder = (_, _) => new CommandResult(0, string.Empty, string.Empty);

        var options = new ExecutionOptions(@"D:\Quarantine", 7) { AuthorizeHibernate = true };
        var result = gate.Handle(Item(), options, CancellationToken.None);

        Assert.Equal(0, result.MovedCount);
        Assert.Contains("仍然存在", result.Note);
    }

    [Fact]
    public void Probe_should_be_language_neutral()
    {
        var (gate, runner, fileSystem) = CreateGate();
        runner.Responder = (_, _) => new CommandResult(0, "这些设置中的休眠不可用", string.Empty);

        // 中文输出说"不可用"，但文件还在 → 以文件为准判为已开启（不受系统语言影响）
        fileSystem.AddFile(HibernateFile, 1);
        Assert.Equal(HibernateState.Enabled, gate.Probe());

        fileSystem.RemoveFile(HibernateFile);
        Assert.Equal(HibernateState.Disabled, gate.Probe());

        runner.Responder = (_, _) => new CommandResult(1, string.Empty, "not supported");
        Assert.Equal(HibernateState.NotSupported, gate.Probe());
    }
}

public class DismComponentCleanupTests
{
    private static CleanPlanItem Item() => new(
        "l3.component-store",
        CleanCategory.L3Cautious,
        "组件存储清理",
        "执行命令：DISM 组件清理",
        Array.Empty<ScanFile>(),
        0,
        DefaultChecked: false,
        UserChecked: true)
    {
        ActionKind = CleanActionKind.DismComponentCleanup
    };

    [Fact]
    public void Should_use_fixed_official_command_only()
    {
        var runner = new FakeCommandRunner { Responder = (_, _) => new CommandResult(0, "完成", string.Empty) };
        var handler = new DismComponentCleanup(runner);

        handler.Handle(Item(), new ExecutionOptions(@"D:\Quarantine", 7), CancellationToken.None);

        var call = Assert.Single(runner.Calls);
        Assert.Equal(DismComponentCleanup.DismExecutable, call.Exe);
        Assert.Equal(DismComponentCleanup.Arguments, call.Args);
        Assert.DoesNotContain("ResetBase", call.Args);
        Assert.DoesNotContain("/Reset", call.Args);
    }

    [Fact]
    public void Should_parse_released_size_from_output()
    {
        var runner = new FakeCommandRunner
        {
            Responder = (_, _) => new CommandResult(0, "操作成功完成。\n已回收 1.5 GB 存储空间", string.Empty)
        };
        var handler = new DismComponentCleanup(runner);

        var result = handler.Handle(Item(), new ExecutionOptions(@"D:\Quarantine", 7), CancellationToken.None);

        Assert.Equal(1, result.MovedCount);
        Assert.Equal((long)(1.5 * 1024 * 1024 * 1024), result.MovedBytes);
    }

    [Fact]
    public void Should_tolerate_unparsable_output()
    {
        var runner = new FakeCommandRunner { Responder = (_, _) => new CommandResult(0, "操作成功完成。", string.Empty) };
        var handler = new DismComponentCleanup(runner);

        var result = handler.Handle(Item(), new ExecutionOptions(@"D:\Quarantine", 7), CancellationToken.None);

        Assert.Equal(0, result.MovedBytes);
        Assert.Contains("未知", result.Note);
    }

    [Fact]
    public void Should_treat_nonzero_exit_as_failure()
    {
        var runner = new FakeCommandRunner { Responder = (_, _) => new CommandResult(11, string.Empty, "需要管理员权限") };
        var handler = new DismComponentCleanup(runner);

        var result = handler.Handle(Item(), new ExecutionOptions(@"D:\Quarantine", 7), CancellationToken.None);

        Assert.Equal(0, result.MovedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Contains("失败", result.Note);
        Assert.Single(runner.Calls);       // 不重试
    }
}
