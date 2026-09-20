namespace SpaceMaid.Core.Safety;

/// <summary>
/// 禁止清单（需求 4.2 / 2.6，不变量 I-2）。
///
/// 关键性质：
/// 1. **以代码常量形式硬编码**，不是配置文件——任何 UI/设置/参数都不能突破；
/// 2. 判定发生在白名单判定**之前**（先拒后允），由 SafetyGate 保证顺序；
/// 3. 系统还原点/VSS 不在此列表中"被清理"，而是**代码库根本不存在相关调用**（不变量 I-5，静态检索测试守住）。
///
/// 注意：禁止清单不能写成"整个 C:\Windows"，因为我们确实要清理
/// C:\Windows\Temp、C:\Windows\Logs\CBS、C:\Windows\SoftwareDistribution\Download、
/// C:\Windows\Prefetch、C:\Windows\MEMORY.DMP、C:\Windows\Minidump —— 因此这里逐条精确列举。
/// </summary>
public static class Denylist
{
    /// <summary>系统盘根，例如 "C:\"。</summary>
    public static readonly string SystemVolumeRoot =
        Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))
        ?? @"C:\";

    /// <summary>Windows 目录，例如 "C:\Windows"。</summary>
    public static readonly string WindowsDirectory =
        PathNormalizer.TrimTrailingSeparator(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows));

    /// <summary>Program Files 两个目录（正常安装目录一律不碰）。</summary>
    public static readonly IReadOnlyList<string> ProgramFilesDirectories = new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
    }
    .Where(p => !string.IsNullOrWhiteSpace(p))
    .Select(PathNormalizer.TrimTrailingSeparator)
    .ToArray();

    /// <summary>永远不碰的目录树（自身与其全部子路径）。</summary>
    public static readonly IReadOnlyList<string> DeniedTrees = BuildDeniedTrees();

    /// <summary>永远不碰的单个文件（精确匹配）。</summary>
    public static readonly IReadOnlyList<string> DeniedFiles = new[]
    {
        Path.Combine(SystemVolumeRoot, "hiberfil.sys"),   // 只能通过 powercfg /h off 释放（需求 3.8）
        Path.Combine(SystemVolumeRoot, "pagefile.sys"),
        Path.Combine(SystemVolumeRoot, "swapfile.sys"),
        Path.Combine(SystemVolumeRoot, "DumpStack.log"),
        Path.Combine(SystemVolumeRoot, "DumpStack.log.tmp")
    };

    /// <summary>用户目录的根本身不允许作为清理目标（其具体子文件可以由明确条目处理）。</summary>
    public static readonly IReadOnlyList<string> DeniedUserRoots = BuildDeniedUserRoots();

    /// <summary>路径中出现这些目录名的任何一段即拒绝（凭据/版本库/系统还原存储）。</summary>
    public static readonly IReadOnlyList<string> DeniedSegments = new[]
    {
        ".ssh",
        ".git",
        ".gnupg",
        "System Volume Information"
    };

    /// <summary>禁止清单判定。入参应为已规范化的绝对路径。</summary>
    public static bool IsDenied(string normalizedPath)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return true; // 空路径一律视为不安全
        }

        foreach (var tree in DeniedTrees)
        {
            if (PathNormalizer.IsSameOrUnder(normalizedPath, tree))
            {
                return true;
            }
        }

        foreach (var file in DeniedFiles)
        {
            if (normalizedPath.Equals(PathNormalizer.TrimTrailingSeparator(file), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var root in DeniedUserRoots)
        {
            if (normalizedPath.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var segment in DeniedSegments)
        {
            if (PathNormalizer.ContainsSegment(normalizedPath, segment))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>把命中原因写清楚，便于日志与界面展示。</summary>
    public static string ExplainDenial(string normalizedPath)
    {
        foreach (var tree in DeniedTrees)
        {
            if (PathNormalizer.IsSameOrUnder(normalizedPath, tree))
            {
                return $"命中禁止清单（系统目录，永不清除）：{tree}";
            }
        }

        foreach (var file in DeniedFiles)
        {
            if (normalizedPath.Equals(PathNormalizer.TrimTrailingSeparator(file), StringComparison.OrdinalIgnoreCase))
            {
                return $"命中禁止清单（受保护的系统文件）：{normalizedPath}";
            }
        }

        foreach (var root in DeniedUserRoots)
        {
            if (normalizedPath.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                return $"命中禁止清单（用户目录本身，只允许处理其下明确列出的文件）：{normalizedPath}";
            }
        }

        foreach (var segment in DeniedSegments)
        {
            if (PathNormalizer.ContainsSegment(normalizedPath, segment))
            {
                return $"命中禁止清单（凭据/版本库/还原点存储，永不清除）：{segment}";
            }
        }

        return "命中禁止清单";
    }

    private static IReadOnlyList<string> BuildDeniedTrees()
    {
        var trees = new List<string>();

        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                trees.Add(PathNormalizer.TrimTrailingSeparator(path));
            }
        }

        foreach (var sub in new[] { "System32", "SysWOW64", "WinSxS", "Installer", "Fonts", "assembly", "servicing", "Microsoft.NET" })
        {
            Add(Path.Combine(WindowsDirectory, sub));
        }

        Add(Path.Combine(WindowsDirectory, "System32", "DriverStore"));
        Add(Path.Combine(WindowsDirectory, "System32", "config"));
        Add(Path.Combine(WindowsDirectory, "System32", "catroot"));
        Add(Path.Combine(WindowsDirectory, "System32", "spool"));
        Add(Path.Combine(WindowsDirectory, "System32", "winevt"));
        Add(Path.Combine(WindowsDirectory, "System32", "LogFiles"));
        Add(Path.Combine(WindowsDirectory, "Boot"));
        Add(Path.Combine(WindowsDirectory, "assembly"));
        Add(Path.Combine(WindowsDirectory, "SystemResources"));

        foreach (var pf in ProgramFilesDirectories)
        {
            Add(pf);
        }

        // 每卷的还原点/卷影副本存储（不变量 I-5）
        Add(Path.Combine(SystemVolumeRoot, "System Volume Information"));
        Add(Path.Combine(SystemVolumeRoot, "Recovery"));

        return trees.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> BuildDeniedUserRoots()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var roots = new List<string>
        {
            userProfile,
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            roots.Add(Path.Combine(userProfile, "Downloads"));
            roots.Add(Path.Combine(userProfile, "OneDrive"));
        }

        return roots
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(PathNormalizer.TrimTrailingSeparator)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
