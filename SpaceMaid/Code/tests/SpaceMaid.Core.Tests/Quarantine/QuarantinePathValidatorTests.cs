using SpaceMaid.Core.Platform;
using SpaceMaid.Core.Quarantine;

namespace SpaceMaid.Core.Tests.Quarantine;

public class QuarantinePathValidatorTests
{
    private const string SourceOnC = @"C:\Windows\Temp";

    private readonly QuarantineFakeFileSystem _fs = new();
    private readonly QuarantineFakeVolumeProbe _volumes = new();
    private readonly WindowsEnvironmentProbe _env = new();
    private readonly QuarantinePathValidator _validator = new();

    public QuarantinePathValidatorTests()
    {
        _volumes.FreeBytes[@"C:\"] = 5L * 1024 * 1024 * 1024;
        _volumes.FreeBytes[@"D:\"] = 100L * 1024 * 1024 * 1024;
        _volumes.FreeBytes[@"E:\"] = 100L * 1024 * 1024 * 1024;
    }

    private QuarantinePathValidation Validate(string candidate, long requiredBytes = 0) =>
        _validator.Validate(candidate, requiredBytes, SourceOnC, _volumes, _fs, _env);

    [Fact]
    public void Should_reject_empty_path()
    {
        Assert.Equal(QuarantinePathLevel.Reject, Validate("   ").Level);
    }

    [Fact]
    public void Should_reject_unc_network_path()
    {
        var result = Validate(@"\\server\share\quarantine");
        Assert.Equal(QuarantinePathLevel.Reject, result.Level);
        Assert.Contains("网络", result.Message);
    }

    [Fact]
    public void Should_reject_volume_root()
    {
        var result = Validate(@"D:\");
        Assert.Equal(QuarantinePathLevel.Reject, result.Level);
        Assert.Contains("根目录", result.Message);
    }

    [Fact]
    public void Should_reject_unwritable_directory()
    {
        _fs.ThrowOnCreate = true;
        var result = Validate(@"D:\SpaceMaidQuarantine");
        Assert.Equal(QuarantinePathLevel.Reject, result.Level);
        Assert.Contains("无法写入", result.Message);
    }

    [Fact]
    public void Should_reject_when_directory_creation_silently_fails()
    {
        _fs.ReportMissingAfterCreate = true;
        var result = Validate(@"D:\SpaceMaidQuarantine");
        Assert.Equal(QuarantinePathLevel.Reject, result.Level);
    }

    [Fact]
    public void Should_reject_denied_system_directory()
    {
        var result = Validate(@"C:\Windows\System32\quarantine");
        Assert.Equal(QuarantinePathLevel.Reject, result.Level);
        Assert.Contains("系统保护目录", result.Message);
    }

    [Fact]
    public void Should_warn_when_quarantine_is_on_same_volume_as_source()
    {
        var result = Validate(@"C:\SpaceMaidQuarantine");

        Assert.Equal(QuarantinePathLevel.Warn, result.Level);
        Assert.Contains("建议改到其他盘", result.Message);
    }

    [Fact]
    public void Should_not_reject_same_volume_even_when_space_is_tight()
    {
        // 回归用例（设计决策 D-6）：同卷只是移动目录条目，不预检空间，绝不能因"空间不足"误拒
        _volumes.FreeBytes[@"C:\"] = 0;

        var result = Validate(@"C:\SpaceMaidQuarantine", requiredBytes: 10L * 1024 * 1024 * 1024);

        Assert.Equal(QuarantinePathLevel.Warn, result.Level);
    }

    [Fact]
    public void Should_reject_cross_volume_when_not_enough_space()
    {
        _volumes.FreeBytes[@"D:\"] = 1024;

        var result = Validate(@"D:\SpaceMaidQuarantine", requiredBytes: 10L * 1024 * 1024);

        Assert.Equal(QuarantinePathLevel.Reject, result.Level);
        Assert.Contains("空间不足", result.Message);
    }

    [Fact]
    public void Should_accept_cross_volume_with_enough_space()
    {
        var result = Validate(@"D:\SpaceMaidQuarantine", requiredBytes: 10L * 1024 * 1024);

        Assert.Equal(QuarantinePathLevel.Ok, result.Level);
        Assert.Contains(@"D:\SpaceMaidQuarantine\SpaceMaid\Quarantine", result.Detail);
    }

    [Fact]
    public void Should_warn_on_removable_media()
    {
        _volumes.RemovableVolumes.Add(@"E:\");

        var result = Validate(@"E:\Quarantine");

        Assert.Equal(QuarantinePathLevel.Warn, result.Level);
        Assert.Contains("可移动", result.Message);
    }

    [Fact]
    public void Should_place_storage_under_spacemaid_subfolder()
    {
        var result = Validate(@"D:\MyQuarantine");

        Assert.Equal(QuarantinePathLevel.Ok, result.Level);
        Assert.Contains(@"D:\MyQuarantine\SpaceMaid\Quarantine", result.Detail);
    }
}
