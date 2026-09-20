using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Logging;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Safety;

namespace SpaceMaid.Core.Scanning;

/// <summary>
/// 扫描引擎：只读遍历清理项、统计体积与文件数（需求 3.1、3.9-1）。
///
/// 安全性质：
/// 1. **只读**——本类只调用 IFileSystem 的枚举/统计成员，绝不移动、删除、改名、改属性（有单测守住）；
/// 2. **可中断**——每处理若干文件检查一次取消令牌；
/// 3. **不猜**——目录不存在时条目判为不可用（Available=false），而不是当成"0 字节"；
/// 4. 落地前还会再经 SafetyGate（本类不代替安全闸门，只负责收集候选）。
/// </summary>
public sealed class ScanEngine : IScanEngine
{
    /// <summary>每处理这么多文件检查一次取消，兼顾响应速度与开销。</summary>
    private const int CancellationCheckInterval = 64;

    private readonly IFileSystem _fileSystem;
    private readonly IEnvironmentProbe _environment;
    private readonly IVolumeProbe _volumes;
    private readonly IClock _clock;
    private readonly IVolumeCapacityProbe? _capacity;
    private readonly IRecycleBinScanner? _recycleBin;
    private readonly ILogSink _log;
    private readonly IReadOnlyList<IItemCandidateSelector> _selectors;

    /// <param name="selectors">
    /// 各清理项的"候选收窄器"（可选）。**它们只能把候选集收窄，不能扩大**——
    /// 收窄器返回值中不属于原候选集的文件会被本类直接丢弃；抛异常时该条目按空集处理。
    /// 默认 null = 不启用任何收窄（保持历史行为，既有调用点无需改动）。
    /// </param>
    public ScanEngine(
        IFileSystem fileSystem,
        IEnvironmentProbe environment,
        IVolumeProbe volumes,
        IClock clock,
        IVolumeCapacityProbe? capacity = null,
        IRecycleBinScanner? recycleBin = null,
        ILogSink? log = null,
        IEnumerable<IItemCandidateSelector>? selectors = null)
    {
        _fileSystem = fileSystem;
        _environment = environment;
        _volumes = volumes;
        _clock = clock;
        _capacity = capacity;
        _recycleBin = recycleBin;
        _log = log ?? SilentLogSink.Instance;
        _selectors = selectors?.Where(selector => selector is not null).ToList() ?? new List<IItemCandidateSelector>();
    }

    public Task<ScanReport> ScanAsync(
        ScanRequest request,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var now = _clock.Now;
        var entries = new List<ScanEntry>();
        long processedBytes = 0;
        var processedFiles = 0;

        foreach (var item in request.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (candidates, unavailableReason) = CollectCandidates(item, request.IncludeRecycleBin, cancellationToken);
            var narrowed = ApplySelectors(item, candidates, cancellationToken);
            var outcome = ScanningRules.Apply(item, narrowed, now, GetTargetCreationTime(item));

            var totalBytes = outcome.Files.Sum(f => f.Size);
            processedBytes += totalBytes;
            processedFiles += outcome.Files.Count;

            var available = unavailableReason is null;
            entries.Add(new ScanEntry(
                outcome.Item,
                totalBytes,
                outcome.Files.Count,
                outcome.SkippedCount,
                outcome.Files,
                available,
                unavailableReason ?? outcome.Note));

            progress?.Report(new ScanProgress(item.Id, item.DisplayName, processedBytes, processedFiles));

            if (!available)
            {
                _log.Info($"扫描：条目 {item.Id} 不可用（{unavailableReason}）");
            }
        }

        var systemVolume = _volumes.GetVolumeOf(_environment.SystemDrive + Path.DirectorySeparatorChar);
        var snapshot = new VolumeSnapshot(
            systemVolume,
            _capacity?.GetTotalBytes(systemVolume) ?? 0,
            _volumes.GetFreeBytes(systemVolume));

        return Task.FromResult(new ScanReport(entries, snapshot, now));
    }

