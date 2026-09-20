using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Logging;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Safety;

namespace SpaceMaid.Core.Quarantine;

/// <summary>
/// 隔离区存储：把文件**搬进**隔离区、写账本、断电自检。
///
/// 安全不变量（设计文档 I-1）：
/// 1. 本类与 <see cref="QuarantineMapStore"/> 是**全项目唯一**允许调用删除 API 的位置；
/// 2. 两阶段写盘：先写 <c>pending=true</c> 的完整账本 → 再搬文件 → 最后改写为已完成；
///    **绝不出现"文件搬了但没记账"**，所以断电后一定能算出真实状态；
/// 3. 搬运失败一律保留源文件（需求 3.4-3），失败项不进账本，只出现在 skipped 里；
/// 4. 同卷只移动目录条目（不占额外空间），跨卷"复制 → 校验 → 删源"，校验不过就保留源文件。
/// </summary>
public sealed class QuarantineStore
{
    private const string PayloadFolder = "payload";

    private readonly IFileSystem _fileSystem;
    private readonly IVolumeProbe _volumes;
    private readonly IClock _clock;
    private readonly ILogSink _log;

    public QuarantineStore(IFileSystem fileSystem, IVolumeProbe volumes, IClock clock, ILogSink? log = null)
    {
        _fileSystem = fileSystem;
        _volumes = volumes;
        _clock = clock;
        _log = log ?? SilentLogSink.Instance;
    }

    /// <summary>生成批次目录名：yyyyMMdd-HHmmss-fff（可读、可排序、不会重名）。</summary>
    public static string CreateBatchId(DateTimeOffset createdAt) =>
        createdAt.ToString("yyyyMMdd-HHmmss-fff");

    /// <summary>批次目录 = 隔离区根\yyyyMMdd-HHmmss-fff。</summary>
    public static string GetBatchDirectory(string quarantineRoot, string batchId) =>
        Path.Combine(quarantineRoot, batchId);

    /// <summary>
    /// 把计划中的文件搬入隔离区（两阶段写盘）。**不做安全授权**——授权由调用方（执行器）在执行前完成。
    /// </summary>
    public Task<BatchStoreResult> StoreAsync(
        string quarantineRoot,
        int retentionDays,
        IReadOnlyList<PlannedFile> files,
        IProgress<CleanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        return Task.Run(() => StoreCore(quarantineRoot, retentionDays, files, progress, cancellationToken), CancellationToken.None);
    }

    /// <summary>同步实现的搬运核心（便于单测直接调用）。</summary>
    public BatchStoreResult StoreCore(
        string quarantineRoot,
        int retentionDays,
        IReadOnlyList<PlannedFile> files,
        IProgress<CleanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var createdAt = _clock.Now;
        var (batchId, batchDirectory) = AllocateBatch(quarantineRoot, createdAt);
        var payloadDirectory = Path.Combine(batchDirectory, PayloadFolder);

        var sourceVolume = files.Count > 0 ? _volumes.GetVolumeOf(files[0].OriginalPath) : string.Empty;
        var quarantineVolume = _volumes.GetVolumeOf(quarantineRoot);
        var sameVolume = !string.IsNullOrEmpty(sourceVolume)
                         && sourceVolume.Equals(quarantineVolume, StringComparison.OrdinalIgnoreCase);

        // ① 先落账本：全部条目标为 Pending（此时还没搬任何文件）
        var pendingEntries = new List<QuarantineMapEntry>();
        var planned = new List<(PlannedFile File, QuarantineMapEntry Entry)>();
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var entry = new QuarantineMapEntry
            {
                ItemId = file.ItemId,
                Category = file.Category,
                OriginalPath = file.OriginalPath,
                SourceVolume = _volumes.GetVolumeOf(file.OriginalPath),
                SizeBytes = file.SizeBytes,
                LastWrite = file.LastWrite,
                StoredAs = Path.Combine(PayloadFolder, BuildStoredName(i + 1, file.OriginalPath)),
                Status = MapEntryStatus.Pending
            };
            planned.Add((file, entry));
            pendingEntries.Add(entry);
        }

        var pendingMap = new QuarantineMap
        {
            BatchId = batchId,
            CreatedAt = createdAt,
            ExpiresAt = createdAt.AddDays(Math.Max(1, retentionDays)),
            QuarantineRoot = quarantineRoot,
            SameVolumeAsSource = sameVolume,
            Pending = true,
            Entries = pendingEntries
        };

