using System.Text.RegularExpressions;
using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Platform;
using SpaceMaid.Core.Safety;

namespace SpaceMaid.Core.Catalog;

/// <summary>
/// 清单自检（设计文档 §4.2）：把"保守原则"和几条安全不变量写成机器能查的规则。
///
/// 为什么要它：清理项闭集是一份人工维护的登记表，靠人眼守规则迟早会漏；
/// 这里把每条规矩变成一条可单测的断言，<see cref="CleanItemCatalog.All"/> 每加一条就自动过一遍。
/// 返回**违规说明列表**（中文，每条以 <c>"{id}: "` 开头），空列表表示通过。
/// </summary>
public static class CatalogValidator
{
    /// <summary>Id 命名规范：小写字母/数字开头，之后允许小写字母、数字、点、连字符。</summary>
    private static readonly Regex IdPattern = new(
        @"^[a-z0-9][a-z0-9\-\.]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 永久禁止项的文本地雷（需求 2.6 / 不变量 I-5）：
    /// 系统还原点与卷影副本连"出现在清单里"都不允许，因此全库文本必须干净。
    /// </summary>
    private static readonly Regex ForbiddenTextPattern = new(
        @"(?i)vss|shadow|restore point|还原点",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyList<TargetRule> NoTargets = Array.Empty<TargetRule>();

    /// <summary>
    /// 对一批清理项运行全部规则。<paramref name="environment"/> 为 null 时用真实的
    /// <see cref="WindowsEnvironmentProbe"/> 展开 <c>%VAR%</c>（单测可注入假环境）。
    /// </summary>
    public static IReadOnlyList<string> Validate(
        IEnumerable<CleanItemDefinition> items,
        IEnvironmentProbe? environment = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        var probe = environment ?? new WindowsEnvironmentProbe();
        var violations = new List<string>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (item is null)
            {
                violations.Add("(空条目): 清单中存在 null 条目");
                continue;
            }

            ValidateItem(item, probe, seenIds, violations);
        }

        return violations;
    }

    private static void ValidateItem(
        CleanItemDefinition item,
        IEnvironmentProbe environment,
        HashSet<string> seenIds,
        List<string> violations)
    {
        var id = string.IsNullOrWhiteSpace(item.Id) ? "(空 Id)" : item.Id;
        var targets = item.Targets ?? NoTargets;

        // 规则 1：Id 非空、唯一、命名规范（Id 是映射表与清单 csv 的唯一对应依据）
        if (string.IsNullOrWhiteSpace(item.Id))
        {
            violations.Add($"{id}: Id 不能为空");
        }
        else
        {
            if (!IdPattern.IsMatch(item.Id))
            {
                violations.Add($"{id}: Id 只能由小写字母、数字、点、连字符组成，且不能以点或连字符开头");
            }

            if (!seenIds.Add(item.Id))
            {
                violations.Add($"{id}: Id 重复——每个清理项的 Id 必须唯一，一经发布不得复用");
            }
        }

        // 规则 2：L1 的判据就是"删掉后自动重建"，所以必须自动重建且默认勾选
        if (item.Category == CleanCategory.L1OneClick && (!item.AutoRegenerated || !item.DefaultChecked))
        {
            violations.Add($"{id}: L1 条目的判据是“删掉之后会自动重建”，必须同时满足 AutoRegenerated=true 与 DefaultChecked=true");
        }

        // 规则 3：保守原则的机器化表达——只有会自动重建的项才允许默认勾选
        if (item.DefaultChecked && !item.AutoRegenerated)
        {
            violations.Add($"{id}: DefaultChecked=true 但 AutoRegenerated=false——只有“自动重建 / 系统本就会自动清理”的项才允许默认勾选");
        }

        // 规则 4：风险项必须在界面里写明动作性质与恢复方式（需求 5.4-3/5.4-4）
        if (item.Risk != ItemRisk.Safe)
        {
            if (string.IsNullOrWhiteSpace(item.ActionNote))
            {
                violations.Add($"{id}: 风险等级为 {item.Risk}，ActionNote（名称后括号里的动作性质）不能为空");
            }

            if (string.IsNullOrWhiteSpace(item.RestoreHint))
            {
                violations.Add($"{id}: 风险等级为 {item.Risk}，RestoreHint（恢复方式）不能为空");
            }
        }

        // 规则 5：移入隔离区的条目必须有明确的清理目标
        if (item.ActionKind == CleanActionKind.Quarantine && targets.Count == 0)
        {
            violations.Add($"{id}: 移入隔离区的条目至少要有一个 TargetRule");
        }

        // 规则 6：命令型动作不通过文件路径工作——目标必须为空，且必须写明动作性质
        if (item.ActionKind is CleanActionKind.HibernateOff or CleanActionKind.DismComponentCleanup)
        {
            if (targets.Count > 0)
            {
                violations.Add($"{id}: {item.ActionKind} 是命令型动作，不通过文件路径工作，Targets 必须为空数组");
            }

            if (string.IsNullOrWhiteSpace(item.ActionNote))
            {
                violations.Add($"{id}: {item.ActionKind} 必须写明动作性质（ActionNote），否则用户不知道这条“不是删文件”");
            }
        }

        // 规则 7/8：目标的落地路径安全判定
        if (item.ActionKind == CleanActionKind.InformationalOnly)
        {
            // 规则 7："只展示、绝不可清理"必须由禁止清单兜底——每个目标都要命中 Denylist
            if (item.DefaultChecked)
            {
                violations.Add($"{id}: 只展示不可清理的条目不允许默认勾选");
            }

            foreach (var rule in targets)
            {
                if (!TryExpand(rule, environment, out var path, out var error))
                {
                    violations.Add($"{id}: 目标路径无法规范化（{error}），无法证明它命中禁止清单，因此不允许作为“只展示”的条目");
                    continue;
                }

                if (!Denylist.IsDenied(path))
                {
                    violations.Add($"{id}: “只展示、不可清理”的目标 {path} 不在禁止清单内——不可清理这句话必须由禁止清单兜底");
                }
            }
        }
        else if (item.ActionKind == CleanActionKind.Quarantine)
        {
            foreach (var rule in targets)
            {
                if (!TryExpand(rule, environment, out var path, out var error))
                {
                    violations.Add($"{id}: 目标路径无法规范化（{error}）：{rule.Path}");
                    continue;
                }

                // 规则 8-1：目标根绝不能落在禁止目录树内（System32 / Program Files / WinSxS …）
                if (Denylist.IsDeniedTree(path))
                {
                    violations.Add($"{id}: 目标 {path} 落在禁止清单的目录树内——{Denylist.ExplainDenial(path)}");
                }

                // 规则 8-2：用户目录"根本身"只能作为非递归目标，禁止整目录递归（需求 4.1-6）
                if (Denylist.IsDeniedUserRoot(path) &&
                    rule.Kind is not (TargetKind.DirectoryContents or TargetKind.FileGlob))
                {
                    violations.Add($"{id}: 目标 {path} 是用户目录根本身，枚举方式只能是 DirectoryContents 或 FileGlob（当前为 {rule.Kind}），不允许整目录递归");
                }
            }
        }

        // 规则 9：回收站单列一档，必须有且只有一个 RecycleBin 目标
        if (item.Category == CleanCategory.RecycleBin)
        {
            var recycleBinTargets = targets.Count(rule => rule.Kind == TargetKind.RecycleBin);
            if (recycleBinTargets != 1)
            {
                violations.Add($"{id}: 回收站条目必须有且只有一个 TargetKind.RecycleBin 目标（当前 {recycleBinTargets} 个）");
            }
        }

        // 规则 10：全库文本不得出现系统还原点/卷影副本相关字样（I-5）
        foreach (var text in AllText(item, targets))
        {
            var match = ForbiddenTextPattern.Match(text);
            if (match.Success)
            {
                violations.Add($"{id}: 文本命中禁止字样“{match.Value}”（系统还原点与卷影副本永不进入清理清单）：{text}");
            }
        }
    }

    /// <summary>展开并规范化目标路径；失败时返回 false 与可直接展示的中文原因。</summary>
    private static bool TryExpand(TargetRule rule, IEnvironmentProbe environment, out string path, out string error) =>
        PathNormalizer.TryNormalize(rule.Path, environment, out path, out error);

    /// <summary>条目里所有会被静态检索的文本（规则 10 的口径）。</summary>
    private static IEnumerable<string> AllText(CleanItemDefinition item, IReadOnlyList<TargetRule> targets)
    {
        yield return item.Id;
        yield return item.DisplayName;
        yield return item.ActionNote;
        yield return item.SideEffect;
        yield return item.RestoreHint;
        yield return item.AvailabilityNote ?? string.Empty;

        foreach (var rule in targets)
        {
            yield return rule.Path;
            yield return rule.Pattern;
        }
    }
}
