using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Platform;
using SpaceMaid.Core.Safety;
using SpaceMaid.Core.Scanning;
using SpaceMaid.Core.Tests.Abstractions;

namespace SpaceMaid.Core.Tests.Scanning;

/// <summary>把 %VAR% 指到临时目录的环境探针，用来在没有 C:\ 的前提下测通配展开。</summary>
internal sealed class TempMappedEnvironmentProbe : IEnvironmentProbe
{
    private readonly string _root;

    public TempMappedEnvironmentProbe(string root) => _root = root;

    public bool IsElevated => true;

    public string SystemDrive => "C:";

    public string ExpandVariables(string raw) => raw
        .Replace("%LOCALAPPDATA%", Path.Combine(_root, "LocalAppData"), StringComparison.OrdinalIgnoreCase)
        .Replace("%APPDATA%", Path.Combine(_root, "Roaming"), StringComparison.OrdinalIgnoreCase)
        .Replace("%TEMP%", Path.Combine(_root, "Temp"), StringComparison.OrdinalIgnoreCase);
}

public class WildcardScanTests
{
    private static CleanItemDefinition Item(params TargetRule[] rules) => new()
    {
        Id = "test.wildcard",
        Category = CleanCategory.L1OneClick,
        DisplayName = "通配测试项",
        Risk = ItemRisk.Safe,
        ActionKind = CleanActionKind.Quarantine,
        Targets = rules,
        AutoRegenerated = true,
        DefaultChecked = true
    };

