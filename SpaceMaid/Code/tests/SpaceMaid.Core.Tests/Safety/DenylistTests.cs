using SpaceMaid.Core.Safety;

namespace SpaceMaid.Core.Tests.Safety;

public class DenylistTests
{
    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"C:\Windows\SysWOW64\kernel32.dll")]
    [InlineData(@"C:\Windows\WinSxS\Manifests\x.manifest")]
    [InlineData(@"C:\Windows\Installer\a.msi")]
    [InlineData(@"C:\Windows\Fonts\arial.ttf")]
    [InlineData(@"C:\Windows\System32\DriverStore\FileRepository\x.driver")]
    [InlineData(@"C:\Windows\System32\config\SOFTWARE")]
    [InlineData(@"C:\hiberfil.sys")]
    [InlineData(@"C:\pagefile.sys")]
    [InlineData(@"C:\swapfile.sys")]
    [InlineData(@"C:\System Volume Information\{abc}")]
    [InlineData(@"C:\Users\test\.ssh\id_rsa")]
    [InlineData(@"C:\Users\test\proj\.git\config")]
    [InlineData(@"C:\Users\test\.gnupg\secring.gpg")]
    public void Should_deny_protected_paths(string path)
    {
        Assert.True(Denylist.IsDenied(path), $"{path} 必须被禁止清单拦截");
    }

    [Theory]
    [InlineData(@"C:\Windows\Temp\a.tmp")]
    [InlineData(@"C:\Windows\Logs\CBS\CBS.log")]
    [InlineData(@"C:\Windows\SoftwareDistribution\Download\a.cab")]
    [InlineData(@"C:\Windows\Prefetch\A.EXE-1234.pf")]
    [InlineData(@"C:\Windows\MEMORY.DMP")]
    [InlineData(@"C:\Windows\Minidump\090126-1234-01.dmp")]
    [InlineData(@"C:\Users\test\AppData\Local\Temp\a.tmp")]
    [InlineData(@"C:\Users\test\AppData\Local\Microsoft\Windows\Explorer\thumbcache_96.db")]
    public void Should_not_deny_paths_that_we_are_supposed_to_clean(string path)
    {
        Assert.False(Denylist.IsDenied(path), $"{path} 是我们要清理的目标，不应被禁止清单拦截");
    }

    [Fact]
    public void Should_deny_user_folder_roots_but_allow_their_explicit_children()
    {
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        // 目录本身不允许被当作清理目标
        Assert.True(Denylist.IsDenied(downloads));

        // 其下明确列出的文件（例如下载目录里的安装包）允许通过禁止清单，再由条目白名单把关
        Assert.False(Denylist.IsDenied(Path.Combine(downloads, "setup.exe")));
    }

    [Fact]
    public void Should_deny_program_files_trees()
    {
        foreach (var pf in Denylist.ProgramFilesDirectories)
        {
            Assert.True(Denylist.IsDenied(Path.Combine(pf, "SomeApp", "app.exe")));
            Assert.True(Denylist.IsDenied(pf));
        }
    }

    [Fact]
    public void Should_deny_empty_or_blank_path()
    {
        Assert.True(Denylist.IsDenied(string.Empty));
        Assert.True(Denylist.IsDenied("   "));
    }

    [Fact]
    public void ExplainDenial_should_be_readable_chinese()
    {
        var reason = Denylist.ExplainDenial(@"C:\Windows\System32\cmd.exe");
        Assert.Contains("禁止清单", reason);
        Assert.Contains("C:", reason);
    }
}
