namespace SpaceMaid.Core.Abstractions;

/// <summary>
/// 运行环境探针（管理员身份、系统盘、环境变量展开）。
/// 为什么需要抽象：路径规范化与 Denylist 判定必须能脱离真实机器被单测，
/// 且"是否提权"决定了休眠闸门等动作能否执行（需求 3.6/3.8）。
/// </summary>
public interface IEnvironmentProbe
{
    /// <summary>当前进程是否以管理员（提权）身份运行。</summary>
    bool IsElevated { get; }

    /// <summary>系统盘盘符，例如 <c>"C:"</c>（不含尾部反斜杠）。</summary>
    string SystemDrive { get; }

    /// <summary>
    /// 展开 <c>%VAR%</c> 与 <c>~</c>。
    /// 契约：**未知变量原样保留**（不展开为空串），因为 PathNormalizer 要靠"结果里还有 %"
    /// 来拒绝含未展开变量的落地路径（设计文档 §3.2 第 3 步）。
    /// </summary>
    /// <param name="raw">原始路径或参数字符串。</param>
    /// <returns>展开后的字符串。</returns>
    string ExpandVariables(string raw);
}
