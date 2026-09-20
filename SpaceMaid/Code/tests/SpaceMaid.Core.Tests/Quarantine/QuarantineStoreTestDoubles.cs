using SpaceMaid.Core.Abstractions;

namespace SpaceMaid.Core.Tests.Quarantine;

/// <summary>把指定路径前缀映射到指定卷，用来在同一个临时目录里模拟"同卷/跨卷"。</summary>
internal sealed class MappedVolumeProbe : IVolumeProbe
{
    private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

    public long FreeBytes { get; set; } = 100L * 1024 * 1024 * 1024;

    public void MapTo(string pathPrefix, string volume) => _map[pathPrefix] = volume;

    public string GetVolumeOf(string path)
    {
        foreach (var (prefix, volume) in _map.OrderByDescending(kv => kv.Key.Length))
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return volume;
            }
        }

        var root = Path.GetPathRoot(Path.GetFullPath(path)) ?? @"C:\";
        return root.EndsWith('\\') ? root : root + "\\";
    }

    public long GetFreeBytes(string volume) => FreeBytes;

    public bool IsRemovable(string volume) => false;

    public bool IsUnc(string path) => path.StartsWith(@"\\", StringComparison.Ordinal);
}

/// <summary>可注入失败的装饰器：用来验证"复制失败必须保留源文件"。</summary>
internal sealed class FailingCopyFileSystem : IFileSystem
{
    private readonly IFileSystem _inner;

    public FailingCopyFileSystem(IFileSystem inner) => _inner = inner;

    public bool FailCopy { get; set; }

    public bool FailDelete { get; set; }

    public bool FileExists(string path) => _inner.FileExists(path);

    public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

    public void CreateDirectory(string path) => _inner.CreateDirectory(path);

    public long GetFileSize(string path) => _inner.GetFileSize(path);

    public DateTimeOffset GetLastWriteTime(string path) => _inner.GetLastWriteTime(path);

    public DateTimeOffset GetCreationTime(string path) => _inner.GetCreationTime(path);

    public IReadOnlyList<string> EnumerateFiles(string directory, string pattern, bool recurse) =>
        _inner.EnumerateFiles(directory, pattern, recurse);

    public IReadOnlyList<string> EnumerateDirectories(string directory) => _inner.EnumerateDirectories(directory);

    public Stream OpenRead(string path) => _inner.OpenRead(path);

    public bool TryMove(string source, string destination, out string error) => _inner.TryMove(source, destination, out error);

    public bool TryCopy(string source, string destination, out string error)
    {
        if (FailCopy)
        {
            error = "模拟复制失败（磁盘错误）";
            return false;
        }

        return _inner.TryCopy(source, destination, out error);
    }

    public bool TryDeleteFile(string path, out string error)
    {
        if (FailDelete)
        {
            error = "模拟删除失败（被占用）";
            return false;
        }

        return _inner.TryDeleteFile(path, out error);
    }

    public bool IsReparsePoint(string path) => _inner.IsReparsePoint(path);

    public bool HasReparsePointAncestor(string path) => _inner.HasReparsePointAncestor(path);

    public bool IsFileLocked(string path) => _inner.IsFileLocked(path);

    public string ComputeHash(string path, bool full) => _inner.ComputeHash(path, full);
}

/// <summary>隔离区测试用固定时钟。</summary>
internal sealed class QuarantineFakeClock : IClock
{
    public QuarantineFakeClock(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }
}
