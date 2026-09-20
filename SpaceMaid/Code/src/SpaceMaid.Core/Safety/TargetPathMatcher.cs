using System.Text.RegularExpressions;
using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Models;

namespace SpaceMaid.Core.Safety;

/// <summary>
/// 目标路径匹配：把 <see cref="TargetRule"/> 翻译成"某个候选文件是否在允许范围内"的判定。
///
/// 两种形态：
/// 1. **无通配**（绝大多数）：等价于"必须是 Path 的子树"——沿用最快的字符串前缀比较；
/// 2. **有通配**（<see cref="TargetRule.SubPathPattern"/> 非空，如 <c>*\LocalCache\Temp</c>）：
///    把模式编译成正则，只允许通配段在**目录**上展开，文件名仍按 Pattern 匹配。
///
/// 安全性质：正则一律**全串匹配**（^…$），不使用部分匹配，杜绝 "C:\TempEvil" 命中 "C:\Temp" 这类越界。
/// </summary>
public static class TargetPathMatcher
{
    /// <summary>该规则是否含通配段。</summary>
    public static bool HasWildcard(TargetRule rule) =>
        !string.IsNullOrWhiteSpace(rule.SubPathPattern) && rule.SubPathPattern.Contains('*');

    /// <summary>
    /// 取"最深的固定前缀"：不含通配符、可安全做禁止清单校验与实际存在性检查的那一段。
    /// </summary>
    public static string GetFixedPrefix(TargetRule rule, IEnvironmentProbe environment)
    {
        var expanded = environment.ExpandVariables(rule.Path);
        var segments = SplitSegments(expanded);
        var kept = new List<string>();

        foreach (var segment in segments)
        {
            if (segment.Contains('*'))
            {
                break;
            }

            kept.Add(segment);
        }

        return kept.Count == 0 ? expanded : string.Join(Path.DirectorySeparatorChar, kept);
    }

    /// <summary>候选文件是否落在该规则允许的范围内。</summary>
    public static bool IsAllowed(string candidatePath, TargetRule rule, IEnvironmentProbe environment)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var expandedPath = environment.ExpandVariables(rule.Path);
        if (!PathNormalizer.TryNormalize(expandedPath, environment, out var root, out _))
        {
            return false;
        }

        var normalizedCandidate = candidatePath;

        if (rule.Kind == TargetKind.FixedFile)
        {
            // 固定文件不接受任何通配；必须是同一条路径
            return normalizedCandidate.Equals(root, StringComparison.OrdinalIgnoreCase);
        }

        if (!HasWildcard(rule))
        {
            // 规则路径自身含通配（历史写法）：无法规范化，直接拒绝（fail-closed）
            if (expandedPath.Contains('*'))
            {
                return false;
            }

            return PathNormalizer.IsUnder(normalizedCandidate, root)
                   && MatchesFileName(normalizedCandidate, rule.Pattern);
        }

        var directoryPattern = BuildDirectoryPattern(rule, environment);
        if (directoryPattern is null)
        {
            return false;
        }

        if (rule.Kind == TargetKind.DirectoryTree)
        {
            // 允许目录之下的任意层级，但不允许目录本身
            return Regex.IsMatch(normalizedCandidate, "^" + directoryPattern + @"\\.+$", RegexOptions.IgnoreCase);
        }

        return Regex.IsMatch(
            normalizedCandidate,
            "^" + directoryPattern + @"\\" + GlobToRegex(Path.GetFileName(rule.Pattern)) + "$",
            RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// 枚举规则实际指向的目录集合（用于扫描）。通配段只会展开成**目录名**匹配。
    /// 返回的目录都已确认存在。
    /// </summary>
    public static IReadOnlyList<string> ExpandDirectories(
        TargetRule rule,
        IFileSystem fileSystem,
        IEnvironmentProbe environment)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(fileSystem);

        var template = environment.ExpandVariables(rule.Path);
        if (!string.IsNullOrWhiteSpace(rule.SubPathPattern))
        {
            template = template.TrimEnd(Path.DirectorySeparatorChar)
                       + Path.DirectorySeparatorChar
                       + rule.SubPathPattern.TrimStart(Path.DirectorySeparatorChar);
        }

        var segments = SplitSegments(template);
        if (segments.Count == 0)
        {
            return Array.Empty<string>();
        }

        // 起始：卷根（第一段是 "C:" 这类）
        var current = new List<string>();
        var rootSegment = segments[0];
        current.Add(rootSegment + Path.DirectorySeparatorChar);

        for (var i = 1; i < segments.Count; i++)
        {
            var segment = segments[i];
            var next = new List<string>();

            foreach (var directory in current)
            {
                if (segment.Contains('*'))
                {
                    foreach (var child in fileSystem.EnumerateDirectories(directory))
                    {
                        if (MatchesName(Path.GetFileName(child), segment))
                        {
                            next.Add(child);
                        }
                    }
                }
                else
                {
                    var candidate = Path.Combine(directory, segment);
                    if (fileSystem.DirectoryExists(candidate))
                    {
                        next.Add(candidate);
                    }
                }
            }

            current = next;
            if (current.Count == 0)
            {
                return Array.Empty<string>();
            }
        }

        return current;
    }

    /// <summary>构造"匹配目录"的正则主体（不含首尾锚点）。</summary>
    private static string? BuildDirectoryPattern(TargetRule rule, IEnvironmentProbe environment)
    {
        var template = environment.ExpandVariables(rule.Path);
        if (!string.IsNullOrWhiteSpace(rule.SubPathPattern))
        {
            template = template.TrimEnd(Path.DirectorySeparatorChar)
                       + Path.DirectorySeparatorChar
                       + rule.SubPathPattern.TrimStart(Path.DirectorySeparatorChar);
        }

        var segments = SplitSegments(template);
        if (segments.Count == 0)
        {
            return null;
        }

        var body = segments[0].Replace(":", ":");
        var parts = new List<string> { Regex.Escape(body) };

        for (var i = 1; i < segments.Count; i++)
        {
            parts.Add(GlobToRegex(segments[i]));
        }

        return string.Join(@"\\", parts);
    }

    private static bool MatchesFileName(string path, string pattern) =>
        MatchesName(Path.GetFileName(path), pattern);

    /// <summary>名字匹配：支持 <c>*</c> 与 <c>?</c>，只比较文件名（不含目录）。</summary>
    private static bool MatchesName(string name, string pattern)
    {
        if (string.IsNullOrEmpty(pattern) || pattern == "*")
        {
            return true;
        }

        return Regex.IsMatch(name, "^" + GlobToRegex(pattern) + "$", RegexOptions.IgnoreCase);
    }

    /// <summary>把单段通配（<c>*</c>/<c>?</c>）转成正则片段。</summary>
    internal static string GlobToRegex(string glob)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var ch in glob)
        {
            switch (ch)
            {
                case '*':
                    builder.Append("[^\\\\]*");
                    break;
                case '?':
                    builder.Append("[^\\\\]");
                    break;
                default:
                    builder.Append(Regex.Escape(ch.ToString()));
                    break;
            }
        }

        return builder.ToString();
    }

    private static List<string> SplitSegments(string path) =>
        path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
}
