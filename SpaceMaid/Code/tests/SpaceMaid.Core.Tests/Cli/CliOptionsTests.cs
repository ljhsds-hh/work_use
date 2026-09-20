using SpaceMaid.Core.Cli;

namespace SpaceMaid.Core.Tests.Cli;

/// <summary>
/// 只读 CLI 的契约测试（需求 9.1 / 实施计划 Task 14）。
/// 重点不是"能解析参数"，而是**证明它没有任何删除入口**。
/// </summary>
public class CliOptionsTests
{
    [Fact]
    public void No_arguments_means_normal_gui_start()
    {
        var options = CliOptions.Parse(Array.Empty<string>());

        Assert.Equal(CliMode.None, options.Mode);
        Assert.Equal(string.Empty, options.Message);
    }

    [Fact]
    public void Should_parse_dry_run()
    {
        var options = CliOptions.Parse(new[] { "--dry-run" });

        Assert.Equal(CliMode.DryRun, options.Mode);
        Assert.Equal(string.Empty, options.Message);
    }

    [Fact]
    public void Should_parse_dry_run_with_output_directory()
    {
        var options = CliOptions.Parse(new[] { "--dry-run", "--out", @"D:\reports" });

        Assert.Equal(CliMode.DryRun, options.Mode);
        Assert.Equal(@"D:\reports", options.OutputDirectory);
    }

    [Fact]
    public void Should_parse_report_with_manifest_directory()
    {
        var options = CliOptions.Parse(new[] { "--report", @"D:\logs\SpaceMaid\清单\20260920-143000-000" });

        Assert.Equal(CliMode.Report, options.Mode);
        Assert.Equal(@"D:\logs\SpaceMaid\清单\20260920-143000-000", options.ManifestDirectory);
    }

    [Fact]
    public void Should_return_help()
    {
        var options = CliOptions.Parse(new[] { "--help" });

        Assert.Equal(CliMode.Help, options.Mode);
        Assert.Contains("--dry-run", options.Message);
        Assert.Contains("不提供", options.Message);
    }

    [Theory]
    [InlineData("--clean")]
    [InlineData("--delete")]
    [InlineData("--execute")]
    [InlineData("--force")]
    [InlineData("--yes")]
    [InlineData("-y")]
    [InlineData("--all")]
    [InlineData("--silent")]
    public void Should_reject_every_deletion_switch(string forbidden)
    {
        var options = CliOptions.Parse(new[] { "--dry-run", forbidden });

        Assert.Equal(CliMode.None, options.Mode);
        Assert.Contains("不提供命令行清理能力", options.Message);
    }

    [Theory]
    [InlineData("--whatever")]
    [InlineData("-x")]
    [InlineData("clean")]
    public void Should_reject_unknown_arguments(string unknown)
    {
        var options = CliOptions.Parse(new[] { unknown });

        Assert.Equal(CliMode.None, options.Mode);
        Assert.Contains("无法识别的参数", options.Message);
    }

    [Fact]
    public void Should_reject_missing_option_values()
    {
        Assert.Contains("需要指定清单目录", CliOptions.Parse(new[] { "--report" }).Message);
        Assert.Contains("需要指定输出目录", CliOptions.Parse(new[] { "--out" }).Message);
    }

    [Fact]
    public void Should_reject_conflicting_modes()
    {
        var options = CliOptions.Parse(new[] { "--dry-run", "--report", @"D:\x" });

        Assert.Equal(CliMode.None, options.Mode);
        Assert.Contains("不能同时使用", options.Message);
    }

    [Fact]
    public void Forbidden_switch_list_should_be_exposed_for_documentation()
    {
        // 这份列表会写进 README / 帮助文本，作为"命令行删除不存在"的机器可读证据
        Assert.Contains("--clean", CliOptions.ForbiddenSwitches);
        Assert.Contains("--force", CliOptions.ForbiddenSwitches);
    }
}
