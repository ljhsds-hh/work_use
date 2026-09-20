using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Cli;
using SpaceMaid.Core.Logging;
using SpaceMaid.Core.Reporting;
using SpaceMaid.Core.Settings;
using SpaceMaid.Core.Tests.Abstractions;
using SpaceMaid.Core.Tests.Quarantine;
using SpaceMaid.Core.Tests.Scanning;

namespace SpaceMaid.Core.Tests.Cli;

/// <summary>
/// 只读 CLI 的端到端测试：跑完必须能在磁盘上看到清单/复核报告，且**一个文件都不能被删**。
/// </summary>
public class CliRunnerTests
{
    /// <summary>把条目里的 %TEMP% 指到临时目录，其余变量原样保留（对应条目会判为不可用，不会碰真实系统目录）。</summary>
    private sealed class TempCliProbe : IEnvironmentProbe
    {
        private readonly string _root;

        public TempCliProbe(string root) => _root = root;

        public bool IsElevated => true;

        public string SystemDrive => "C:";

        public string ExpandVariables(string raw) =>
            raw.Replace("%TEMP%", Path.Combine(_root, "Temp"), StringComparison.OrdinalIgnoreCase);
    }

    private static CoreServices CreateServices(TempRoot root) => CoreServices.Create(
        new AppSettings
        {
            QuarantineBasePath = root.Combine("quarantine"),
            LogDirectory = root.Combine("logs"),
            ReportDirectory = root.Combine("reports")
        },
        fileSystem: new SpaceMaid.Core.Platform.WindowsFileSystem(),
        clock: new FakeClock(DateTimeOffset.Now),
        volumes: new MappedVolumeProbe(),
        environment: new TempCliProbe(root.Path),
        commandRunner: new SpaceMaid.Core.Tests.Execution.FakeCommandRunner(),
        log: SilentLogSink.Instance);

    [Fact]
    public void Dry_run_should_export_manifest_and_delete_nothing()
    {
        using var root = new TempRoot();
        var junk = root.WriteFile(@"Temp\junk.tmp", "junk-content");
        var services = CreateServices(root);

        var result = new CliRunner(services).Run(CliOptions.Parse(new[] { "--dry-run" }));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, result.Artifacts.Count);
        Assert.All(result.Artifacts, path => Assert.True(File.Exists(path)));
        Assert.True(File.Exists(junk), "dry-run 绝不允许动任何被扫描的文件");

        var csv = File.ReadAllText(result.Artifacts[1]);
        Assert.Contains("junk.tmp", csv);
        Assert.Contains("只读", result.Message);
    }

    [Fact]
    public void Dry_run_should_honour_output_directory()
    {
        using var root = new TempRoot();
        root.WriteFile(@"Temp\junk.tmp", "junk");
        var services = CreateServices(root);
        var output = root.Combine("custom-output");

        var result = new CliRunner(services).Run(CliOptions.Parse(new[] { "--dry-run", "--out", output }));

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith(output, result.Artifacts[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Report_should_read_manifest_and_write_review()
    {
        using var root = new TempRoot();
        var junk = root.WriteFile(@"Temp\junk.tmp", "junk");
        var services = CreateServices(root);
        var runner = new CliRunner(services);

        var dryRun = runner.Run(CliOptions.Parse(new[] { "--dry-run" }));
        var manifestDirectory = Path.GetDirectoryName(dryRun.Artifacts[0])!;

        // 模拟"用户在执行清理时把这个文件移走了"（测试里直接用删除代替，工具本身不提供命令行删除）
        File.Delete(junk);

        var result = runner.Run(CliOptions.Parse(new[] { "--report", manifestDirectory }));

        Assert.Equal(0, result.ExitCode);
        Assert.Single(result.Artifacts);
        Assert.True(File.Exists(result.Artifacts[0]));
        Assert.Contains("复核完成", result.Message);

        var report = File.ReadAllText(result.Artifacts[0]);
        Assert.Contains("清单文件数：1", report);
    }

    [Fact]
    public void Report_should_fail_cleanly_on_missing_manifest()
    {
        using var root = new TempRoot();
        var services = CreateServices(root);

        var result = new CliRunner(services).Run(CliOptions.Parse(new[] { "--report", root.Combine("nope") }));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("不存在", result.Message);
        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public void Usage_error_should_return_exit_code_two()
    {
        using var root = new TempRoot();
        var services = CreateServices(root);

        var result = new CliRunner(services).Run(CliOptions.Parse(new[] { "--clean" }));

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("不提供命令行清理能力", result.Message);
    }

    [Fact]
    public void Help_should_return_zero_and_print_usage()
    {
        using var root = new TempRoot();
        var services = CreateServices(root);

        var result = new CliRunner(services).Run(CliOptions.Parse(new[] { "--help" }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--dry-run", result.Message);
    }
}