        if (!QuarantineMapStore.TryWrite(batchDirectory, pendingMap, out var writeError))
        {
            _log.Error($"隔离区账本写入失败，未搬运任何文件：{writeError}");
            return new BatchStoreResult(batchId, batchDirectory, 0, 0, Array.Empty<SkippedFile>(), Array.Empty<QuarantineMapEntry>(), false);
        }

        try
        {
            _fileSystem.CreateDirectory(payloadDirectory);
        }
        catch (Exception ex)
        {
            _log.Error($"创建隔离区目录失败：{ex.Message}");
            QuarantineMapStore.TryWrite(batchDirectory, pendingMap with { Pending = false, Entries = Array.Empty<QuarantineMapEntry>() }, out _);
            return new BatchStoreResult(batchId, batchDirectory, 0, 0, Array.Empty<SkippedFile>(), Array.Empty<QuarantineMapEntry>(), false);
        }

        // ② 逐文件搬运
        var stored = new List<QuarantineMapEntry>();
        var skipped = new List<SkippedFile>();
        long storedBytes = 0;
        var cancelled = false;

        for (var i = 0; i < planned.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                for (var j = i; j < planned.Count; j++)
                {
                    skipped.Add(new SkippedFile(planned[j].File.ItemId, planned[j].File.OriginalPath, "用户取消，未处理"));
                }

                break;
            }

            var (file, entry) = planned[i];
            var destination = Path.Combine(batchDirectory, entry.StoredAs);

            var (ok, reason) = MoveFile(file.OriginalPath, destination, sameVolume);
            if (ok)
            {
                stored.Add(entry with { Status = MapEntryStatus.Stored });
                storedBytes += file.SizeBytes;
            }
            else
            {
                skipped.Add(new SkippedFile(file.ItemId, file.OriginalPath, reason));
                _log.Warn($"隔离失败（源文件保留）：{file.OriginalPath} —— {reason}");
            }

