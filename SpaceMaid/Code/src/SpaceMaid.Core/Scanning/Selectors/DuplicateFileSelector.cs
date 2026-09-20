using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Models;

namespace SpaceMaid.Core.Scanning.Selectors;

/// <summary>
/// `l3.duplicate-files`（重复文件）的候选收窄器。
///
/// 需求 2.4 / 设计文档 §5.2 的落地口径（两轮哈希，先粗后细）：
/// ① 按 <c>Size</c> 分组，跳过 0 字节与只有一个文件的组——大小不同绝不可能内容相同；
/// ② 组内先用 <c>ComputeHash(full: false)</c>（首尾各 4MB + 文件长度）粗筛；
/// ③ 粗筛命中的再算全文件 SHA-256 确认（杜绝"首尾相同、中间不同"被误判）；
/// ④ **每组只保留一份**（最早修改的那份；年龄相同按路径升序取第一），**其余全部列出**；
/// ⑤ 哈希为空串（读取失败/无权限）的文件**一律不列出**——读不出来就不敢说它是重复的；
/// ⑥ 输出按体积降序、同体积按路径升序。
///
/// <para>
/// 与 `l3.large-files` 一样，收窄器**不碰勾选状态**（`DefaultChecked` 恒为 false，需求 5.4-1），
/// 也不修改传入的 <paramref name="item"/>。
/// </para>
/// </summary>
public sealed class DuplicateFileSelector : IItemCandidateSelector
{
    /// <summary>对应的清理项 Id（与 CleanItemCatalog 一致，一经发布不得改名）。</summary>
    public const string ItemId = "l3.duplicate-files";

    /// <summary>
    /// 参与重复判定的体积上限（512 MB）。超过它的文件直接排除：
    /// 对几个 GB 的镜像/虚拟磁盘做全量哈希会让扫描长时间无响应，而那种"重复"通常是你自己的数据，
    /// 交给下面的"大文件"条目按体积展示更合适（对抗式评审 F-12：扫描必须有界限）。
    /// </summary>
    public const long MaxFileSizeBytes = 512L * 1024 * 1024;

    /// <inheritdoc />
    public bool CanHandle(string itemId) => string.Equals(itemId, ItemId, StringComparison.Ordinal);

    /// <inheritdoc />
    public IReadOnlyList<ScanFile> Select(
        CleanItemDefinition item,
        IReadOnlyList<ScanFile> candidates,
        IFileSystem fileSystem,
        ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(fileSystem);

        var result = new List<ScanFile>();
        var failedHashes = 0;
        var tooLarge = 0;

        // ① 先按体积分组（0 字节文件、独苗组与超大文件直接跳过）
        foreach (var sizeGroup in candidates.Where(file => file.Size > 0 && file.Size <= MaxFileSizeBytes).GroupBy(file => file.Size))
        {
            var sameSize = sizeGroup.ToList();
            if (sameSize.Count < 2)
            {
                continue;
            }

            // ② 采样哈希粗筛
            foreach (var sampleBucket in GroupByHash(sameSize, fileSystem, full: false, ref failedHashes))
            {
                if (sampleBucket.Count < 2)
                {
                    continue;
                }

                // ③ 全量哈希确认
                foreach (var confirmed in GroupByHash(sampleBucket, fileSystem, full: true, ref failedHashes))
                {
                    if (confirmed.Count < 2)
                    {
                        continue;
                    }

                    // ④ 保留最早修改的那一份（同龄按路径升序取第一），其余全部列出
                    var ordered = confirmed
                        .OrderBy(file => file.LastWrite)
                        .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    for (var index = 1; index < ordered.Count; index++)
                    {
                        result.Add(ordered[index]);
                    }
                }
            }
        }

        var output = result
            .OrderByDescending(file => file.Size)
            .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        tooLarge = candidates.Count(file => file.Size > MaxFileSizeBytes);

        if (candidates.Count != output.Count)
        {
            log?.Info($"重复文件候选收窄：{candidates.Count} 个候选 → {output.Count} 个" +
                      $"（只列出每组重复里除保留份之外的其余文件；{failedHashes} 个文件因读不到内容被跳过；" +
                      $"{tooLarge} 个文件超过 {MaxFileSizeBytes / 1024 / 1024} MB 未参与判定）");
        }

        return output;
    }

    /// <summary>
    /// 按内容哈希把文件分组。哈希为空串（读取失败）的文件**不进任何组**，因此永远不会被列出。
    /// </summary>
    private static List<List<ScanFile>> GroupByHash(
        List<ScanFile> files,
        IFileSystem fileSystem,
        bool full,
        ref int failedHashes)
    {
        var buckets = new Dictionary<string, List<ScanFile>>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var file in files)
        {
            var hash = fileSystem.ComputeHash(file.Path, full);
            if (string.IsNullOrEmpty(hash))
            {
                failedHashes++;
                continue;
            }

            if (!buckets.TryGetValue(hash, out var bucket))
            {
                bucket = new List<ScanFile>();
                buckets[hash] = bucket;
                order.Add(hash);
            }

            bucket.Add(file);
        }

        return order.Select(hash => buckets[hash]).ToList();
    }
}
