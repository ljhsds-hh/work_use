using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Logging;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Safety;

namespace SpaceMaid.Core.Scanning.Selectors;

/// <summary>
/// `l3.orphan-app-dirs`（卸载残留目录）的候选收窄器——**全清单里最保守的一项**。
///
/// 需求 2.4 与设计文档 §5.4 的口径：列出条件四条**必须全部满足**，**任一条件无法判定就不列出**。
///
/// <list type="number">
/// <item>候选文件按其**相对根目录的第一层子目录**分组（根目录 = 该条目 Targets 里能匹配到的最深固定前缀）。
///       匹配不到任何根、或文件直接挂在根下的（那不是一个"残留目录"），一律不列出；</item>
/// <item>该目录没有被任何已安装程序引用（<see cref="IInstalledProgramIndex"/> 返回 false）；</item>
/// <item>该目录下**所有**候选文件的最后修改时间都早于"现在 - 180 天"（近期改过的一律认为还在用）；</item>
/// <item>该目录下至少有一个候选文件（空目录天然不列出）。</item>
/// </list>
///
/// <para>
/// **失败关闭**：注册表索引抛异常时按"被引用"处理（不列出）；根目录解析不出来时整组不列出。
/// 收窄器不修改传入的 <paramref name="item"/>，也不碰勾选状态。
/// </para>
/// </summary>
public sealed class OrphanDirectorySelector : IItemCandidateSelector
{
    /// <summary>对应的清理项 Id（与 CleanItemCatalog 一致，一经发布不得改名）。</summary>
    public const string ItemId = "l3.orphan-app-dirs";

    /// <summary>目录"久未修改"的判定阈值：180 天。</summary>
    public static readonly TimeSpan StaleAge = TimeSpan.FromDays(180);

    private readonly IInstalledProgramIndex _index;
    private readonly IClock _clock;
    private readonly ILogSink _log;
    private readonly IEnvironmentProbe? _environment;

    /// <param name="index">已安装程序索引（注册表卸载项的抽象）。</param>
    /// <param name="clock">时钟（180 天判定用；注入假时钟才能写出确定性用例）。</param>
    /// <param name="log">日志汇（可空；记录"为什么没列出"）。</param>
    /// <param name="environment">
    /// 环境探针，用于把条目里的 <c>%VAR%</c> 模板展开成真实根目录。
    /// 可空：为空时只接受不含变量的字面路径（解析不出来的根一律不列出，失败关闭）。
    /// </param>
    public OrphanDirectorySelector(
        IInstalledProgramIndex index,
        IClock clock,
        ILogSink? log = null,
        IEnvironmentProbe? environment = null)
    {
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? SilentLogSink.Instance;
        _environment = environment;
    }

    /// <inheritdoc />
    public bool CanHandle(string itemId) => string.Equals(itemId, ItemId, StringComparison.Ordinal);

    /// <inheritdoc />
    public IReadOnlyList<ScanFile> Select(
        CleanItemDefinition item,
        IReadOnlyList<ScanFile> candidates,
        IFileSystem fileSystem,
        ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(candidates);

        var sink = log ?? _log;

        // ① 先把条目里能确定的根目录解析出来；一个都拿不到就整项不列出（"判定不确定就不列"）
        var roots = ResolveRoots(item);
        if (roots.Count == 0)
        {
            if (candidates.Count > 0)
            {
                sink.Warn($"卸载残留：条目 {item.Id} 的根目录无法确定，本项整组不列出（保守）");
            }

            return Array.Empty<ScanFile>();
        }

        var staleBefore = _clock.Now - StaleAge;

        // ② 按"相对根目录的第一层子目录"分组；归不到任何已知根下的文件一律丢弃
        var groups = new Dictionary<string, List<ScanFile>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var file in candidates)
        {
            if (file is null || !TryGetFirstLevelDirectory(file.Path, roots, out var directory))
            {
                continue;
            }

            if (!groups.TryGetValue(directory, out var bucket))
            {
                bucket = new List<ScanFile>();
                groups[directory] = bucket;
                order.Add(directory);
            }

            bucket.Add(file);
        }

