using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Models;

namespace SpaceMaid.Core.Scanning;

/// <summary>
/// 针对某个清理项的"候选收窄器"：输入扫描到的候选文件，输出真正允许进入清单的子集。
///
/// 为什么需要这个扩展点：L3 的三个"智能项"（大文件 / 重复文件 / 卸载残留）光靠枚举候选是不够的——
/// 枚举会把目标目录下的所有文件都列出来（可能上万条），而需求 2.4 要求的是"判定之后才列"。
/// 判定逻辑各不相同且依赖 IO（哈希、注册表），塞进 <see cref="ScanningRules"/> 会毁掉它的纯函数性质，
/// 所以单开一个只做"收窄"的扩展点。
///
/// <para>
/// **安全性质（不可协商）**：实现**只能收窄候选集，永远不能扩大**。
/// 唯一的授权方是 <c>SafetyGate</c>——收窄器既不能白名单、也不能放行任何原本不在候选集里的文件。
/// <see cref="ScanEngine"/> 在调用点做了硬守卫：返回值里不属于原候选集的文件一律被丢弃（见 ScanEngine.ApplySelectors），
/// 因此即使某个实现写错了，也不可能出现"清单里多出不该有的文件"。
/// </para>
///
/// <para>
/// **失败关闭**：实现必须把"不确定"一律解释为"不列出"。抛异常时 ScanEngine 按空集处理（不是"全都要"）。
/// </para>
/// </summary>
public interface IItemCandidateSelector
{
    /// <summary>是否负责该清理项（按 <c>CleanItemDefinition.Id</c> 精确匹配）。</summary>
    bool CanHandle(string itemId);

    /// <summary>
    /// 从候选集中挑出真正允许进入清单的文件（结果必须是 <paramref name="candidates"/> 的子集）。
    /// </summary>
    /// <param name="item">清理项定义（实现只读，不得修改）。</param>
    /// <param name="candidates">扫描层枚举到的候选文件（未过滤）。</param>
    /// <param name="fileSystem">文件系统抽象（哈希等只读能力）。</param>
    /// <param name="log">日志汇；实现可用来记录"为什么没列出"。</param>
    IReadOnlyList<ScanFile> Select(
        CleanItemDefinition item,
        IReadOnlyList<ScanFile> candidates,
        IFileSystem fileSystem,
        ILogSink log);
}
