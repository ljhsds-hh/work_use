using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Logging;
using SpaceMaid.Core.Platform;
using SpaceMaid.Core.Settings;
using SpaceMaid.Core.Tests.Abstractions;

namespace SpaceMaid.Core.Tests;

/// <summary>
/// 对抗式评审 F-7 / F-8 的回归：重解析点判定失败时必须**保守**，且隔离区路径不合格时不得做任何删除类维护。
/// </summary>
public class PlatformSafetyFallbackTests
{
    [Fact]
    public void IsReparsePoint_should_be_false_for_plain_and_missing_paths()
    {
        using var root = new TempRoot();
        var plain = root.WriteFile(@"plain\a.tmp", "x");
        var fileSystem = new WindowsFileSystem();

        Assert.False(fileSystem.IsReparsePoint(plain));
        Assert.False(fileSystem.IsReparsePoint(root.Combine(@"plain\not-there.tmp")));
    }

    [Fact]
    public void IsReparsePoint_should_be_conservative_when_attributes_cannot_be_read()
    {
        var fileSystem = new WindowsFileSystem();

        // 含非法字符的路径：GetAttributes 会抛异常。无法判断"是不是链接"时按**是**处理（保守方向），
        // 因为 SafetyGate 会把 ReparsePoint 直接判为不允许——反过来（吞成 false）会让路径被放行。
        Assert.True(fileSystem.IsReparsePoint(@"C:\<>|?*"));
    }

    [Fact]
    public void Prepare_should_skip_destructive_maintenance_when_quarantine_is_unusable()
    {
        using var root = new TempRoot();
        var fileSystem = new WindowsFileSystem();

        // 用 UNC 路径作为隔离区基目录：校验必然拒绝（网络路径），因此不得执行任何删除类维护
        var services = CoreServices.Create(
            new AppSettings
            {
                QuarantineBasePath = @"\\server\share\quarantine",
                LogDirectory = root.Combine("logs"),
                ReportDirectory = root.Combine("reports")
            },
            fileSystem: fileSystem,
            clock: new SpaceMaid.Core.Tests.Scanning.FakeClock(DateTimeOffset.Now),
            volumes: new SpaceMaid.Core.Tests.Quarantine.MappedVolumeProbe(),
            environment: new WindowsEnvironmentProbe(),
            commandRunner: new SpaceMaid.Core.Tests.Execution.FakeCommandRunner(),
            log: SilentLogSink.Instance);

        var preparation = services.Prepare(allowDestructiveMaintenance: true);

        Assert.False(preparation.QuarantineUsable);
        Assert.Contains(preparation.Recovery.Notes, note => note.Contains("已跳过"));
        Assert.Contains(preparation.Release.Notes, note => note.Contains("已跳过"));
    }
}
