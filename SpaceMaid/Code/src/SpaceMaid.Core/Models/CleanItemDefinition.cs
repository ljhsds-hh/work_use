namespace SpaceMaid.Core.Models;

/// <summary>
/// 一个清理项的候选路径规则。Path 支持 %VAR%（由 IEnvironmentProbe 展开）。
/// Pattern 只允许出现在这里——落地路径中禁止通配符（需求 4.1）。
/// </summary>
public sealed record TargetRule
{
    public required TargetKind Kind { get; init; }

    /// <summary>固定枚举根（不含通配符，可用 %VAR% 模板）。</summary>
    public required string Path { get; init; }

    /// <summary>文件名匹配模式（如 <c>*.dmp</c>；默认 <c>*</c>）。</summary>
    public string Pattern { get; init; } = "*";

    /// <summary>
    /// 相对 <see cref="Path"/> 的**子目录模式**，可含 <c>*</c> 段，
    /// 例如 <c>*\LocalCache\Temp</c>（应用包临时目录）、<c>*\cache2</c>（Firefox 缓存）。
    /// 为空表示直接枚举 <see cref="Path"/>。
    /// 为什么要单独一个字段：Path 里出现通配符会让路径无法规范化（进而无法做禁止清单校验），
    /// 所以通配只允许出现在这里，扫描层负责把它展开成若干真实目录。
    /// </summary>
    public string? SubPathPattern { get; init; }

    public bool Recurse { get; init; }

    public static TargetRule Contents(string path, string pattern = "*") =>
        new() { Kind = TargetKind.DirectoryContents, Path = path, Pattern = pattern };

    public static TargetRule ContentsUnder(string path, string subPathPattern, string pattern = "*") =>
        new() { Kind = TargetKind.DirectoryContents, Path = path, SubPathPattern = subPathPattern, Pattern = pattern };

    public static TargetRule Tree(string path) =>
        new() { Kind = TargetKind.DirectoryTree, Path = path, Recurse = true };

    public static TargetRule Glob(string path, string pattern) =>
        new() { Kind = TargetKind.FileGlob, Path = path, Pattern = pattern };

    public static TargetRule GlobUnder(string path, string subPathPattern, string pattern) =>
        new() { Kind = TargetKind.FileGlob, Path = path, SubPathPattern = subPathPattern, Pattern = pattern };

    public static TargetRule File(string path) =>
        new() { Kind = TargetKind.FixedFile, Path = path };
}

/// <summary>
/// 清理项定义（清单闭集的元素，需求 4.1）。
/// Id 一经发布不得复用/改名——隔离区映射表与清单 csv 靠它与用户历史报告对应。
/// </summary>
public sealed record CleanItemDefinition
{
    public required string Id { get; init; }

    public required CleanCategory Category { get; init; }

    /// <summary>界面显示名称（不含括号动作性质）。</summary>
    public required string DisplayName { get; init; }

    public required ItemRisk Risk { get; init; }

    public required CleanActionKind ActionKind { get; init; }

    public required IReadOnlyList<TargetRule> Targets { get; init; }

    /// <summary>括号里的"动作性质"，例如"仅关闭功能，不删文件"（需求 5.4）。风险项必填。</summary>
    public string ActionNote { get; init; } = string.Empty;

    /// <summary>用用户能看懂的话写清"会发生什么"（禁止写空话）。</summary>
    public string SideEffect { get; init; } = string.Empty;

    /// <summary>恢复方式，例如"恢复：powercfg /h on"。L3 必填。</summary>
    public string RestoreHint { get; init; } = string.Empty;

    /// <summary>默认勾选状态。保守原则：只有 AutoRegenerated 的项才允许为 true（需求 1.2-9 / 5.4-1）。</summary>
    public bool DefaultChecked { get; init; }

    /// <summary>L1 判据字段：删掉之后会被自动重建（或系统本就会自动清理）。</summary>
    public bool AutoRegenerated { get; init; }

    /// <summary>只处理超过该时长未修改的文件（null = 不限）。</summary>
    public TimeSpan? MinAge { get; init; }

    /// <summary>保留最新一份，其余处理（蓝屏转储，需求 2.2）。</summary>
    public bool KeepsNewest { get; init; }

    public bool RequiresElevation { get; init; }

    /// <summary>不可用/需额外提示时展示给用户的说明（例如"升级不足 10 天，默认不勾"）。</summary>
    public string? AvailabilityNote { get; init; }

    /// <summary>界面显示名（风险项自动带括号动作性质）。</summary>
    public string DisplayNameWithNote =>
        string.IsNullOrWhiteSpace(ActionNote) ? DisplayName : $"{DisplayName}（{ActionNote}）";
}
