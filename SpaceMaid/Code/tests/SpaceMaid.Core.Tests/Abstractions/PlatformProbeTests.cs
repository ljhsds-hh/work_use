using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Platform;

namespace SpaceMaid.Core.Tests.Abstractions;

/// <summary>
/// 卷 / 环境 / 时钟 / 日志四个小而关键的实现契约。
/// 这些类没有自己的 Task 测试文件清单，但都是 Task 5–12 的注入依赖，必须一并守住。
/// </summary>
public class PlatformProbeTests
{
    [Fact]
    public void VolumeProbe_should_resolve_root_and_free_space()
    {
        using TempRoot root = new TempRoot();
        IVolumeProbe volumes = new WindowsVolumeProbe();

        string volume = volumes.GetVolumeOf(root.Path);

        Assert.EndsWith("\\", volume, StringComparison.Ordinal);
        Assert.Equal(Path.GetPathRoot(Path.GetFullPath(root.Path))!.ToUpperInvariant(), volume.ToUpperInvariant());
        Assert.True(volumes.GetFreeBytes(volume) > 0);
    }

    [Fact]
    public void VolumeProbe_should_tolerate_unknown_volume()
    {
        IVolumeProbe volumes = new WindowsVolumeProbe();

        Assert.Equal(0, volumes.GetFreeBytes(@"Q:\"));
        Assert.False(volumes.IsRemovable(@"Q:\"));
    }

    [Theory]
    [InlineData(@"\\server\share", true)]
    [InlineData(@"\\server\share\folder\file.txt", true)]
    [InlineData(@"C:\Temp", false)]
    [InlineData("/server/share", false)]
    public void VolumeProbe_should_detect_unc_paths(string path, bool expected)
    {
        IVolumeProbe volumes = new WindowsVolumeProbe();

        Assert.Equal(expected, volumes.IsUnc(path));
    }

    [Fact]
    public void EnvironmentProbe_should_report_system_drive_and_expansion_rules()
    {
        IEnvironmentProbe env = new WindowsEnvironmentProbe();

        Assert.Matches(@"^[A-Za-z]:$", env.SystemDrive);
        Assert.Equal(Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\'), env.SystemDrive);

        // 已知变量被展开
        string expanded = env.ExpandVariables("%WINDIR%\\Temp");
        Assert.DoesNotContain("%", expanded);
        Assert.Contains("Temp", expanded, StringComparison.OrdinalIgnoreCase);

        // 未知变量原样保留（PathNormalizer 要靠这一点拒绝"未展开变量"落地路径）
        Assert.Equal("%SPACEMAID_NOPE_VAR%\\x", env.ExpandVariables("%SPACEMAID_NOPE_VAR%\\x"));

        // 单独的 % 不是变量语法，必须原样保留
        Assert.Equal("100%\\x", env.ExpandVariables("100%\\x"));
    }

    [Fact]
    public void Clock_should_return_monotonic_aware_now()
    {
        IClock clock = new SystemClock();

        DateTimeOffset before = DateTimeOffset.Now;
        DateTimeOffset now = clock.Now;
        DateTimeOffset after = DateTimeOffset.Now;

        Assert.NotEqual(TimeSpan.Zero, now.Offset);
        Assert.InRange(now, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public void FileLogSink_should_append_lines_to_daily_file()
    {
        using TempRoot root = new TempRoot();
        IClock clock = new SystemClock();
        ILogSink sink = new FileLogSink(root.Path, clock);

        sink.Info("普通信息");
        sink.Warn("警告信息");
        sink.Error("错误信息", new InvalidOperationException("模拟异常"));

        string expectedFile = Path.Combine(root.Path, $"spacemaid-{clock.Now:yyyyMMdd}.log");
        Assert.True(File.Exists(expectedFile));

        string content = File.ReadAllText(expectedFile);
        Assert.Contains("普通信息", content);
        Assert.Contains("警告信息", content);
        Assert.Contains("错误信息", content);
        Assert.Contains("InvalidOperationException", content);
    }

    [Fact]
    public void FileLogSink_should_degrade_silently_when_directory_is_unwritable()
    {
        ILogSink sink = new FileLogSink(@"\\spacemaid-unreachable-host\nowhere\logs", new SystemClock());

        // 不得抛异常打断程序（设计文档 §1.1-3 / §10 的"不可写则回退且不中断"）
        Exception? thrown = Record.Exception(() =>
        {
            sink.Info("a");
            sink.Warn("b");
            sink.Error("c", new InvalidOperationException("x"));
        });

        Assert.Null(thrown);
    }

    [Fact]
    public void NullLogSink_should_swallow_everything()
    {
        ILogSink sink = new NullLogSink();

        Exception? thrown = Record.Exception(() =>
        {
            sink.Info("a");
            sink.Warn("b");
            sink.Error("c", new InvalidOperationException("x"));
        });

        Assert.Null(thrown);
    }
}
