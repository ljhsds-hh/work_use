using SpaceMaid.Core.Platform;

namespace SpaceMaid.Core.Tests.Scanning.Selectors;

/// <summary>
/// <see cref="RegistryInstalledProgramIndex"/> 的用例。
///
/// 注册表内容随机器变化、不可断言，因此这里只测两件事：
/// ① 纯判定函数 <c>IsPathReferencedBy</c> 的关系规则（无 IO，可穷尽）；
/// ② <c>IsReferenced</c> 面对不可判定输入时**不抛异常、且绝不因此放松判定**（保守返回 true）。
/// </summary>
public class RegistryInstalledProgramIndexTests
{
    [Theory]
    [InlineData(@"C:\Program Files\App", @"C:\Program Files\App")]                 // 同一个目录
    [InlineData(@"C:\Program Files\App", @"C:\Program Files\App\sub")]             // 候选是记录的子孙
    [InlineData(@"C:\Program Files\App\app.exe", @"C:\Program Files\App")]         // 记录是候选的子孙
    [InlineData(@"C:\Program Files\App\", @"C:\Program Files\App")]                // 尾部反斜杠
    [InlineData(@"""C:\Program Files\App\app.exe"",0", @"C:\Program Files\App")]   // DisplayIcon 形态
    [InlineData(@"C:\Program Files\App\unins000.exe /S", @"C:\Program Files\App")] // 未加引号的 UninstallString
    [InlineData(@"c:\program files\app", @"C:\Program Files\App")]                 // 大小写不敏感
    public void Should_treat_ancestor_relations_as_referenced(string recordPath, string candidateDirectory)
    {
        Assert.True(RegistryInstalledProgramIndex.IsPathReferencedBy(recordPath, candidateDirectory));
    }

    [Theory]
    [InlineData(@"C:\Program Files\App", @"C:\ProgramData\OldApp")]
    [InlineData(@"C:\Program Files\App", @"C:\ProgramData\OldApplication")]        // 前缀相同但不是子路径
    [InlineData("MsiExec.exe /X{1234-5678-90AB}", @"C:\ProgramData\OldApp")]       // 抽不出绝对路径
    [InlineData("", @"C:\ProgramData\OldApp")]
    [InlineData("   ", @"C:\ProgramData\OldApp")]
    [InlineData(@"%UNKNOWNVAR%\App", @"C:\ProgramData\OldApp")]                    // 变量展开不了 -> 不敢当引用
    public void Should_not_treat_unrelated_records_as_referenced(string recordPath, string candidateDirectory)
    {
        Assert.False(RegistryInstalledProgramIndex.IsPathReferencedBy(recordPath, candidateDirectory));
    }

    [Fact]
    public void Should_be_conservative_when_the_candidate_directory_is_unusable()
    {
        // 候选侧确定不了 -> 一律当作"被引用"（宁可不列出）
        Assert.True(RegistryInstalledProgramIndex.IsPathReferencedBy(@"C:\Program Files\App", null));
        Assert.True(RegistryInstalledProgramIndex.IsPathReferencedBy(@"C:\Program Files\App", string.Empty));
        Assert.True(RegistryInstalledProgramIndex.IsPathReferencedBy(@"C:\Program Files\App", "   "));
        Assert.True(RegistryInstalledProgramIndex.IsPathReferencedBy(@"C:\Program Files\App", @"relative\dir"));
        Assert.True(RegistryInstalledProgramIndex.IsPathReferencedBy(@"C:\Program Files\App", @"%NOTEXPANDED%\dir"));
    }

    [Fact]
    public void Should_never_throw_and_never_relax_on_bad_input()
    {
        var log = new RecordingLogSink();
        var index = new RegistryInstalledProgramIndex(log);

        var exception = Record.Exception(() => index.IsReferenced("bad\u0000path"));

        Assert.Null(exception);
        Assert.True(index.IsReferenced("bad\u0000path"));      // 不确定 -> 被引用
        Assert.True(index.IsReferenced(string.Empty));         // 空 -> 被引用
        Assert.True(index.IsReferenced(@"relative\dir"));      // 相对路径 -> 被引用
        Assert.NotEmpty(log.Warnings);
    }

    [Fact]
    public void Should_not_throw_when_reading_the_uninstall_keys()
    {
        // 真实读取一次真实的注册表键：无论机器上有没有对应记录，都不能抛异常。
        var index = new RegistryInstalledProgramIndex();

        var exception = Record.Exception(() => index.IsReferenced(@"C:\__spacemaid_no_such_app_directory__\sub"));

        Assert.Null(exception);
    }
}
