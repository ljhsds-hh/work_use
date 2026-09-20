using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Scanning;

namespace SpaceMaid.Core.Tests.Scanning;

/// <summary>扫描测试用假卷探针：按盘符判定卷，空间与介质类型可配置。</summary>
internal sealed class ScanFakeVolumeProbe : IVolumeProbe
{
    public Dictionary<string, long> FreeBytes { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [@"C:\"] = 10L * 1024 * 1024 * 1024,
        [@"D:\"] = 100L * 1024 * 1024 * 1024
    };

    public string GetVolumeOf(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path)) ?? @"C:\";
        return root.EndsWith('\\') ? root : root + "\\";
    }

    public long GetFreeBytes(string volume) => FreeBytes.TryGetValue(volume, out var free) ? free : 0;

    public bool IsRemovable(string volume) => false;

    public bool IsUnc(string path) => path.StartsWith(@"\\", StringComparison.Ordinal);
}

/// <summary>扫描测试用假容量探针。</summary>
internal sealed class ScanFakeCapacityProbe : IVolumeCapacityProbe
{
    public long TotalBytes { get; set; } = 200L * 1024 * 1024 * 1024;

    public long GetTotalBytes(string volume) => TotalBytes;
}

/// <summary>测试用固定时钟。</summary>
internal sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }
}
