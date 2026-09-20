using SpaceMaid.Core.Abstractions;

namespace SpaceMaid.Core.Safety;

/// <summary>
/// 路径规范化：把用户/条目给出的路径变成可比较的绝对路径。
/// 规则顺序固定（设计文档 3.2），任一步失败即判不安全。
/// 特别注意：**不解析符号链接/junction 的目标**——顺链接操作会删到别处，重解析点交给 SafetyGate 拒绝。
/// </summary>
public static class PathNormalizer
{
    /// <summary>把原始路径规范化。失败时 <paramref name="error"/> 为可直接展示的中文原因。</summary>
    public static bool TryNormalize(
        string? raw,
        IEnvironmentProbe environment,
        out string normalized,
        out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "路径为空";
            return false;
        }

        var expanded = environment.ExpandVariables(raw.Trim());

        if (expanded.Contains('%'))
        {
            error = $"路径含无法展开的环境变量：{raw}";
            return false;
        }

        if (expanded.IndexOfAny(new[] { '*', '?' }) >= 0)
        {
            error = $"落地路径不允许通配符：{raw}";
            return false;
        }

        if (!Path.IsPathRooted(expanded))
        {
            error = $"必须是绝对路径：{raw}";
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(expanded);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"路径无法规范化：{raw}";
            return false;
        }

        normalized = TrimTrailingSeparator(full);
        return true;
    }

    /// <summary>去掉尾部目录分隔符（卷根除外），便于前缀比较。</summary>
    public static string TrimTrailingSeparator(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        var root = Path.GetPathRoot(path) ?? string.Empty;
        if (path.Length <= root.Length)
        {
            return path;
        }

        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// path 是否严格位于 root 之下（不含 root 自身）。大小写不敏感，按分隔符边界比较，杜绝 "C:\TempEvil" 匹配 "C:\Temp"。
    /// </summary>
    /// <remarks>
    /// **卷根必须特殊处理**（这曾经是个真实缺陷）：root 写成 <c>"C:\"</c> 时，用原样的 root 参与边界比较会去读
    /// <c>p[3]</c>——那是下一级目录名的首字符而不是分隔符，于是"C 盘下的任何路径"都会被判成**不在 root 之下**。
    /// 回收站清理项的目标根正是 <c>%SystemDrive%\</c>，所以整档会被安全闸门 100% 拒绝（需求 2.5 形同未实现）。
    /// 正确做法是先用**去掉尾部分隔符**的 root 做前缀比较，再检查边界字符。
    /// </remarks>
    public static bool IsUnder(string path, string root)
    {
        var p = TrimTrailingSeparator(path);
        var rootFull = TrimTrailingSeparator(root);

        // 自身不算"之下"（同样适用于卷根："C:\" 不在 "C:\" 之下）
        if (p.Equals(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var r = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (r.Length == 0 || p.Length <= r.Length)
        {
            return false;
        }

        if (!p.StartsWith(r, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var boundary = p[r.Length];
        return boundary == Path.DirectorySeparatorChar || boundary == Path.AltDirectorySeparatorChar;
    }

    /// <summary>路径是否等于 root 或位于 root 之下（用于"命中禁止目录"判定）。</summary>
    public static bool IsSameOrUnder(string path, string root)
    {
        var p = TrimTrailingSeparator(path);
        var r = TrimTrailingSeparator(root);
        return p.Equals(r, StringComparison.OrdinalIgnoreCase) || IsUnder(p, r);
    }

    /// <summary>逐段比较，判断路径是否包含某个目录名（例如 .ssh / .git / System Volume Information）。</summary>
    public static bool ContainsSegment(string path, string segment)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(segment))
        {
            return false;
        }

        var parts = path.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        return parts.Any(p => p.Equals(segment, StringComparison.OrdinalIgnoreCase));
    }
}
