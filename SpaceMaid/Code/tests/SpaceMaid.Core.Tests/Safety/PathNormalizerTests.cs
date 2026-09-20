using SpaceMaid.Core.Safety;

namespace SpaceMaid.Core.Tests.Safety;

public class PathNormalizerTests
{
    private readonly SafetyFakeEnvironment _env = new();

    [Fact]
    public void Should_reject_empty_path()
    {
        Assert.False(PathNormalizer.TryNormalize("   ", _env, out _, out var error));
        Assert.Contains("空", error);
    }

    [Fact]
    public void Should_reject_relative_path()
    {
        Assert.False(PathNormalizer.TryNormalize(@"Temp\a.tmp", _env, out _, out var error));
        Assert.Contains("绝对路径", error);
    }

    [Fact]
    public void Should_reject_wildcard_in_landing_path()
    {
        Assert.False(PathNormalizer.TryNormalize(@"C:\Temp\*.tmp", _env, out _, out var error));
        Assert.Contains("通配符", error);

        Assert.False(PathNormalizer.TryNormalize(@"C:\Temp\a?.tmp", _env, out _, out _));
    }

    [Fact]
    public void Should_reject_unexpanded_variable()
    {
        Assert.False(PathNormalizer.TryNormalize(@"%NOT_SET%\a.tmp", _env, out _, out var error));
        Assert.Contains("环境变量", error);
    }

    [Fact]
    public void Should_expand_known_variables()
    {
        Assert.True(PathNormalizer.TryNormalize(@"%TEMP%\a.tmp", _env, out var normalized, out _));
        Assert.Equal(@"C:\Users\test\AppData\Local\Temp\a.tmp", normalized);
    }

    [Fact]
    public void Should_collapse_parent_traversal_into_absolute_path()
    {
        // 规范化只负责消除 .. —— 至于这个路径是否有资格被删除，由 SafetyGate/Denylist 说了算。
        Assert.True(PathNormalizer.TryNormalize(@"C:\Windows\Temp\..\..\Windows\System32\cmd.exe", _env, out var normalized, out _));
        Assert.Equal(@"C:\Windows\System32\cmd.exe", normalized);
    }

    [Fact]
    public void Should_trim_trailing_separator_but_keep_volume_root()
    {
        Assert.Equal(@"C:\Windows\Temp", PathNormalizer.TrimTrailingSeparator(@"C:\Windows\Temp\"));
        Assert.Equal(@"C:\", PathNormalizer.TrimTrailingSeparator(@"C:\"));
    }

    [Theory]
    [InlineData(@"C:\Temp\a.tmp", @"C:\Temp", true)]
    [InlineData(@"C:\Temp\sub\a.tmp", @"C:\Temp", true)]
    [InlineData(@"C:\TempEvil\a.tmp", @"C:\Temp", false)]
    [InlineData(@"C:\Temp", @"C:\Temp", false)]
    [InlineData(@"C:\Temp\", @"C:\Temp", false)]
    public void IsUnder_should_respect_separator_boundary(string path, string root, bool expected)
    {
        Assert.Equal(expected, PathNormalizer.IsUnder(path, root));
    }

    [Fact]
    public void IsSameOrUnder_should_include_root_itself()
    {
        Assert.True(PathNormalizer.IsSameOrUnder(@"C:\Temp", @"C:\Temp"));
        Assert.True(PathNormalizer.IsSameOrUnder(@"C:\Temp\a.tmp", @"C:\Temp"));
        Assert.False(PathNormalizer.IsSameOrUnder(@"C:\Temporary", @"C:\Temp"));
    }

    [Theory]
    [InlineData(@"C:\Windows\Temp\a.tmp", @"C:\", true)]
    [InlineData(@"C:\Temp", @"C:\", true)]
    [InlineData(@"C:\", @"C:\", false)]
    [InlineData(@"D:\a", @"C:\", false)]
    [InlineData(@"C:\Temp\a.tmp", @"C:\Temp", true)]
    [InlineData(@"C:\TempEvil\a.tmp", @"C:\Temp", false)]
    public void IsUnder_should_handle_volume_root_as_base(string path, string root, bool expected)
    {
        // 回归：root 写成 "C:\" 时不能把 C 盘下所有路径都判成"不在其下"
        //（回收站清理项的目标根正是 %SystemDrive%\，否则整档会被安全闸门 100% 拒绝）
        Assert.Equal(expected, PathNormalizer.IsUnder(path, root));
    }

    [Fact]
    public void ContainsSegment_should_match_whole_segments_only()
    {
        Assert.True(PathNormalizer.ContainsSegment(@"C:\Users\test\.ssh\id_rsa", ".ssh"));
        Assert.True(PathNormalizer.ContainsSegment(@"D:\a\System Volume Information\b", "System Volume Information"));
        Assert.False(PathNormalizer.ContainsSegment(@"C:\Users\test\.ssh-backup\id_rsa", ".ssh"));
    }
}
