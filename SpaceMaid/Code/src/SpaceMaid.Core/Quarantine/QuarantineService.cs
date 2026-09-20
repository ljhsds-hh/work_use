using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Logging;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Safety;

namespace SpaceMaid.Core.Quarantine;

/// <summary>
/// 隔离区服务：面向业务的入口（搬运 / 还原 / 惰性释放 / 立即清空 / 自检）。
///
/// 设计要点：
/// 1. **惰性释放**（需求 3.4-5）：本工具无常驻进程、不注册计划任务，
///    所以"到期释放"只发生在**下次启动或下次扫描前**调用 <see cref="ReleaseExpired"/> 时；
/// 2. **还原不静默覆盖**（需求 3.4-6）：原位置已存在同名文件时记为冲突，由用户决定；
/// 3. 所有删除都委托给 <see cref="QuarantineStore"/>（唯一允许删除的类，不变量 I-1）。
/// </summary>
public sealed class QuarantineService
{
    private readonly QuarantineStore _store;
    private readonly IFileSystem _fileSystem;
    private readonly IVolumeProbe _volumes;
    private readonly IEnvironmentProbe _environment;
    private readonly IClock _clock;
    private readonly ILogSink _log;

    public QuarantineService(
        QuarantineStore store,
        IFileSystem fileSystem,
        IVolumeProbe volumes,
        IEnvironmentProbe environment,
        IClock clock,
        ILogSink? log = null)
    {
        _store = store;
        _fileSystem = fileSystem;
        _volumes = volumes;
        _environment = environment;
        _clock = clock;
        _log = log ?? SilentLogSink.Instance;
    }

