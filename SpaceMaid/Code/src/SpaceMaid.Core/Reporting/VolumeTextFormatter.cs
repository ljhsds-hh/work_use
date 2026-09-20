namespace SpaceMaid.Core.Reporting;

/// <summary>
/// 体积文案的统一口径（设计决策 D-6 / 需求 3.2-6、3.4-7）。
///
/// 为什么必须集中在一处：全量进隔离区之后，"清理了多少"和"真正释放了多少"是两个不同的数字。
/// 隔离区与源文件同卷时空间**不会立刻释放**，任何地方都不许写成"已释放"，否则就是对用户谎报。
/// </summary>
public static class VolumeTextFormatter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    /// <summary>人类可读体积，例如 1.5 GB。保留两位小数（小于 1 KB 时不加小数）。</summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:0} {Units[unit]}" : $"{value:0.##} {Units[unit]}";
    }

    /// <summary>
    /// 描述一次清理的落地效果：
    /// 跨卷 = 文件已真的离开原盘，可以叫"已释放"；
    /// 同卷 = 文件只是换了个目录，必须说"已移入隔离区（保留期结束或清空隔离区后释放）"。
    /// </summary>
    public static string DescribeProcessed(long bytes, bool sameVolume) =>
        sameVolume
            ? $"已移入隔离区 {FormatBytes(bytes)}（保留期结束或清空隔离区后释放）"
            : $"已释放 {FormatBytes(bytes)}";

    /// <summary>汇总行：同时给出"本次可处理"与"实际效果"两个数字。</summary>
    public static string DescribeSummary(long processableBytes, long processedBytes, bool sameVolume) =>
        $"本次可处理 {FormatBytes(processableBytes)}；{DescribeProcessed(processedBytes, sameVolume)}";
}
