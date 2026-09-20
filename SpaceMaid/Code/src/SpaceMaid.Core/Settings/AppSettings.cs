namespace SpaceMaid.Core.Settings;

/// <summary>
/// 用户设置（需求 §10）。所有字段都有安全默认值：读不到、读坏了都不影响启动。
/// </summary>
public sealed record AppSettings
{
    /// <summary>
    /// 隔离区**基目录**。实际存放位置是它下面的 <c>SpaceMaid\Quarantine</c>（需求 3.4-2：不污染用户目录结构）。
    /// 默认 <c>%LOCALAPPDATA%</c>，因此默认存放位置就是 <c>%LOCALAPPDATA%\SpaceMaid\Quarantine</c>。
    /// </summary>
    public string QuarantineBasePath { get; init; } = DefaultQuarantineBasePath;

    /// <summary>隔离区保留天数（1–365）。</summary>
    public int RetentionDays { get; init; } = 7;

    /// <summary>是否把非系统盘的回收站也纳入（默认否，需求 2.5）。</summary>
    public bool IncludeOtherDriveRecycleBin { get; init; }

    /// <summary>日志目录（默认沿用仓库其他工具约定；不可写时自动回退）。</summary>
    public string LogDirectory { get; init; } = DefaultLogDirectory;

    /// <summary>清单与复核报告的存放目录。</summary>
    public string ReportDirectory { get; init; } = DefaultReportDirectory;

    /// <summary>日志滚动保留天数（惰性清理，需求 6 章）。</summary>
    public int LogRetentionDays { get; init; } = 30;

    public static string DefaultQuarantineBasePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static string DefaultLogDirectory => @"D:\logs\SpaceMaid";

    public static string DefaultReportDirectory => Path.Combine(DefaultLogDirectory, "清单");
}
