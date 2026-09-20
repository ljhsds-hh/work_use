using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Logging;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Quarantine;
using SpaceMaid.Core.Safety;

namespace SpaceMaid.Core.Execution;

/// <summary>
/// 执行器：把**清单**里勾选的文件搬进隔离区，并如实报告每个项目的去向。
///
/// 三条不可破坏的性质：
/// 1. **只处理清单里的文件**（设计决策 D-7）：执行器不枚举任何目录，
///    因此不可能出现"清单里没有、执行时却删了"的情况；
/// 2. **每一项都要先拿到清理定义**：定义（含允许路径）来自扫描报告，取不到就整项跳过——
///    没有白名单就没有删除，安全闸门缺失时宁可什么都不做；
/// 3. **隔离区路径先预检**：路径不合格直接中止，一个文件都不动（需求 7 章第 7 条）。
/// </summary>
public sealed class CleanExecutor
{
    private readonly SafetyGate _safetyGate;
    private readonly QuarantineService _quarantine;
    private readonly QuarantinePathValidator _pathValidator;
    private readonly IFileSystem _fileSystem;
    private readonly IVolumeProbe _volumes;
    private readonly IEnvironmentProbe _environment;
    private readonly IClock _clock;
    private readonly IReadOnlyList<ISpecialItemHandler> _specialHandlers;
    private readonly ILogSink _log;

    public CleanExecutor(
        SafetyGate safetyGate,
        QuarantineService quarantine,
        QuarantinePathValidator pathValidator,
        IFileSystem fileSystem,
        IVolumeProbe volumes,
        IEnvironmentProbe environment,
        IClock clock,
        IEnumerable<ISpecialItemHandler>? specialHandlers = null,
        ILogSink? log = null)
    {
        _safetyGate = safetyGate;
        _quarantine = quarantine;
        _pathValidator = pathValidator;
        _fileSystem = fileSystem;
        _volumes = volumes;
        _environment = environment;
        _clock = clock;
        _specialHandlers = (specialHandlers ?? Array.Empty<ISpecialItemHandler>()).ToList();
        _log = log ?? SilentLogSink.Instance;
    }

    /// <summary>异步执行（供界面调用，内部在线程池执行以免冻结 UI）。</summary>
    public Task<ExecutionReport> ExecuteAsync(
        CleanPlan plan,
        ExecutionOptions options,
        IProgress<CleanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Execute(plan, options, progress, cancellationToken), CancellationToken.None);

    /// <summary>同步执行核心（单测直接调用，行为与异步版一致）。</summary>
    public ExecutionReport Execute(
        CleanPlan plan,
        ExecutionOptions options,
        IProgress<CleanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);

        var startedAt = _clock.Now;
        var results = new List<ItemExecutionResult>();
        var skipped = new List<SkippedFile>();

        var checkedItems = plan.Checked.ToList();

        // ② 收集要搬的文件（只从清单里取），并对每一项做安全闸门预检
        var plannedFiles = new List<PlannedFile>();
        var blockedItems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long requiredBytes = 0;

        foreach (var item in checkedItems)
        {
            if (item.ActionKind != CleanActionKind.Quarantine)
            {
                continue;
            }

            var definition = ResolveDefinition(plan, item.ItemId);
            if (definition is null)
            {
                blockedItems[item.ItemId] = "计划中缺少该项的清理定义，为安全起见整项跳过";
                continue;
            }

            var blockedFiles = new List<SkippedFile>();
            foreach (var file in item.Files)
            {
                var decision = _safetyGate.Authorize(file.Path, definition);
                if (decision.IsAllowed)
                {
                    plannedFiles.Add(new PlannedFile(item.ItemId, item.Category, file.Path, file.Size, file.LastWrite));
                    requiredBytes += file.Size;
                }
                else
                {
                    blockedFiles.Add(new SkippedFile(item.ItemId, file.Path, decision.Reason));
                }
            }

            if (blockedFiles.Count > 0)
            {
                skipped.AddRange(blockedFiles);
                _log.Warn($"项目 {item.ItemId}：{blockedFiles.Count} 个文件被安全闸门拒绝");
            }
        }

        // ③ 隔离区路径预检（在动任何文件之前）
        var sourceVolumeOf = plan.Scan.Volume.Drive;
        var validation = _pathValidator.Validate(
            options.QuarantinePath,
            requiredBytes,
            sourceVolumeOf,
            _volumes,
            _fileSystem,
            _environment);

