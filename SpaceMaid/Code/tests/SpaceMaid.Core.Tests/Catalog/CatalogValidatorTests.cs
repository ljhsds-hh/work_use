using SpaceMaid.Core.Catalog;
using SpaceMaid.Core.Models;

namespace SpaceMaid.Core.Tests.Catalog;

/// <summary>
/// 清单自检规则的单测（设计文档 §4.2）：每条规则都用一个**故意违规**的条目证明它真的会响。
/// 先证明"违规会被抓到"，再用 Should_have_no_violations_for_builtin_catalog 证明内置清单是干净的。
/// </summary>
public class CatalogValidatorTests
{
    [Fact]
    public void Should_have_no_violations_for_builtin_catalog()
    {
        var violations = CatalogValidator.Validate(CleanItemCatalog.All);

        Assert.Empty(violations);
    }

    [Fact]
    public void Should_report_duplicate_ids()
    {
        var violations = CatalogValidator.Validate(new[]
        {
            Item(),
            Item()
        });

        var violation = Assert.Single(violations);
        Assert.Contains("重复", violation);
    }

    [Fact]
    public void Should_report_default_checked_without_autoregenerated()
    {
        var violations = CatalogValidator.Validate(new[]
        {
            Item(
                id: "l2.default-checked-but-user-data",
                category: CleanCategory.L2Recommended,
                defaultChecked: true,
                autoRegenerated: false)
        });

        var violation = Assert.Single(violations);
        Assert.Contains("AutoRegenerated", violation);
        Assert.Contains("默认勾选", violation);
    }

    [Fact]
    public void Should_report_missing_note_for_risky_item()
    {
        var violations = CatalogValidator.Validate(new[]
        {
            Item(id: "l3.risky-without-note", category: CleanCategory.L3Cautious, risk: ItemRisk.Dangerous)
        });

        Assert.Contains(violations, v => v.Contains("ActionNote"));
        Assert.Contains(violations, v => v.Contains("RestoreHint"));
    }

    [Fact]
    public void Should_report_targets_for_command_item()
    {
        var violations = CatalogValidator.Validate(new[]
        {
            Item(id: "l2.quarantine-without-target", targets: Array.Empty<TargetRule>())
        });

        var violation = Assert.Single(violations);
        Assert.Contains("TargetRule", violation);
    }

    [Fact]
    public void Should_report_denied_target_tree()
    {
        var violations = CatalogValidator.Validate(new[]
        {
            Item(id: "l1.denied-tree", targets: new[] { TargetRule.Tree(@"%SystemRoot%\System32") })
        });

        var violation = Assert.Single(violations);
        Assert.Contains("禁止清单", violation);
    }

    [Fact]
    public void Should_report_user_root_with_recursive_kind()
    {
        var violations = CatalogValidator.Validate(new[]
        {
            Item(id: "l3.user-root-recursive", targets: new[] { TargetRule.Tree(@"%LOCALAPPDATA%") })
        });

        var violation = Assert.Single(violations);
        Assert.Contains("用户目录", violation);
        Assert.Contains("DirectoryContents", violation);
    }

    [Fact]
    public void Should_report_informational_item_not_denied()
    {
        var violations = CatalogValidator.Validate(new[]
        {
            Item(
                id: "l3.informational-not-denied",
                category: CleanCategory.L3Cautious,
                risk: ItemRisk.Caution,
                actionKind: CleanActionKind.InformationalOnly,
                targets: new[] { TargetRule.File(@"%SystemRoot%\Temp\readme.txt") },
                actionNote: "仅展示，不可清理",
                restoreHint: "不需要恢复：本项不会执行任何清理动作",
                defaultChecked: false,
                autoRegenerated: false)
        });

        var violation = Assert.Single(violations);
        Assert.Contains("禁止清单", violation);
    }

    [Fact]
    public void Should_report_restore_point_keyword()
    {
        var violations = CatalogValidator.Validate(new[]
        {
            Item(id: "l3.sneaky-item", sideEffect: "顺手把系统还原点也清掉")
        });

        var violation = Assert.Single(violations);
        Assert.Contains("还原点", violation);
    }

    /// <summary>
    /// 默认构造一个**完全合规**的条目，测试只改其中一项，保证每个用例只暴露一条违规。
    /// </summary>
    private static CleanItemDefinition Item(
        string id = "l1.sample-item",
        CleanCategory category = CleanCategory.L1OneClick,
        ItemRisk risk = ItemRisk.Safe,
        CleanActionKind actionKind = CleanActionKind.Quarantine,
        IReadOnlyList<TargetRule>? targets = null,
        string actionNote = "",
        string sideEffect = "示例：文件被清理后会被自动重建，没有其他影响。",
        string restoreHint = "",
        bool defaultChecked = true,
        bool autoRegenerated = true) => new()
        {
            Id = id,
            Category = category,
            DisplayName = "示例条目",
            Risk = risk,
            ActionKind = actionKind,
            Targets = targets ?? new[] { TargetRule.Contents(@"%LOCALAPPDATA%\SpaceMaidSampleCache") },
            ActionNote = actionNote,
            SideEffect = sideEffect,
            RestoreHint = restoreHint,
            DefaultChecked = defaultChecked,
            AutoRegenerated = autoRegenerated
        };
}
