using SpaceMaid.Core.Models;

namespace SpaceMaid.Core.Scanning;

/// <summary>
/// 扫描结果（进入清理计划之前的形态）。
/// </summary>
/// <param name="Item">**生效后**的定义：可能被补上"升级不足 10 天"提示、并把默认勾选改为 false。</param>
/// <param name="Files">将被处理的文件（已通过年龄过滤、已剔除要保留的那一份）。</param>
/// <param name="Kept">明确保留、不处理的文件（例如最近一次蓝屏转储），清单里要写给用户看。</param>
/// <param name="SkippedCount">因年龄/占用等原因未纳入处理的数量（不是错误，但要让用户看得见）。</param>
/// <param name="Note">不可用或需额外提醒时的中文说明。</param>
public sealed record ScanningOutcome(
    CleanItemDefinition Item,
    IReadOnlyList<ScanFile> Files,
    IReadOnlyList<ScanFile> Kept,
    int SkippedCount,
    string? Note);

/// <summary>
/// 扫描规则（纯函数，无 IO）。所有"哪些文件该被处理"的判断都集中在这里，便于穷尽单测。
/// </summary>
public static class ScanningRules
{
    /// <summary>Windows.old 的回退窗口天数（需求 2.3：升级不足 10 天时默认不勾）。</summary>
    public const int WindowsOldGraceDays = 10;

    /// <summary>Windows.old 清理项的 Id（与 CleanItemCatalog 保持一致）。</summary>
    public const string WindowsOldItemId = "l2.windows-old";

    /// <summary>
    /// 对单个条目的候选文件应用全部规则。
    /// </summary>
    /// <param name="item">条目的原始定义。</param>
    /// <param name="candidates">枚举到的候选文件（未过滤）。</param>
    /// <param name="now">当前时间（注入，便于测试年龄与 10 天窗口）。</param>
    /// <param name="targetCreatedAt">目标目录的创建时间（Windows.old 的 10 天窗口判定用；null 表示不适用）。</param>
    public static ScanningOutcome Apply(
        CleanItemDefinition item,
        IEnumerable<ScanFile> candidates,
        DateTimeOffset now,
        DateTimeOffset? targetCreatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(candidates);

        var all = candidates.ToList();
        var effectiveItem = item;
        string? note = null;

        // ① 年龄过滤：只处理超过 MinAge 未修改的文件（避免碰到正在被写入的文件）
        var skippedByAge = 0;
        var aged = new List<ScanFile>();
        foreach (var file in all)
        {
            if (item.MinAge is { } minAge && file.LastWrite > now - minAge)
            {
                skippedByAge++;
                continue;
            }

            aged.Add(file);
        }

        // ② 保留最新一份（蓝屏转储：保留最近一次，其余清理）
        var kept = new List<ScanFile>();
        if (item.KeepsNewest && aged.Count > 0)
        {
            var newest = aged.OrderByDescending(f => f.LastWrite).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase).First();
            kept.Add(newest);
            aged.Remove(newest);
        }

        // ③ Windows.old 的 10 天回退窗口（需求 2.3）：窗口还开着时默认不勾，并给出提示
        if (item.Id == WindowsOldItemId && targetCreatedAt is { } createdAt)
        {
            var age = now - createdAt;
            if (age < TimeSpan.FromDays(WindowsOldGraceDays))
            {
                var remaining = TimeSpan.FromDays(WindowsOldGraceDays) - age;
                note = $"升级距今不足 {WindowsOldGraceDays} 天（约还剩 {Math.Max(1, Math.Ceiling(remaining.TotalDays))} 天回退窗口），" +
                       "默认不勾选：再等几天系统会自动关闭回退窗口";
                effectiveItem = item with { AvailabilityNote = note, DefaultChecked = false };
            }
        }

        if (note is null && !string.IsNullOrWhiteSpace(item.AvailabilityNote))
        {
            note = item.AvailabilityNote;
        }

        return new ScanningOutcome(effectiveItem, aged, kept, skippedByAge, note);
    }
}
