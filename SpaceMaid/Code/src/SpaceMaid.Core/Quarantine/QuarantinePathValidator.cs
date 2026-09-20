using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Safety;

namespace SpaceMaid.Core.Quarantine;

/// <summary>隔离区路径校验级别。</summary>
public enum QuarantinePathLevel
{
    /// <summary>可以使用。</summary>
    Ok,

    /// <summary>能用但要提醒用户（例如仍放在 C 盘、介质可移动）。</summary>
    Warn,

    /// <summary>拒绝保存（不可写、网络路径、系统目录、空间不足等）。</summary>
    Reject
}

/// <summary>
/// 校验结果。<c>Message</c> 是**可直接展示给用户的中文**（界面不再自行拼文案，设计文档 §6.1）。
/// </summary>
public sealed record QuarantinePathValidation(QuarantinePathLevel Level, string Message, string Detail)
{
    public bool IsUsable => Level != QuarantinePathLevel.Reject;
}

/// <summary>
/// 隔离区路径校验（需求 3.4-2 / 7 章第 7 条）。必须在**开始执行之前**完成，
/// 执行过程中不再询问、也不再因路径问题中途失败。
///
/// 关键差异（设计决策 D-6）：
/// ① 跨卷：流程是"复制 → 校验 → 删源"，**必须预留空间**；
/// ② 同卷：只是移动目录条目，**不预检空间**，但会 Warn 提示"C 盘不会立刻腾出空间"。
/// </summary>
public sealed class QuarantinePathValidator
{
    /// <summary>
    /// 校验候选隔离区目录。
    /// </summary>
    /// <param name="candidate">用户选择的目录（尚不必存在）。</param>
    /// <param name="requiredBytes">本次待隔离总体积（跨卷时需要预留的空间）。</param>
    /// <param name="sourceVolumeOf">待处理文件的所在卷（用于同卷/跨卷判定）。</param>
    public QuarantinePathValidation Validate(
        string candidate,
        long requiredBytes,
        string sourceVolumeOf,
        IVolumeProbe volumes,
        IFileSystem fileSystem,
        IEnvironmentProbe environment)
    {
        ArgumentNullException.ThrowIfNull(volumes);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(environment);

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return new QuarantinePathValidation(
                QuarantinePathLevel.Reject,
                "请先指定隔离区位置",
                "隔离区为空路径");
        }

        // ① 网络路径：U 盘/网络共享一旦断开就无法还原
        if (volumes.IsUnc(candidate) || candidate.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return new QuarantinePathValidation(
                QuarantinePathLevel.Reject,
                "隔离区不能放在网络路径上（网络断开后将无法还原）",
                $"拒绝 UNC 路径：{candidate}");
        }

        if (!PathNormalizer.TryNormalize(candidate, environment, out var normalized, out var pathError))
        {
            return new QuarantinePathValidation(QuarantinePathLevel.Reject, pathError, pathError);
        }

        // ② 系统目录 / 磁盘根
        //
        // 注意这里**只拒绝禁止目录树**，不拒绝"用户目录根本身"（对抗式评审后的实测修正）：
        // 默认基目录是 %LOCALAPPDATA%，而它恰好是禁止清单里的"用户目录根本身"——
        // 那条规则的用途是"不把用户目录整个当清理目标"，而隔离区只是**往里建一个子目录**
        // （<基目录>\SpaceMaid\Quarantine），并不会动用户目录本身。此前用 IsDenied 判定基目录，
        // 导致默认配置在真机上直接"隔离区不可用"，整条清理链路都跑不起来。
        var root = Path.GetPathRoot(normalized) ?? string.Empty;
        if (PathNormalizer.TrimTrailingSeparator(normalized).Equals(PathNormalizer.TrimTrailingSeparator(root), StringComparison.OrdinalIgnoreCase))
        {
            return new QuarantinePathValidation(
                QuarantinePathLevel.Reject,
                "请不要直接选择磁盘根目录，请选择具体文件夹（例如 D:\\SpaceMaidQuarantine）",
                $"拒绝卷根：{normalized}");
        }

        if (Denylist.IsDeniedTree(normalized))
        {
            return new QuarantinePathValidation(
                QuarantinePathLevel.Reject,
                "隔离区不能放在系统保护目录下",
                Denylist.ExplainDenial(normalized));
        }

        // ③ 可写性：尝试创建 <candidate>\SpaceMaid\Quarantine
        var storageRoot = Path.Combine(normalized, "SpaceMaid", "Quarantine");

        // 真正要保证安全的是**实际存放目录**：它不能落在禁止目录树里，也不能命中凭据/还原点等禁止段
        if (Denylist.IsDeniedTree(storageRoot)
            || Denylist.DeniedSegments.Any(segment => PathNormalizer.ContainsSegment(storageRoot, segment)))
        {
            return new QuarantinePathValidation(
                QuarantinePathLevel.Reject,
                "隔离区实际存放位置不安全，请换一个普通文件夹",
                $"拒绝存放目录：{storageRoot}");
        }

        try
        {
            fileSystem.CreateDirectory(storageRoot);
        }
        catch (Exception ex)
        {
            return new QuarantinePathValidation(
                QuarantinePathLevel.Reject,
                "该位置无法写入，请换一个文件夹（建议放在非系统盘）",
                $"创建隔离目录失败：{ex.Message}");
        }

        if (!fileSystem.DirectoryExists(storageRoot))
        {
            return new QuarantinePathValidation(
                QuarantinePathLevel.Reject,
                "该位置无法写入，请换一个文件夹（建议放在非系统盘）",
                $"隔离目录不存在：{storageRoot}");
        }

        var quarantineVolume = volumes.GetVolumeOf(storageRoot);
        var sourceVolume = volumes.GetVolumeOf(sourceVolumeOf);
        var sameVolume = string.Equals(quarantineVolume, sourceVolume, StringComparison.OrdinalIgnoreCase);

        if (!sameVolume)
        {
            // 跨卷：复制 → 校验 → 删源，必须预留空间
            var free = volumes.GetFreeBytes(quarantineVolume);
            if (requiredBytes > 0 && free < requiredBytes)
            {
                return new QuarantinePathValidation(
                    QuarantinePathLevel.Reject,
                    $"隔离区所在磁盘空间不足：需要 {Format(requiredBytes)}，可用 {Format(free)}",
                    $"卷 {quarantineVolume} 可用 {free} 字节，需要 {requiredBytes} 字节");
            }
        }

        if (volumes.IsRemovable(quarantineVolume))
        {
            return new QuarantinePathValidation(
                QuarantinePathLevel.Warn,
                "隔离区位于可移动磁盘：拔掉该磁盘后将无法还原文件，建议改用固定硬盘",
                $"卷 {quarantineVolume} 为可移动介质");
        }

        if (sameVolume)
        {
            return new QuarantinePathValidation(
                QuarantinePathLevel.Warn,
                "隔离区仍在系统盘：清理后 C 盘空间不会立刻释放（要等保留期结束或清空隔离区），建议改到其他盘",
                $"隔离区与待处理文件同卷：{quarantineVolume}");
        }

        return new QuarantinePathValidation(
            QuarantinePathLevel.Ok,
            "隔离区位置可用",
            $"隔离目录：{storageRoot}（卷 {quarantineVolume}）");
    }

    private static string Format(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
