namespace SpaceMaid.Core.Models;

/// <summary>落地授权的判定结果（先拒后允，顺序见设计文档 3.4）。</summary>
public enum SafetyVerdict
{
    Allowed,

    /// <summary>路径本身不安全（相对路径、含通配符、无法规范化）。</summary>
    UnsafePath,

    /// <summary>命中禁止清单（硬编码常量，任何配置都不能突破）。</summary>
    DeniedByDenylist,

    /// <summary>路径本身或祖先目录是重解析点（符号链接 / junction），拒绝顺着链接操作。</summary>
    ReparsePoint,

    /// <summary>不在该清理项的允许路径子树内。</summary>
    OutsideAllowlist
}

/// <summary>落地授权决策。Reason 必须是可直接展示与写日志的中文。</summary>
public sealed record SafetyDecision(SafetyVerdict Verdict, string Reason)
{
    public bool IsAllowed => Verdict == SafetyVerdict.Allowed;

    public static SafetyDecision Allow(string reason) => new(SafetyVerdict.Allowed, reason);
}
