using System.IO;
using SpaceMaid.Core.Abstractions;

namespace SpaceMaid.Core.Platform;

/// <summary>
/// <see cref="IVolumeProbe"/> 的 Windows 实现。
/// 为什么不缓存 <see cref="DriveInfo"/>：本进程是短生命周期工具，缓存只会带来"U 盘换盘后数据过期"的麻烦。
/// </summary>
public sealed class WindowsVolumeProbe : IVolumeProbe
{
    /// <inheritdoc />
    /// <remarks>
    /// 用 <see cref="Path.GetPathRoot(string)"/> 取卷根（如 <c>C:\</c>）；
    /// 路径不可解析时返回空串，由调用方（隔离区校验）判为"拒绝"。
    /// </remarks>
    public string GetVolumeOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetPathRoot(Path.GetFullPath(path)) ?? string.Empty;
        }
        catch (Exception)
        {
            // 含非法字符的路径：交给上层判为不可用，而不是在这里抛。
            return string.Empty;
        }
    }

    /// <inheritdoc />
    /// <remarks>卷不存在（驱动器未就绪、BIT LOCKER 锁定）时 <see cref="DriveInfo"/> 会抛异常，统一返回 0。</remarks>
    public long GetFreeBytes(string volume)
    {
        if (string.IsNullOrWhiteSpace(volume))
        {
            return 0;
        }

        try
        {
            DriveInfo drive = new DriveInfo(volume);
            return drive.IsReady ? drive.AvailableFreeSpace : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 可移动介质（U 盘/存储卡）拔出后隔离文件无法还原，因此必须由校验层拒绝或警告（设计文档 §6.1）。
    /// </remarks>
    public bool IsRemovable(string volume)
    {
        if (string.IsNullOrWhiteSpace(volume))
        {
            return false;
        }

        try
        {
            DriveInfo drive = new DriveInfo(volume);
            return drive.DriveType == DriveType.Removable;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>判定用 <c>\\</c> 前缀，与设计文档 §6.1"网络路径（<c>\\</c> 开头、映射驱动器）"一致。</remarks>
    public bool IsUnc(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        // 注意：不能用 Path.IsPathRooted 代替，它对本机盘符与 UNC 都会返回 true。
        return path.StartsWith(@"\\", StringComparison.Ordinal);
    }
}