    /// <summary>把文件搬入隔离区（转发给存储层，业务侧只认识这个方法）。</summary>
    public Task<BatchStoreResult> StoreAsync(
        string quarantineRoot,
        int retentionDays,
        IReadOnlyList<PlannedFile> files,
        IProgress<CleanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        _store.StoreAsync(quarantineRoot, retentionDays, files, progress, cancellationToken);

    /// <summary>同步搬运（执行器与单测使用）。</summary>
    public BatchStoreResult Store(
        string quarantineRoot,
        int retentionDays,
        IReadOnlyList<PlannedFile> files,
        IProgress<CleanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        _store.StoreCore(quarantineRoot, retentionDays, files, progress, cancellationToken);

    /// <summary>隔离区现状。</summary>
    public QuarantineInfo Inspect(string quarantineRoot) => _store.Inspect(quarantineRoot);

    /// <summary>断电/强退后的账本自检。</summary>
    public RecoverReport Recover(string quarantineRoot) => _store.Recover(quarantineRoot);

    /// <summary>列出所有批次（复核报告要拿它判断"文件到底去哪了"）。</summary>
    public IReadOnlyList<QuarantineMap> ReadMaps(string quarantineRoot) =>
        _store.ReadAllMaps(quarantineRoot).Select(pair => pair.Map).ToList();

    /// <summary>
    /// 还原一个批次：按映射表把文件搬回原位。
    /// 冲突（原位置已有同名文件）与失败（副本缺失、原卷不可用）都逐条报告，**不覆盖用户现有文件**。
    /// </summary>
    public RestoreResult Restore(string quarantineRoot, string batchId, RestoreOptions? options = null)
    {
        var restoreOptions = options ?? new RestoreOptions();
        var conflicts = new List<RestoreConflict>();
        var failures = new List<RestoreFailure>();
        var restoredCount = 0;
        long restoredBytes = 0;

        var batch = _store.ReadAllMaps(quarantineRoot)
            .FirstOrDefault(pair => pair.Map.BatchId == batchId);

        if (batch.Map is null)
        {
            failures.Add(new RestoreFailure(batchId, "找不到该批次的隔离记录"));
            return new RestoreResult(0, 0, conflicts, failures);
        }

        var entries = new List<QuarantineMapEntry>();
        foreach (var entry in batch.Map.Entries)
        {
            if (entry.Status != MapEntryStatus.Stored)
            {
                entries.Add(entry);
                continue;
            }

            // 账本是不可信输入：还原目标必须①落在本批次目录内、②是规范化的绝对路径、③不在禁止清单里。
            // 少了这一层，一条伪造的账目就能把文件搬进（或覆盖）系统目录。
            if (!QuarantineStore.IsInsideBatch(quarantineRoot, batch.BatchDirectory, entry.StoredAs))
            {
                failures.Add(new RestoreFailure(entry.OriginalPath, "账本条目存放路径越界，已拒绝还原"));
                entries.Add(entry with { Status = MapEntryStatus.Unknown, Note = "账本条目存放路径越界" });
                continue;
            }

            if (!PathNormalizer.TryNormalize(entry.OriginalPath, _environment, out var normalizedOriginal, out var pathError))
            {
                failures.Add(new RestoreFailure(entry.OriginalPath, $"原始路径不可用：{pathError}"));
                entries.Add(entry);
                continue;
            }

            if (Denylist.IsDenied(normalizedOriginal))
            {
                failures.Add(new RestoreFailure(entry.OriginalPath, Denylist.ExplainDenial(normalizedOriginal)));
                entries.Add(entry with { Status = MapEntryStatus.Unknown, Note = "目标命中禁止清单，已拒绝还原" });
                continue;
            }

            var payloadPath = Path.Combine(batch.BatchDirectory, entry.StoredAs);

            if (!_fileSystem.FileExists(payloadPath))
            {
                failures.Add(new RestoreFailure(entry.OriginalPath, "隔离区内的副本已不存在"));
                entries.Add(entry with { Status = MapEntryStatus.Unknown, Note = "还原时发现副本缺失" });
                continue;
            }

            var root = Path.GetPathRoot(normalizedOriginal);
            if (!string.IsNullOrEmpty(root) && !_fileSystem.DirectoryExists(root))
            {
                failures.Add(new RestoreFailure(entry.OriginalPath, "原卷不可用（盘符不存在）"));
                entries.Add(entry);
                continue;
            }

            if (!restoreOptions.Overwrite && _fileSystem.FileExists(normalizedOriginal))
            {
                conflicts.Add(new RestoreConflict(entry.OriginalPath, "原位置已存在同名文件（未覆盖）"));
                entries.Add(entry);
                continue;
            }

            var (ok, reason) = MoveBack(payloadPath, normalizedOriginal);
            if (ok)
            {
                restoredCount++;
                restoredBytes += entry.SizeBytes;
                entries.Add(entry with { Status = MapEntryStatus.Restored, Note = "已还原" });
            }
            else
            {
                failures.Add(new RestoreFailure(entry.OriginalPath, reason));
                entries.Add(entry);
            }
        }

        _store.TryWriteMap(batch.BatchDirectory, batch.Map with { Entries = entries }, out var writeError);
        if (!string.IsNullOrEmpty(writeError))
        {
            _log.Warn($"还原后账本写回失败：{writeError}");
        }

        _log.Info($"还原批次 {batchId}：成功 {restoredCount}，冲突 {conflicts.Count}，失败 {failures.Count}");
        return new RestoreResult(restoredCount, restoredBytes, conflicts, failures);
    }

    /// <summary>
    /// 惰性释放到期批次（需求 3.4-5）。在**启动时**与**扫描前**各调用一次即可，不需要任何后台任务。
    /// </summary>
    public ReleaseResult ReleaseExpired(string quarantineRoot)
    {
        var now = _clock.Now;
        var notes = new List<string>();
        var releasedBatches = 0;
        var releasedFiles = 0;
        long releasedBytes = 0;

        foreach (var (batchDirectory, map) in _store.ReadAllMaps(quarantineRoot))
        {
            if (map.ExpiresAt > now)
            {
                continue;
            }

            var stored = map.Entries.Where(e => e.Status == MapEntryStatus.Stored).ToList();
            var (files, bytes, deleteNotes) = DeletePayload(quarantineRoot, batchDirectory, stored);
            notes.AddRange(deleteNotes);

            if (_store.RemoveBatchDirectory(quarantineRoot, batchDirectory, out var removeError))
            {
                releasedBatches++;
                releasedFiles += files;
                releasedBytes += bytes;
            }
            else
            {
                notes.Add($"批次 {map.BatchId} 未完全释放：{removeError}");
            }
        }

        if (releasedBatches > 0)
        {
            _log.Info($"惰性释放：{releasedBatches} 个批次，{releasedFiles} 个文件，{releasedBytes} 字节");
        }

        return new ReleaseResult(releasedBatches, releasedFiles, releasedBytes, notes);
    }

    /// <summary>立即清空整个隔离区（需界面二次确认；文案要写明不可还原）。</summary>
    public ReleaseResult ClearAll(string quarantineRoot)
    {
        var notes = new List<string>();
        var batches = 0;
        var files = 0;
        long bytes = 0;

        foreach (var (batchDirectory, map) in _store.ReadAllMaps(quarantineRoot))
        {
            var entries = map.Entries.Where(e => e.Status == MapEntryStatus.Stored).ToList();
            var (fileCount, entryBytes, deleteNotes) = DeletePayload(quarantineRoot, batchDirectory, entries);
            notes.AddRange(deleteNotes);

            if (_store.RemoveBatchDirectory(quarantineRoot, batchDirectory, out var removeError))
            {
                batches++;
                files += fileCount;
                bytes += entryBytes;
            }
            else
            {
                notes.Add($"批次 {map.BatchId} 未完全清空：{removeError}");
            }
        }

        // 顺手清掉空目录（含用户的 SpaceMaid\Quarantine 结构本身不动，只清批次目录）
        _log.Info($"清空隔离区：{batches} 个批次，{files} 个文件，{bytes} 字节");
        return new ReleaseResult(batches, files, bytes, notes);
    }

    private (int Files, long Bytes, List<string> Notes) DeletePayload(string quarantineRoot, string batchDirectory, IReadOnlyList<QuarantineMapEntry> entries)
    {
        var notes = new List<string>();
        var deleted = 0;
        long bytes = 0;

        foreach (var entry in entries)
        {
            // 越界条目一律不动（账本是不可信输入，见 QuarantineStore.DeleteQuarantinedFile 的说明）
            if (!QuarantineStore.IsInsideBatch(quarantineRoot, batchDirectory, entry.StoredAs))
            {
                notes.Add($"已忽略越界的账本条目（存放路径越界）：{entry.StoredAs}");
                continue;
            }

            var payloadPath = Path.Combine(batchDirectory, entry.StoredAs);
            if (!_fileSystem.FileExists(payloadPath))
            {
                continue;
            }

            if (_store.DeleteQuarantinedFile(quarantineRoot, batchDirectory, entry.StoredAs, out var error))
            {
                deleted++;
                bytes += entry.SizeBytes;
            }
            else
            {
                notes.Add($"无法删除隔离文件 {payloadPath}：{error}");
            }
        }

        return (deleted, bytes, notes);
    }

    private (bool Ok, string Reason) MoveBack(string payloadPath, string originalPath)
    {
        var payloadVolume = _volumes.GetVolumeOf(payloadPath);
        var targetVolume = _volumes.GetVolumeOf(originalPath);
        var sameVolume = payloadVolume.Equals(targetVolume, StringComparison.OrdinalIgnoreCase);

        var directory = Path.GetDirectoryName(originalPath);
        if (!string.IsNullOrEmpty(directory))
        {
            try
            {
                _fileSystem.CreateDirectory(directory);
            }
            catch (Exception ex)
            {
                return (false, $"无法重建原目录：{ex.Message}");
            }
        }

        if (sameVolume)
        {
            return _fileSystem.TryMove(payloadPath, originalPath, out var moveError)
                ? (true, string.Empty)
                : (false, $"还原失败：{moveError}");
        }

        if (!_fileSystem.TryCopy(payloadPath, originalPath, out var copyError))
        {
            return (false, $"还原复制失败：{copyError}");
        }

        var payloadHash = _fileSystem.ComputeHash(payloadPath, full: true);
        var targetHash = _fileSystem.ComputeHash(originalPath, full: true);
        if (string.IsNullOrEmpty(payloadHash) || !payloadHash.Equals(targetHash, StringComparison.OrdinalIgnoreCase))
        {
            _fileSystem.TryDeleteFile(originalPath, out _);
            return (false, "还原后校验不一致，已放弃该文件");
        }

        if (!_fileSystem.TryDeleteFile(payloadPath, out var deleteError))
        {
            return (false, $"还原后删除隔离副本失败：{deleteError}");
        }

        return (true, string.Empty);
    }
}
