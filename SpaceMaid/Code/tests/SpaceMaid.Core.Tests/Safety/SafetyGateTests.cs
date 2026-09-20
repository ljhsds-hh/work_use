using SpaceMaid.Core.Models;
using SpaceMaid.Core.Safety;

namespace SpaceMaid.Core.Tests.Safety;

public class SafetyGateTests
{
    private readonly SafetyFakeFileSystem _fs = new();
    private readonly SafetyFakeEnvironment _env = new();

    private SafetyGate CreateGate() => new(_fs, _env);

    private static CleanItemDefinition Item(params TargetRule[] rules) => new()
    {
        Id = "test.item",
        Category = CleanCategory.L1OneClick,
        DisplayName = "测试项",
        Risk = ItemRisk.Safe,
        ActionKind = CleanActionKind.Quarantine,
        Targets = rules
    };

    [Fact]
    public void Should_deny_protected_path_even_when_item_allows_it()
    {
        // 条目（错误地）把 C:\Windows 整个列为目标：禁止清单必须先拦住它 —— 先拒后允
        var item = Item(TargetRule.Tree(@"C:\Windows"));
        var decision = CreateGate().Authorize(@"C:\Windows\System32\cmd.exe", item);

        Assert.False(decision.IsAllowed);
        Assert.Equal(SafetyVerdict.DeniedByDenylist, decision.Verdict);
    }

    [Fact]
    public void Should_deny_traversal_escape_out_of_allowed_root()
    {
        var item = Item(TargetRule.Contents(@"C:\Windows\Temp"));
        var decision = CreateGate().Authorize(@"C:\Windows\Temp\..\System32\cmd.exe", item);

        Assert.False(decision.IsAllowed);
        Assert.Equal(SafetyVerdict.DeniedByDenylist, decision.Verdict);
    }

    [Fact]
    public void Should_allow_normalized_path_still_inside_allowed_root()
    {
        var item = Item(TargetRule.Contents(@"C:\Windows\Temp"));
        var decision = CreateGate().Authorize(@"C:\Windows\Temp\sub\..\a.tmp", item);

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void Should_reject_reparse_point()
    {
        _fs.ReparsePoints.Add(@"C:\Windows\Temp\link.tmp");
        var item = Item(TargetRule.Contents(@"C:\Windows\Temp"));

        var decision = CreateGate().Authorize(@"C:\Windows\Temp\link.tmp", item);

        Assert.Equal(SafetyVerdict.ReparsePoint, decision.Verdict);
    }

    [Fact]
    public void Should_reject_path_with_reparse_point_ancestor()
    {
        _fs.ReparseAncestors.Add(@"C:\Windows\Temp\junction\a.tmp");
        var item = Item(TargetRule.Tree(@"C:\Windows\Temp"));

        var decision = CreateGate().Authorize(@"C:\Windows\Temp\junction\a.tmp", item);

        Assert.Equal(SafetyVerdict.ReparsePoint, decision.Verdict);
    }

    [Fact]
    public void Should_reject_fixed_file_sibling_paths()
    {
        var item = Item(TargetRule.File(@"C:\Windows\MEMORY.DMP"));

        Assert.True(CreateGate().Authorize(@"C:\Windows\MEMORY.DMP", item).IsAllowed);
        Assert.Equal(
            SafetyVerdict.OutsideAllowlist,
            CreateGate().Authorize(@"C:\Windows\MEMORY.DMP.bak", item).Verdict);
        Assert.Equal(
            SafetyVerdict.OutsideAllowlist,
            CreateGate().Authorize(@"C:\Windows\Minidump\090126-1234-01.dmp", item).Verdict);
    }

    [Fact]
    public void Should_not_allow_the_allowed_directory_itself_for_contents_rule()
    {
        var item = Item(TargetRule.Contents(@"C:\Windows\Temp"));
        var decision = CreateGate().Authorize(@"C:\Windows\Temp", item);

        Assert.Equal(SafetyVerdict.OutsideAllowlist, decision.Verdict);
    }

    [Fact]
    public void Should_report_unsafe_path_for_relative_input()
    {
        var item = Item(TargetRule.Tree(@"C:\Windows\Temp"));
        var decision = CreateGate().Authorize(@"Temp\a.tmp", item);

        Assert.Equal(SafetyVerdict.UnsafePath, decision.Verdict);
    }

    [Fact]
    public void Should_allow_inside_target_root()
    {
        var item = Item(TargetRule.Contents(@"C:\Windows\Temp"));
        var decision = CreateGate().Authorize(@"C:\Windows\Temp\a.tmp", item);

        Assert.True(decision.IsAllowed);
        Assert.Contains("目标范围", decision.Reason);
    }

    [Fact]
    public void Should_deny_outside_all_targets()
    {
        var item = Item(TargetRule.Contents(@"C:\Windows\Temp"));
        var decision = CreateGate().Authorize(@"C:\Windows\Logs\CBS\CBS.log", item);

        Assert.Equal(SafetyVerdict.OutsideAllowlist, decision.Verdict);
    }

    [Fact]
    public void AuthorizeAll_should_stop_at_first_rejection()
    {
        var tempItem = Item(TargetRule.Contents(@"C:\Windows\Temp"));
        var fixedItem = Item(TargetRule.File(@"C:\Windows\MEMORY.DMP"));

        var decision = CreateGate().AuthorizeAll(new[]
        {
            (@"C:\Windows\Temp\a.tmp", tempItem),
            (@"C:\hiberfil.sys", fixedItem)
        });

        Assert.False(decision.IsAllowed);
        Assert.Equal(SafetyVerdict.DeniedByDenylist, decision.Verdict);
    }
}
