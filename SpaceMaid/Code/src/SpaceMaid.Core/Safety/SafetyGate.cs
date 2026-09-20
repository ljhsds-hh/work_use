using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Logging;
using SpaceMaid.Core.Models;

namespace SpaceMaid.Core.Safety;

/// <summary>
/// 落地授权闸门：**全项目唯一的落地授权入口**（不变量 I-1）。
///
/// 判定顺序固定为「先拒后允」（设计文档 3.4）：
///   ① 规范化失败 → UnsafePath
///   ② 禁止清单   → DeniedByDenylist
///   ③ 重解析点   → ReparsePoint
///   ④ 白名单子树 → OutsideAllowlist
/// 顺序不可调整：即使某个清理项"允许"了某个系统目录，也永远越不过禁止清单。
/// </summary>
public sealed class SafetyGate
{
    private readonly IFileSystem _fileSystem;
    private readonly IEnvironmentProbe _environment;
    private readonly ILogSink _log;

    // 祖先重解析点检查代价较高（每个候选文件要向上走几层），这里做进程内缓存。
    private readonly Dictionary<string, bool> _reparsePointCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheLock = new();

    public SafetyGate(IFileSystem fileSystem, IEnvironmentProbe environment, ILogSink? log = null)
    {
        _fileSystem = fileSystem;
        _environment = environment;
        _log = log ?? SilentLogSink.Instance;
    }

    /// <summary>对单个候选路径做落地授权。</summary>
    public SafetyDecision Authorize(string candidatePath, CleanItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(item);

        // ① 规范化
        if (!PathNormalizer.TryNormalize(candidatePath, _environment, out var normalized, out var error))
        {
            return new SafetyDecision(SafetyVerdict.UnsafePath, error);
        }

        // ② 禁止清单（先拒后允里的"先拒"）
        if (Denylist.IsDenied(normalized))
        {
            return new SafetyDecision(SafetyVerdict.DeniedByDenylist, Denylist.ExplainDenial(normalized));
        }

        // ③ 重解析点：拒绝顺着符号链接/junction 操作
        if (IsOrHasReparsePoint(normalized))
        {
            return new SafetyDecision(
                SafetyVerdict.ReparsePoint,
                $"路径或其上级是符号链接/junction，拒绝继续操作：{normalized}");
        }

        // ④ 白名单：必须位于该清理项某条目标规则的允许范围内（支持子目录通配模式）
        foreach (var target in item.Targets)
        {
            if (TargetPathMatcher.IsAllowed(normalized, target, _environment))
            {
                return SafetyDecision.Allow($"位于清理项目标范围内：{target.Path}");
            }
        }

        return new SafetyDecision(
            SafetyVerdict.OutsideAllowlist,
            $"不在清理项「{item.DisplayName}」的允许范围内：{normalized}");
    }

    /// <summary>批量预检：任何一个路径不安全就返回该决策（供执行前自检使用）。</summary>
    public SafetyDecision AuthorizeAll(IEnumerable<(string Path, CleanItemDefinition Item)> candidates)
    {
        foreach (var (path, item) in candidates)
        {
            var decision = Authorize(path, item);
            if (!decision.IsAllowed)
            {
                return decision;
            }
        }

        return SafetyDecision.Allow("全部候选路径通过安全闸门");
    }

    private bool IsOrHasReparsePoint(string normalizedPath)
    {
        lock (_cacheLock)
        {
            if (_reparsePointCache.TryGetValue(normalizedPath, out var cached))
            {
                return cached;
            }
        }

        var result = false;
        try
        {
            result = _fileSystem.IsReparsePoint(normalizedPath) || _fileSystem.HasReparsePointAncestor(normalizedPath);
        }
        catch (Exception ex)
        {
            // 无法判定时按"不安全"处理（保守原则）
            _log.Warn($"重解析点检测失败，按不安全处理：{normalizedPath}（{ex.Message}）");
            result = true;
        }

        lock (_cacheLock)
        {
            if (_reparsePointCache.Count > 20_000)
            {
                _reparsePointCache.Clear();
            }

            _reparsePointCache[normalizedPath] = result;
        }

        return result;
    }
}
