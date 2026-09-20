using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Models;

namespace SpaceMaid.Core.Scanning.Selectors;

/// <summary>
/// `l3.large-files`（大文件）的候选收窄器。
///
/// 需求 2.4 / 设计文档 §5.2 的落地口径：
/// ① 只保留 <see cref="MinSizeBytes"/>（默认 100MB）以上的文件——低于阈值的根本不是"大文件"；
/// ② 按体积降序、同体积按路径升序，只取前 <see cref="MaxCount"/>（默认 200）个——清单是给用户看的，
///    列一万条等于没列。
///
/// <para>
/// **本项是纯展示型**：收窄器只决定"哪些文件出现在清单里"，
/// **不碰勾选状态**——`l3.large-files` 的 `DefaultChecked` 恒为 false（保守原则，需求 1.2-9 / 5.4-1），
/// 是否勾选完全由用户在界面上逐条决定。收窄器也绝不修改传入的 <paramref name="item"/>。
/// </para>
/// </summary>
public sealed class LargeFileSelector : IItemCandidateSelector
{
    /// <summary>对应的清理项 Id（与 CleanItemCatalog 一致，一经发布不得改名）。</summary>
    public const string ItemId = "l3.large-files";

    /// <summary>体积下限：100MB。低于它的文件不进"大文件"清单。</summary>
    public const long MinSizeBytes = 100L * 1024 * 1024;

    /// <summary>最多列出多少条（按体积降序的 Top-N）。</summary>
    public const int MaxCount = 200;

    private readonly long _minSizeBytes;
    private readonly int _maxCount;

    /// <param name="minSizeBytes">体积下限；非正数时回落到 <see cref="MinSizeBytes"/>。</param>
    /// <param name="maxCount">最多条数；非正数时回落到 <see cref="MaxCount"/>。</param>
    public LargeFileSelector(long minSizeBytes = MinSizeBytes, int maxCount = MaxCount)
    {
        _minSizeBytes = minSizeBytes > 0 ? minSizeBytes : MinSizeBytes;
        _maxCount = maxCount > 0 ? maxCount : MaxCount;
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
        ArgumentNullException.ThrowIfNull(candidates);

        var selected = candidates
            .Where(file => file.Size >= _minSizeBytes)
            .OrderByDescending(file => file.Size)
            .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .Take(_maxCount)
            .ToList();

        if (candidates.Count != selected.Count)
        {
            log?.Info($"大文件候选收窄：{candidates.Count} 个候选 → {selected.Count} 个" +
                      $"（体积下限 {_minSizeBytes / (1024 * 1024)}MB，最多 {_maxCount} 条）");
        }

        return selected;
    }
}
