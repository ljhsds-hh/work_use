using System.Text.RegularExpressions;
using SpaceMaid.Core.Catalog;
using SpaceMaid.Core.Models;

namespace SpaceMaid.Core.Tests.Catalog;

/// <summary>
/// 清理项闭集的内容断言（需求 2.2/2.3/2.4/2.5、设计文档 §4.1）。
/// 这里守住的是"清单本身有没有被悄悄改坏"：Id 集合、默认勾选门槛、无盘符硬编码、无还原点字样。
/// </summary>
public class CleanItemCatalogTests
{
    /// <summary>与需求逐字一致的 Id 全集（不得增删改名）。</summary>
    private static readonly string[] ExpectedIds =
    {
        "l1.user-temp",
        "l1.windows-temp",
        "l1.wu-download",
        "l1.delivery-optimization",
        "l1.wer",
        "l1.cbs-logs",
        "l1.dumps",
        "l1.live-kernel-reports",
        "l1.thumb-cache",
        "l1.packages-temp",
        "l1.app-logs-vscode",
        "l1.app-logs-jetbrains",
        "l2.browser-cache",
        "l2.browser-cookies",
        "l2.downloads-installers",
        "l2.dev-caches",
        "l2.windows-old",
        "l2.driver-downloader",
        "l2.prefetch",
        "l2.crash-dumps",
        "l3.hibernate",
        "l3.component-store",
        "l3.orphan-app-dirs",
        "l3.large-files",
        "l3.duplicate-files",
        "l3.chat-cache",
        "l3.pagefile",
        "rb.recycle-bin"
    };

    /// <summary>永久禁止项的兜底检索（需求 2.6 / 不变量 I-5）。</summary>
    private static readonly Regex ForbiddenKeywords = new("(?i)vss|shadow|restore point|还原点");

    /// <summary>盘符硬编码（需求：一切目标路径必须是 %VAR% 模板）。</summary>
    private static readonly Regex DriveLetterPrefix = new("^[A-Za-z]:");

    [Fact]
    public void Should_contain_all_expected_ids()
    {
        var actual = CleanItemCatalog.All.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);

        var missing = ExpectedIds.Where(id => !actual.Contains(id)).ToArray();
        var extra = actual.Where(id => !ExpectedIds.Contains(id, StringComparer.Ordinal)).ToArray();

        Assert.Empty(missing);
        Assert.Empty(extra);
        Assert.Equal(ExpectedIds.Length, actual.Count);
    }

    [Fact]
    public void Should_have_unique_ids()
    {
        var duplicates = CleanItemCatalog.All
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Should_not_contain_vss_keywords()
    {
        foreach (var item in CleanItemCatalog.All)
        {
            foreach (var text in AllText(item))
            {
                Assert.False(
                    ForbiddenKeywords.IsMatch(text),
                    $"清理项 {item.Id} 的文本命中禁止字样（系统还原点/卷影副本永不进入清理清单）：{text}");
            }
        }
    }

    [Fact]
    public void Should_mark_hibernate_as_command_item_without_file_targets()
    {
        var hibernate = CleanItemCatalog.ById("l3.hibernate");

        Assert.NotNull(hibernate);
        Assert.Equal(CleanActionKind.HibernateOff, hibernate.ActionKind);
        Assert.Equal(ItemRisk.Dangerous, hibernate.Risk);
        Assert.False(hibernate.DefaultChecked);
        Assert.Empty(hibernate.Targets);
        Assert.False(string.IsNullOrWhiteSpace(hibernate.ActionNote));
        Assert.Contains("powercfg /h on", hibernate.RestoreHint);

        // 全库任何条目都不得把 hiberfil.sys 当删除目标（需求 3.8：只能 powercfg /h off 释放）。
        var fileTargets = CleanItemCatalog.All
            .SelectMany(item => item.Targets)
            .Where(rule => rule.Path.Contains("hiberfil", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(fileTargets);
    }

    [Fact]
    public void Should_only_allow_default_checked_for_l1_and_windows_old()
    {
        var expected = CleanItemCatalog.All
            .Where(item => item.Category == CleanCategory.L1OneClick)
            .Select(item => item.Id)
            .Append("l2.windows-old")
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var actual = CleanItemCatalog.All
            .Where(item => item.DefaultChecked)
            .Select(item => item.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Should_use_no_hardcoded_drive_letters()
    {
        foreach (var item in CleanItemCatalog.All)
        {
            foreach (var rule in item.Targets)
            {
                Assert.False(
                    DriveLetterPrefix.IsMatch(rule.Path),
                    $"清理项 {item.Id} 的目标路径硬编码了盘符：{rule.Path}");
                Assert.Contains("%", rule.Path);
            }
        }
    }

    /// <summary>条目里所有会被静态检索的文本（与 CatalogValidator 的规则 10 口径一致）。</summary>
    private static IEnumerable<string> AllText(CleanItemDefinition item)
    {
        yield return item.Id;
        yield return item.DisplayName;
        yield return item.ActionNote;
        yield return item.SideEffect;
        yield return item.RestoreHint;

        foreach (var rule in item.Targets)
        {
            yield return rule.Path;
            yield return rule.Pattern;
        }
    }
}
