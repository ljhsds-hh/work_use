using System.IO;
using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Scanning;

namespace SpaceMaid.Core.Platform;

/// <summary>
/// 卷容量探针的生产实现：用 <see cref="DriveInfo"/> 取总容量。
///
/// 为什么需要它：界面的"磁盘总览"和清单头部都会显示总容量，探针缺省时那个数字会显示成 <c>0 B</c>——
/// 首次真机 dry-run 就暴露了这一点（"系统盘：C:\（总容量 0 B，可用 64.54 GB）"）。
/// </summary>
public sealed class WindowsVolumeCapacityProbe : IVolumeCapacityProbe
{
    /// <inheritdoc />
    public long GetTotalBytes(string volume)
    {
        if (string.IsNullOrWhiteSpace(volume))
        {
            return 0;
        }

        try
        {
            return new DriveInfo(volume).TotalSize;
        }
        catch (Exception)
        {
            // 卷不存在/无权限：返回 0，界面按"未知"展示，绝不抛异常打断扫描
            return 0;
        }
    }
}