        if (!validation.IsUsable)
        {
            foreach (var file in plannedFiles)
            {
                skipped.Add(new SkippedFile(file.ItemId, file.OriginalPath, $"隔离区不可用：{validation.Message}"));
            }

            foreach (var item in checkedItems)
            {
                results.Add(new ItemExecutionResult(
                    item.ItemId,
                    item.DisplayName,
                    item.ActionKind,
                    0,
                    0,
                    item.Files.Count,
                    $"隔离区不可用，未执行：{validation.Message}"));
            }

            _log.Error($"执行中止：隔离区路径不可用 —— {validation.Message}");
            return BuildReport(plan, startedAt, results, skipped, movedBytes: 0, sameVolume: IsSameVolume(options.QuarantineRoot, sourceVolumeOf), batchId: null);
        }

        // ④ 一次性搬运（一个执行 = 一个批次）
        var sameVolume = IsSameVolume(options.QuarantineRoot, sourceVolumeOf);
        BatchStoreResult? storeResult = null;

        if (plannedFiles.Count > 0)
        {
            storeResult = _quarantine.Store(options.QuarantineRoot, options.RetentionDays, plannedFiles, progress, cancellationToken);
            skipped.AddRange(storeResult.Skipped);
        }

        // ⑤ 汇总每项结果
        foreach (var item in checkedItems)
        {
            if (item.ActionKind == CleanActionKind.Quarantine)
            {
                if (blockedItems.TryGetValue(item.ItemId, out var reason))
                {
                    results.Add(new ItemExecutionResult(item.ItemId, item.DisplayName, item.ActionKind, 0, 0, item.Files.Count, reason));
                    continue;
                }

                var moved = storeResult?.StoredEntries.Where(e => e.ItemId == item.ItemId).ToList() ?? new List<QuarantineMapEntry>();
                var itemSkipped = storeResult?.Skipped.Count(s => s.ItemId == item.ItemId) ?? 0;
                results.Add(new ItemExecutionResult(
                    item.ItemId,
                    item.DisplayName,
                    item.ActionKind,
                    moved.Count,
                    moved.Sum(e => e.SizeBytes),
                    itemSkipped,
                    storeResult?.Cancelled == true ? "执行被用户中断，剩余文件未处理" : null));
                continue;
            }

            if (item.ActionKind == CleanActionKind.InformationalOnly)
            {
                results.Add(new ItemExecutionResult(item.ItemId, item.DisplayName, item.ActionKind, 0, 0, 0, "仅展示，不执行"));
                continue;
            }

            var handler = _specialHandlers.FirstOrDefault(h => h.CanHandle(item.ActionKind));
            if (handler is null)
            {
                results.Add(new ItemExecutionResult(item.ItemId, item.DisplayName, item.ActionKind, 0, 0, 0, "未接入专项处理器，本次不执行"));
                continue;
            }

            results.Add(handler.Handle(item, options, cancellationToken));
        }

        var movedBytes = storeResult?.StoredBytes ?? 0;
        _log.Info($"执行完成：搬运 {storeResult?.StoredCount ?? 0} 个文件（{movedBytes} 字节），跳过 {skipped.Count} 个");

        return BuildReport(plan, startedAt, results, skipped, movedBytes, sameVolume, storeResult?.BatchId);
    }

    private ExecutionReport BuildReport(
        CleanPlan plan,
        DateTimeOffset startedAt,
        IReadOnlyList<ItemExecutionResult> results,
        IReadOnlyList<SkippedFile> skipped,
        long movedBytes,
        bool sameVolume,
        string? batchId) =>
        new(
            plan.PlanId,
            startedAt,
            _clock.Now,
            results,
            movedBytes,
            sameVolume,
            skipped,
            batchId);

    private bool IsSameVolume(string quarantineRoot, string sourceVolume) =>
        _volumes.GetVolumeOf(quarantineRoot).Equals(_volumes.GetVolumeOf(sourceVolume), StringComparison.OrdinalIgnoreCase);

    /// <summary>清理定义来自扫描报告（同一份清单的数据源），取不到就整项跳过。</summary>
    private static CleanItemDefinition? ResolveDefinition(CleanPlan plan, string itemId) =>
        plan.Scan.Find(itemId)?.Item;
}
