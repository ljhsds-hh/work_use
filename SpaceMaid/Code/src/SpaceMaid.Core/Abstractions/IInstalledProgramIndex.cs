namespace SpaceMaid.Core.Abstractions;

/// <summary>
/// "已安装程序索引"：回答"这个目录还被某个已安装的程序引用吗"。
///
/// 为什么需要抽象：`l3.orphan-app-dirs`（卸载残留目录）的判据完全依赖"注册表里还有没有卸载项指向它"，
/// 而注册表读取既不可单测（内容随机器变化）、又会因权限/损坏而失败，
/// 因此把"读注册表"收在一个接口后面，判定逻辑（收窄器）只依赖这个接口，可以用假实现穷尽单测。
///
/// <para>**保守方向**：判定不确定时必须返回 <c>true</c>（"被引用"→ 不列出）。
/// 读注册表失败、路径无法规范化等一切异常路径，实现都必须返回 <c>true</c> 并记日志。</para>
/// </summary>
public interface IInstalledProgramIndex
{
    /// <summary>
    /// 该目录是否被任何已安装程序引用（安装位置 / 卸载命令 / 图标路径）。
    /// 返回 <c>true</c> 表示"在用或有疑问，不要动"。
    /// </summary>
    bool IsReferenced(string directoryPath);
}
