namespace SpaceMaid.Core.Abstractions;

/// <summary>
/// 卷信息探针（卷根、剩余空间、介质类型、网络路径）。
/// 为什么需要抽象：隔离区路径校验与"同卷 / 跨卷"体积口径（设计文档 D-6）都依赖它；
/// 跨卷用例用假实现把两个临时目录标成不同卷即可测（设计文档 §11 跨卷测试）。
/// </summary>
public interface IVolumeProbe
{
    /// <summary>返回路径所在卷的根，例如 <c>C:\</c> 或 <c>D:\</c>（带尾部反斜杠）。</summary>
    /// <param name="path">任意文件或目录路径。</param>
    string GetVolumeOf(string path);

    /// <summary>返回指定卷的剩余可用字节数；卷不存在或不可用时返回 0。</summary>
    /// <param name="volume">卷根，例如 <c>C:\</c>。</param>
    long GetFreeBytes(string volume);

    /// <summary>指定卷是否为可移动介质（U 盘、SD 卡等，拔出后隔离文件不可还原）。</summary>
    /// <param name="volume">卷根，例如 <c>E:\</c>。</param>
    bool IsRemovable(string volume);

    /// <summary>路径是否为 UNC 网络路径（<c>\\server\share</c> 形式）。</summary>
    /// <param name="path">任意路径。</param>
    bool IsUnc(string path);
}