        // ③ 四条件全部满足才放行
        var result = new List<ScanFile>();
        foreach (var directory in order)
        {
            var files = groups[directory];

            if (files.Count == 0)
            {
                continue; // ④ 空目录不列出（分组由候选文件构成，这里只是显式守住该条件）
            }

            if (files.Any(file => file.LastWrite > staleBefore))
            {
                continue; // ③ 近期改过 -> 认为还在用
            }

            if (IsReferencedOrUnknown(directory, sink))
            {
                continue; // ② 注册表引用 / 判定失败 -> 不列出
            }

            result.AddRange(files);
        }

        if (candidates.Count != result.Count)
        {
            sink.Info($"卸载残留候选收窄：{candidates.Count} 个候选 → {result.Count} 个" +
                      $"（只在注册表无引用、180 天未修改、非空三项同时满足时列出）");
        }

        return result;
    }

    /// <summary>
    /// 注册表索引判定；**抛异常按"被引用"处理**（保守：宁可漏报残留，也不误删在用的目录）。
    /// </summary>
    private bool IsReferencedOrUnknown(string directory, ILogSink log)
    {
        try
        {
            return _index.IsReferenced(directory);
        }
        catch (Exception ex)
        {
            log.Warn($"卸载残留：判定目录 {directory} 是否被引用时失败，按“被引用”处理（不列出）：{ex.Message}");
            return true;
        }
    }

    /// <summary>
    /// 把条目的目标规则展开成"可比较的根目录"列表。
    /// 含未展开变量 / 通配符 / 相对路径的规则无法确定真实根，直接忽略（忽略的后果只会是"少列"）。
    /// </summary>
    private IReadOnlyList<string> ResolveRoots(CleanItemDefinition item)
    {
        var roots = new List<string>();

        foreach (var rule in item.Targets)
        {
            if (rule.Kind == TargetKind.RecycleBin)
            {
                continue;
            }

            var raw = _environment is null ? rule.Path : _environment.ExpandVariables(rule.Path);
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            raw = raw.Trim();
            if (raw.Contains('%') || raw.IndexOfAny(new[] { '*', '?' }) >= 0 || !Path.IsPathRooted(raw))
            {
                continue; // 变量没展开 / 通配 / 相对路径 -> 根不确定
            }

            try
            {
                var full = PathNormalizer.TrimTrailingSeparator(Path.GetFullPath(raw));
                if (!string.IsNullOrEmpty(full))
                {
                    roots.Add(full);
                }
            }
            catch (Exception)
            {
                // 规范化失败 -> 该条规则作废（只会少列，不会多列）
            }
        }

        return roots
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 求出一个文件相对根目录的第一层子目录（例如 <c>C:\ProgramData\OldApp\a.dat</c> → <c>C:\ProgramData\OldApp</c>）。
    /// 返回 false 表示"确定不了"：不在任何已知根下、或直接挂在根下（根下的一堆散文件不是"残留目录"）。
    /// </summary>
    private static bool TryGetFirstLevelDirectory(string filePath, IReadOnlyList<string> roots, out string directory)
    {
        directory = string.Empty;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        // 取**最深**的匹配根（条目里可能同时写了父目录与子目录）
        string? matched = null;
        foreach (var root in roots)
        {
            if (PathNormalizer.IsUnder(filePath, root)
                && (matched is null || root.Length > matched.Length))
            {
                matched = root;
            }
        }

        if (matched is null)
        {
            return false;
        }

        var relative = filePath
            .Substring(matched.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var separator = relative.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
        if (separator <= 0)
        {
            return false; // 直接挂在根下：没有"第一层子目录"，不是一个残留目录
        }

        directory = Path.Combine(matched, relative.Substring(0, separator));
        return true;
    }
}