    [Fact]
    public async Task Should_expand_subdirectory_wildcard()
    {
        using var root = new TempRoot();
        root.WriteFile(@"LocalAppData\Packages\App1\LocalCache\Temp\a.tmp", "a");
        root.WriteFile(@"LocalAppData\Packages\App2\LocalCache\Temp\b.tmp", "b");
        root.WriteFile(@"LocalAppData\Packages\App3\Other\c.tmp", "c");          // 不该被扫到

        var environment = new TempMappedEnvironmentProbe(root.Path);
        var item = Item(TargetRule.ContentsUnder(@"%LOCALAPPDATA%\Packages", @"*\LocalCache\Temp"));
        var engine = new ScanEngine(new WindowsFileSystem(), environment, new ScanFakeVolumeProbe(), new FakeClock(DateTimeOffset.Now));

        var report = await engine.ScanAsync(new ScanRequest(new[] { item }, false), null, CancellationToken.None);

        var entry = report.Entries.Single();
        Assert.True(entry.Available);
        Assert.Equal(2, entry.FileCount);
        Assert.Contains(entry.Files, f => f.Path.EndsWith("a.tmp", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entry.Files, f => f.Path.EndsWith("b.tmp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entry.Files, f => f.Path.EndsWith("c.tmp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Should_expand_matching_directories_only()
    {
        using var root = new TempRoot();
        root.WriteFile(@"LocalAppData\JetBrains\Idea\log\x.log", "x");
        root.WriteFile(@"LocalAppData\JetBrains\Rider\log\y.log", "y");
        root.WriteFile(@"LocalAppData\Other\Rider\log\z.log", "z");

        var environment = new TempMappedEnvironmentProbe(root.Path);
        var rule = TargetRule.ContentsUnder(@"%LOCALAPPDATA%\JetBrains", @"*\log");

        var directories = TargetPathMatcher.ExpandDirectories(rule, new WindowsFileSystem(), environment);

        Assert.Equal(2, directories.Count);
        Assert.All(directories, d => Assert.StartsWith(Path.Combine(root.Path, "LocalAppData", "JetBrains"), d, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Should_allow_candidates_under_wildcard_directory()
    {
        using var root = new TempRoot();
        var inside = root.WriteFile(@"LocalAppData\Packages\App1\LocalCache\Temp\a.tmp", "a");
        var outside = root.WriteFile(@"LocalAppData\Packages\App1\Other\a.tmp", "a");

        var environment = new TempMappedEnvironmentProbe(root.Path);
        var item = Item(TargetRule.ContentsUnder(@"%LOCALAPPDATA%\Packages", @"*\LocalCache\Temp"));
        var gate = new SafetyGate(new WindowsFileSystem(), environment);

        Assert.True(gate.Authorize(inside, item).IsAllowed);
        Assert.Equal(SafetyVerdict.OutsideAllowlist, gate.Authorize(outside, item).Verdict);
    }

    [Fact]
    public void Should_match_file_glob_under_wildcard_directory()
    {
        using var root = new TempRoot();
        var wanted = root.WriteFile(@"LocalAppData\Google\Chrome\User Data\Default\Network\Cookies", "c");
        var other = root.WriteFile(@"LocalAppData\Google\Chrome\User Data\Default\Other\Cookies", "c");
        var wrongName = root.WriteFile(@"LocalAppData\Google\Chrome\User Data\Default\Network\History", "h");

        var environment = new TempMappedEnvironmentProbe(root.Path);
        var item = Item(TargetRule.GlobUnder(@"%LOCALAPPDATA%\Google\Chrome\User Data", @"*\Network", "Cookies"));
        var gate = new SafetyGate(new WindowsFileSystem(), environment);

        Assert.True(gate.Authorize(wanted, item).IsAllowed);
        Assert.Equal(SafetyVerdict.OutsideAllowlist, gate.Authorize(other, item).Verdict);
        Assert.Equal(SafetyVerdict.OutsideAllowlist, gate.Authorize(wrongName, item).Verdict);
    }

    [Fact]
    public void Should_still_require_the_file_name_pattern_for_plain_rules()
    {
        using var root = new TempRoot();
        var installer = root.WriteFile(@"Downloads\setup.exe", "e");
        var document = root.WriteFile(@"Downloads\report.docx", "d");

        var environment = new TempMappedEnvironmentProbe(root.Path);
        var item = Item(TargetRule.Contents(Path.Combine(root.Path, "Downloads"), "*.exe"));
        var gate = new SafetyGate(new WindowsFileSystem(), environment);

        Assert.True(gate.Authorize(installer, item).IsAllowed);
        Assert.Equal(SafetyVerdict.OutsideAllowlist, gate.Authorize(document, item).Verdict);
    }

    [Fact]
    public void Should_not_expand_when_root_missing()
    {
        using var root = new TempRoot();
        var environment = new TempMappedEnvironmentProbe(root.Path);
        var rule = TargetRule.ContentsUnder(@"%LOCALAPPDATA%\NotThere", @"*\log");

        var directories = TargetPathMatcher.ExpandDirectories(rule, new WindowsFileSystem(), environment);

        Assert.Empty(directories);
    }

    [Fact]
    public async Task Recursive_wildcard_rule_should_allow_nested_files_end_to_end()
    {
        using var root = new TempRoot();
        root.WriteFile(@"LocalAppData\Packages\App1\LocalCache\Temp\sub\deep.tmp", "deep");
        var outside = Path.Combine(root.Path, "LocalAppData", "Packages", "App1", "Other", "deep.tmp");

        var environment = new TempMappedEnvironmentProbe(root.Path);
        // 清单里的缓存条目会被 WithDefaultDepth 置为递归：扫描能收到嵌套文件，
        // 安全闸门也必须放行同一批文件，否则会出现"扫到了却全部被拒绝"的空转。
        var item = Item(TargetRule.ContentsUnder(@"%LOCALAPPDATA%\Packages", @"*\LocalCache\Temp") with { Recurse = true });
        var engine = new ScanEngine(new WindowsFileSystem(), environment, new ScanFakeVolumeProbe(), new FakeClock(DateTimeOffset.Now));

        var report = await engine.ScanAsync(new ScanRequest(new[] { item }, false), null, CancellationToken.None);
        var scanned = report.Entries.Single().Files.Single().Path;

        Assert.EndsWith(@"sub\deep.tmp", scanned, StringComparison.OrdinalIgnoreCase);

        var gate = new SafetyGate(new WindowsFileSystem(), environment);
        Assert.True(gate.Authorize(scanned, item).IsAllowed, "递归的通配规则必须放行嵌套文件");
        Assert.Equal(SafetyVerdict.OutsideAllowlist, gate.Authorize(outside, item).Verdict);
    }
}
