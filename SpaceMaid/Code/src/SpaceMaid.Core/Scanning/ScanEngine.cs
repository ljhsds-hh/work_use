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
    private readonly int _maxFilesPerItem;
    private readonly TimeSpan _itemTimeBudget;

    /// <summary>
    /// 单项候选枚举上限（默认 5 万）。
    ///
    /// 实测依据：默认值曾是 20 万，而在真实机器上光是 `~\.nuget\packages`、`~\.gradle\caches`
    /// 这类开发缓存就有十几万个文件，每个文件要读两次属性（修改时间 + 体积）——
    /// 单这一项就能让扫描卡住好几分钟。5 万是"能覆盖绝大多数机器、又不至于让用户等到怀疑人生"的折中；
    /// 达到上限时**如实标注"结果可能不完整"**（对抗式评审 F-12）。
    /// </summary>
    public const int MaxFilesPerItem = 20_000;

    /// <summary>
    /// 单项扫描的时间预算（默认 15 秒）。
    /// 实测依据：`%LOCALAPPDATA%\pnpm\store` 这类内容寻址缓存可达十几万个小文件，
    /// 单是枚举就远超 20 秒——没有时间预算，一次性扫描就会卡在这种目录上。
    /// 超时不是失败：已收集的部分照常处理，并在条目上如实标注"结果可能不完整"。
    /// </summary>
    public static readonly TimeSpan DefaultItemTimeBudget = TimeSpan.FromSeconds(15);

    private static readonly string TruncationNote =
        $"该目录规模超出本工具的单项上限（{MaxFilesPerItem} 个文件或 {DefaultItemTimeBudget.TotalSeconds:0} 秒），本次结果可能不完整（建议先在文件管理器里看看这个目录的规模）";

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
        IEnumerable<IItemCandidateSelector>? selectors = null,
        int maxFilesPerItem = MaxFilesPerItem,
        TimeSpan? itemTimeBudget = null)
    {
        _fileSystem = fileSystem;
        _environment = environment;
        _volumes = volumes;
        _clock = clock;
        _capacity = capacity;
        _recycleBin = recycleBin;
        _log = log ?? SilentLogSink.Instance;
        _selectors = selectors?.Where(selector => selector is not null).ToList() ?? new List<IItemCandidateSelector>();
        _maxFilesPerItem = maxFilesPerItem > 0 ? maxFilesPerItem : MaxFilesPerItem;
        _itemTimeBudget = itemTimeBudget ?? DefaultItemTimeBudget;
    }

    /// <summary>
    /// 扫描入口。**必须跑在线程池上**（这曾经是个真实缺陷）：扫描是纯同步 IO 长任务，
    /// 若在 UI 线程上同步跑完再返回一个"已完成的任务"，界面会整段冻结、"取消扫描"按钮点不动，
    /// 进度回调也要等扫描结束才被派发。因此这里统一 <c>Task.Run</c>，让调用方 <c>await</c> 时 UI 仍然响应。
    /// </summary>
    public Task<ScanReport> ScanAsync(
        ScanRequest request,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() => Scan(request, progress, cancellationToken), CancellationToken.None);
    }

    /// <summary>同步扫描核心（线程池内执行；单测可直接调用）。</summary>
    internal ScanReport Scan(
        ScanRequest request,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = _clock.Now;
        var entries = new List<ScanEntry>();
        long processedBytes = 0;
        var processedFiles = 0;

        foreach (var item in request.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (candidates, unavailableReason, truncated) = CollectCandidates(item, request.IncludeRecycleBin, cancellationToken);
            var narrowed = ApplySelectors(item, candidates, cancellationToken);
            var outcome = ScanningRules.Apply(item, narrowed, now, GetTargetCreationTime(item));

            var totalBytes = outcome.Files.Sum(f => f.Size);
            processedBytes += totalBytes;
            processedFiles += outcome.Files.Count;

            // 说明文字优先级：不可用原因 > 规则提示 > 枚举上限提示
            var note = unavailableReason ?? outcome.Note;
            if (truncated)
            {
                note = string.IsNullOrWhiteSpace(note) ? TruncationNote : $"{note}；{TruncationNote}";
            }

            var available = unavailableReason is null;
            entries.Add(new ScanEntry(
                outcome.Item,
                totalBytes,
                outcome.Files.Count,
                outcome.SkippedCount,
                outcome.Files,
                available,
                note)
            {
                // 明确保留不处理的那一份（例如最近一次蓝屏转储）：必须一路传到清单，用户才看得到"保留：xxx"
                Kept = outcome.Kept
            });

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

        return new ScanReport(entries, snapshot, now);
    }

    /// <summary>
    /// 按目标规则收集候选文件。返回 (候选, 不可用原因)；不可用原因为 null 表示条目可用。
    /// </summary>
    private (List<ScanFile> Candidates, string? UnavailableReason, bool Truncated) CollectCandidates(
        CleanItemDefinition item,
        bool includeRecycleBin,
        CancellationToken cancellationToken)
    {
        var candidates = new List<ScanFile>();
        var missing = new List<string>();
        var counter = 0;
        var truncated = false;
        var budget = System.Diagnostics.Stopwatch.StartNew();

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
                    return (candidates, "回收站扫描器未启用", false);
                }

                candidates.AddRange(_recycleBin.Scan(includeOtherDrives: includeRecycleBin));
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
                        // 用惰性枚举：每产出一个文件就能检查取消；一次性的 EnumerateFiles 会把整棵树先物化，
                        // 真实机器上大目录树会让扫描"卡在枚举里"，取消按钮形同虚设（实测缺陷）
                        foreach (var file in _fileSystem.EnumerateFilesStreaming(directory, rule.Pattern, recurse, cancellationToken))
                        {
                            if (++counter % CancellationCheckInterval == 0)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                            }

                            if (candidates.Count >= _maxFilesPerItem || budget.Elapsed > _itemTimeBudget)
                            {
                                // 两条界限：文件数上限与时间预算。畸形目录树（十几万小文件、junction 环）
                                // 不能让整次扫描变成"假死"；已收集的部分照常处理，并如实标注"可能不完整"。
                                truncated = true;
                                return (candidates, null, truncated);
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
            return (candidates, $"目标路径不存在或不可用：{string.Join("；", missing.Take(3))}", false);
        }

        return (candidates, null, truncated);
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