            progress?.Report(new CleanProgress(
                file.ItemId,
                file.ItemId,
                i + 1,
                planned.Count,
                storedBytes));
        }

        // ③ 账本收尾：只保留真正搬进来的文件，Pending=false
        var finalMap = pendingMap with
        {
            Pending = false,
            Entries = stored
        };

        if (!QuarantineMapStore.TryWrite(batchDirectory, finalMap, out var finalError))
        {
            // 账本收尾失败：保持 pending 状态，下次启动自检会按真实文件状态修复
            _log.Error($"隔离区账本收尾失败（下次启动将自检修复）：{finalError}");
        }

        _log.Info($"隔离完成：批次 {batchId}，{stored.Count} 个文件，{storedBytes} 字节，跳过 {skipped.Count} 个");
        return new BatchStoreResult(batchId, batchDirectory, stored.Count, storedBytes, skipped, stored, cancelled);
    }

    /// <summary>统计隔离区现状（含已到期批次数）。</summary>
    public QuarantineInfo Inspect(string quarantineRoot)
    {
        var now = _clock.Now;
        var batches = new List<BatchInfo>();

        foreach (var directory in EnumerateBatchDirectories(quarantineRoot))
        {
            var map = QuarantineMapStore.TryRead(directory);
            if (map is null)
            {
                continue;
            }

            var storedEntries = map.Entries.Where(e => e.Status == MapEntryStatus.Stored).ToList();
            batches.Add(new BatchInfo(
                map.BatchId,
                map.CreatedAt,
                map.ExpiresAt,
                storedEntries.Sum(e => e.SizeBytes),
                storedEntries.Count,
                map.ExpiresAt <= now));
        }

        return new QuarantineInfo(
            batches.OrderBy(b => b.CreatedAt).ToList(),
            batches.Sum(b => b.TotalBytes),
            batches.Count(b => b.Expired));
    }

    /// <summary>
    /// 启动自检（断电恢复，需求 3.2-6 / 3.4-4）。对仍标着 Pending 的账本，按"文件到底在哪"修复状态：
    /// 副本在、源没了 → 记为已隔离；源还在 → 丢弃该账目（源文件是权威，用户的文件不丢）；
    /// 两边都在 → 删除多余副本并丢弃账目（不谎报"已清理"）；两边都没了 → 标记 Unknown 交给复核报告列出。
    /// </summary>
    public RecoverReport Recover(string quarantineRoot)
    {
        var storedRecovered = 0;
        var markedUnknown = 0;
        var pendingCleared = 0;
        var notes = new List<string>();

        foreach (var directory in EnumerateBatchDirectories(quarantineRoot))
        {
            var map = QuarantineMapStore.TryRead(directory);
            if (map is null || !map.Pending)
            {
                continue;
            }

            var repaired = new List<QuarantineMapEntry>();
            foreach (var entry in map.Entries)
            {
                var payloadPath = Path.Combine(directory, entry.StoredAs);

                // 账本是不可信输入：只处理"确实落在本批次目录内"的存放路径（TryRead 已做一层，这里是纵深防御）
                if (!IsInsideBatch(quarantineRoot, directory, entry.StoredAs))
                {
                    _log.Warn($"账本条目存放路径越界，已忽略：{entry.StoredAs}");
                    markedUnknown++;
                    notes.Add($"异常：账本条目存放路径越界（{entry.StoredAs}），已忽略以防误删");
                    continue;
                }

                var payloadExists = _fileSystem.FileExists(payloadPath);
                var sourceExists = _fileSystem.FileExists(entry.OriginalPath);

                if (payloadExists && !sourceExists)
                {
                    repaired.Add(entry with { Status = MapEntryStatus.Stored, Note = "断电自检：已确认搬入隔离区" });
                    storedRecovered++;
                }
                else if (!payloadExists && sourceExists)
                {
                    pendingCleared++;
                }
                else if (payloadExists && sourceExists)
                {
                    // 复制成功但删源未完成：源文件是用户的原始文件，优先保留它，删掉隔离副本
                    DeleteQuarantinedFile(quarantineRoot, directory, entry.StoredAs, out var deleteError);
                    pendingCleared++;
                    notes.Add($"幂等修复：{entry.OriginalPath} 仍在原位，已丢弃隔离副本（{(string.IsNullOrEmpty(deleteError) ? "成功" : deleteError)}）");
                }
                else
                {
                    repaired.Add(entry with { Status = MapEntryStatus.Unknown, Note = "断电自检：原位与隔离区都不存在该文件" });
                    markedUnknown++;
                    notes.Add($"异常：{entry.OriginalPath} 既不在原位也不在隔离区，请在复核报告中确认");
                }
            }

            var repairedMap = map with { Pending = false, Entries = repaired };
            if (!QuarantineMapStore.TryWrite(directory, repairedMap, out var error))
            {
                notes.Add($"恢复批次 {map.BatchId} 账本写回失败：{error}");
            }
        }

        if (pendingCleared > 0 || storedRecovered > 0 || markedUnknown > 0)
        {
            _log.Info($"隔离区自检完成：补记 {storedRecovered}，清除未生效账目 {pendingCleared}，异常 {markedUnknown}");
        }

        return new RecoverReport(storedRecovered, markedUnknown, pendingCleared, notes);
    }

    /// <summary>列出所有批次的映射表（服务层还原/释放使用）。</summary>
    public IReadOnlyList<(string BatchDirectory, QuarantineMap Map)> ReadAllMaps(string quarantineRoot)
    {
        var result = new List<(string, QuarantineMap)>();
        foreach (var directory in EnumerateBatchDirectories(quarantineRoot))
        {
            var map = QuarantineMapStore.TryRead(directory);
            if (map is not null)
            {
                result.Add((directory, map));
            }
        }

        return result;
    }

    /// <summary>写出一个批次的映射表（服务层还原/释放后回写状态）。</summary>
    public bool TryWriteMap(string batchDirectory, QuarantineMap map, out string error) =>
        QuarantineMapStore.TryWrite(batchDirectory, map, out error);

    /// <summary>
    /// 删除隔离区内的一个实体文件——**唯一允许的删除调用点**（不变量 I-1）。
    /// 入参用"隔离区根 + 批次目录 + 相对存放路径"而不是裸路径：这样可以在真正删除之前
    /// 用 <see cref="IsInsideBatch"/> 证明目标是"本批次目录内的相对路径"，
    /// 从根上堵住"伪造账本条目的 StoredAs 指向系统文件"这条提权删除通道。
    /// </summary>
    internal bool DeleteQuarantinedFile(string quarantineRoot, string batchDirectory, string storedAs, out string error)
    {
        if (!IsInsideBatch(quarantineRoot, batchDirectory, storedAs))
        {
            error = $"拒绝删除隔离区之外的文件：{storedAs}";
            _log.Error(error);
            return false;
        }

        var payloadPath = Path.Combine(batchDirectory, storedAs);
        return _fileSystem.TryDeleteFile(payloadPath, out error);
    }

    /// <summary>
    /// 存放路径是否"安全地落在本批次目录内"：① StoredAs 本身相对且无 ..；② 拼出的绝对路径确实位于批次目录之下；
    /// ③ 批次目录位于给定隔离区根之下。
    /// </summary>
    internal static bool IsInsideBatch(string quarantineRoot, string batchDirectory, string storedAs)
    {
        if (!QuarantineMapStore.IsSafeStoredAs(storedAs))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(quarantineRoot) || string.IsNullOrWhiteSpace(batchDirectory))
        {
            return false;
        }

        try
        {
            var root = Path.GetFullPath(quarantineRoot);
            var batch = Path.GetFullPath(batchDirectory);
            var payload = Path.GetFullPath(Path.Combine(batch, storedAs));

            return PathNormalizer.IsUnder(batch, root) && PathNormalizer.IsUnder(payload, batch);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 删除一个批次目录（释放/清空时使用）。**唯一允许调用目录删除的位置**（不变量 I-1）。
    /// 带两重护栏：批次目录必须位于给定隔离区根之下，且根目录自身绝不允许被删除。
    /// </summary>
    internal bool RemoveBatchDirectory(string quarantineRoot, string batchDirectory, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(quarantineRoot) || string.IsNullOrWhiteSpace(batchDirectory))
        {
            error = "隔离区根或批次目录为空";
            return false;
        }

        var root = Path.GetFullPath(quarantineRoot);
        var target = Path.GetFullPath(batchDirectory);

        if (!PathNormalizer.IsUnder(target, root))
        {
            error = $"拒绝删除隔离区之外的目录：{target}";
            _log.Error(error);
            return false;
        }

        try
        {
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"删除批次目录失败：{ex.Message}";
            return false;
        }
    }

    private (bool Ok, string Reason) MoveFile(string source, string destination, bool sameVolume)
    {
        if (!_fileSystem.FileExists(source))
        {
            return (false, "文件已不存在");
        }

        if (_fileSystem.IsFileLocked(source))
        {
            return (false, "文件正被占用");
        }

        if (sameVolume)
        {
            return _fileSystem.TryMove(source, destination, out var moveError)
                ? (true, string.Empty)
                : (false, moveError);
        }

        // 跨卷：复制 → 校验 → 删源
        if (!_fileSystem.TryCopy(source, destination, out var copyError))
        {
            return (false, $"复制失败：{copyError}");
        }

        var sourceHash = _fileSystem.ComputeHash(source, full: true);
        var destinationHash = _fileSystem.ComputeHash(destination, full: true);
        if (string.IsNullOrEmpty(sourceHash) || !string.Equals(sourceHash, destinationHash, StringComparison.OrdinalIgnoreCase))
        {
            // 校验不通过：保留源文件，删掉不可信的副本
            _fileSystem.TryDeleteFile(destination, out _);
            return (false, "复制后校验不一致，已保留源文件");
        }

        if (!_fileSystem.TryDeleteFile(source, out var deleteError))
        {
            _fileSystem.TryDeleteFile(destination, out _);
            return (false, $"删除源文件失败：{deleteError}");
        }

        return (true, string.Empty);
    }

    /// <summary>
    /// 分配一个**唯一**的批次目录。
    /// 为什么需要去重：批次 Id 精确到毫秒，同一毫秒内连续清理两次（或时钟被回拨）会撞名，
    /// 后一批的账本会覆盖前一批，直接导致文件丢失——必须在这里挡住。
    /// </summary>
    private (string BatchId, string BatchDirectory) AllocateBatch(string quarantineRoot, DateTimeOffset createdAt)
    {
        var baseId = CreateBatchId(createdAt);
        var batchId = baseId;
        var directory = GetBatchDirectory(quarantineRoot, batchId);
        var suffix = 1;

        while (_fileSystem.DirectoryExists(directory))
        {
            batchId = $"{baseId}-{suffix++}";
            directory = GetBatchDirectory(quarantineRoot, batchId);
        }

        return (batchId, directory);
    }

    private IEnumerable<string> EnumerateBatchDirectories(string quarantineRoot)
    {
        if (string.IsNullOrWhiteSpace(quarantineRoot) || !_fileSystem.DirectoryExists(quarantineRoot))
        {
            yield break;
        }

        foreach (var directory in _fileSystem.EnumerateDirectories(quarantineRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            yield return directory;
        }
    }

    /// <summary>payload 内文件名：序号 + 原名（原名只做可读性，序号保证唯一）。</summary>
    private static string BuildStoredName(int index, string originalPath)
    {
        var name = Path.GetFileName(originalPath);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = "unnamed";
        }

        if (name.Length > 120)
        {
            name = name[..120];
        }

        return $"{index:D4}_{name}";
    }
}