    /// <summary>
    /// 按目标规则收集候选文件。返回 (候选, 不可用原因)；不可用原因为 null 表示条目可用。
    /// </summary>
    private (List<ScanFile> Candidates, string? UnavailableReason) CollectCandidates(
        CleanItemDefinition item,
        bool includeRecycleBin,
        CancellationToken cancellationToken)
    {
        var candidates = new List<ScanFile>();
        var missing = new List<string>();
        var counter = 0;

        foreach (var rule in item.Targets)
        {
            if (rule.Kind == TargetKind.RecycleBin)
            {
                if (!includeRecycleBin)
                {
                    continue;
                }

                if (_recycleBin is null)
                {
                    return (candidates, "回收站扫描器未启用");
                }

                candidates.AddRange(_recycleBin.Scan(includeOtherDrives: false));
                continue;
            }

            if (!PathNormalizer.TryNormalize(_environment.ExpandVariables(rule.Path), _environment, out var root, out var pathError))
            {
                missing.Add(pathError);
                continue;
            }

            switch (rule.Kind)
            {
                case TargetKind.FixedFile:
                    if (_fileSystem.FileExists(root))
                    {
                        candidates.Add(ToScanFile(root, item, _fileSystem.GetLastWriteTime(root), _fileSystem.GetFileSize(root)));
                    }
                    else
                    {
                        missing.Add(root);
                    }

                    break;

                case TargetKind.FileGlob:
                case TargetKind.DirectoryContents:
                case TargetKind.DirectoryTree:
                    // 子目录模式（如 *\LocalCache\Temp）先展开成真实目录；无通配时就是 root 本身
                    var directories = TargetPathMatcher.HasWildcard(rule)
                        ? TargetPathMatcher.ExpandDirectories(rule, _fileSystem, _environment)
                        : (_fileSystem.DirectoryExists(root) ? new[] { root } : Array.Empty<string>());

                    if (directories.Count == 0)
                    {
                        missing.Add(root);
                        break;
                    }

                    var recurse = rule.Kind == TargetKind.DirectoryTree || rule.Recurse;
                    foreach (var directory in directories)
                    {
                        foreach (var file in _fileSystem.EnumerateFiles(directory, rule.Pattern, recurse))
                        {
                            if (++counter % CancellationCheckInterval == 0)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                            }

                            candidates.Add(ToScanFile(file, item, _fileSystem.GetLastWriteTime(file), _fileSystem.GetFileSize(file)));
                        }
                    }

                    break;

                default:
                    missing.Add($"不支持的枚举方式：{rule.Kind}");
                    break;
            }
        }

        if (candidates.Count == 0 && missing.Count > 0)
        {
            return (candidates, $"目标路径不存在或不可用：{string.Join("；", missing.Take(3))}");
        }

        return (candidates, null);
    }

    /// <summary>
    /// 把候选集交给匹配的收窄器（扫描层唯一允许"减少候选"的扩展点）。
    ///
    /// 安全性质：
    /// ① **只收窄**——收窄器返回值里凡是"不在当前候选集里"的文件一律丢弃（防止某个实现凭空放行文件）；
    /// ② **失败关闭**——收窄器抛异常时该项按"什么都判定不了"处理，返回空集，
    ///    绝不退化成"异常了就全都要"；
    /// ③ 多个收窄器同时命中同一项时按顺序依次收窄（交集），仍然只会越收越少。
    /// </summary>
    private List<ScanFile> ApplySelectors(
        CleanItemDefinition item,
        List<ScanFile> candidates,
        CancellationToken cancellationToken)
    {
        var current = candidates;

        foreach (var selector in _selectors)
        {
            if (!selector.CanHandle(item.Id))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<ScanFile> selected;
            try
            {
                selected = selector.Select(item, current, _fileSystem, _log) ?? Array.Empty<ScanFile>();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"条目 {item.Id} 的候选收窄器 {selector.GetType().Name} 执行失败，" +
                           "本项按“无法判定”处理（不列出任何文件）", ex);
                return new List<ScanFile>();
            }

            var allowed = new HashSet<string>(current.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
            var narrowed = new List<ScanFile>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rejected = 0;

            foreach (var file in selected)
            {
                if (file is null || !allowed.Contains(file.Path) || !seen.Add(file.Path))
                {
                    rejected++;
                    continue;
                }

                narrowed.Add(file);
            }

            if (rejected > 0)
            {
                // 收窄器只能收窄：不属于原候选集（或重复）的条目一律不算数。
                _log.Warn($"候选收窄器 {selector.GetType().Name} 返回了 {rejected} 个不在候选集内的条目，" +
                          $"已丢弃（收窄器只允许收窄，条目 {item.Id}）");
            }

            if (narrowed.Count != current.Count)
            {
                _log.Info($"条目 {item.Id} 候选收窄：{current.Count} → {narrowed.Count}");
            }

            current = narrowed;
        }

        return current;
    }

    private ScanFile ToScanFile(string path, CleanItemDefinition item, DateTimeOffset lastWrite, long size) =>
        new(path, size, lastWrite, item.ActionKind);

    /// <summary>Windows.old 的 10 天窗口判定需要的目录创建时间（其他条目返回 null）。</summary>
    private DateTimeOffset? GetTargetCreationTime(CleanItemDefinition item)
    {
        if (item.Id != ScanningRules.WindowsOldItemId)
        {
            return null;
        }

        foreach (var rule in item.Targets)
        {
            if (PathNormalizer.TryNormalize(_environment.ExpandVariables(rule.Path), _environment, out var root, out _)
                && _fileSystem.DirectoryExists(root))
            {
                var createdAt = _fileSystem.GetCreationTime(root);
                return createdAt == DateTimeOffset.MinValue ? null : createdAt;
            }
        }

        return null;
    }
}
