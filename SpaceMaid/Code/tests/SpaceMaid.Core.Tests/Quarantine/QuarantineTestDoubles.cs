using SpaceMaid.Core.Abstractions;

namespace SpaceMaid.Core.Tests.Quarantine;

/// <summary>隔离区校验测试用假文件系统：只需要"能否创建目录"这一件事。</summary>
internal sealed class QuarantineFakeFileSystem : IFileSystem
{
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    public bool ThrowOnCreate { get; set; }

    public bool ReportMissingAfterCreate { get; set; }

    public bool FileExists(string path) => false;

    public bool DirectoryExists(string path) => _directories.Contains(path);

    public void CreateDirectory(string path)
    {
        if (ThrowOnCreate)
        {
            throw new UnauthorizedAccessException("拒绝访问");
        }

        if (!ReportMissingAfterCreate)
        {
            _directories.Add(path);
        }
    }

    public long GetFileSize(string path) => 0;

    public DateTimeOffset GetLastWriteTime(string path) => DateTimeOffset.MinValue;

    public DateTimeOffset GetCreationTime(string path) => DateTimeOffset.MinValue;

    public IReadOnlyList<string> EnumerateFiles(string directory, string pattern, bool recurse) => Array.Empty<string>();

    public IReadOnlyList<string> EnumerateDirectories(string directory) => Array.Empty<string>();

    public Stream OpenRead(string path) => throw new NotSupportedException();

    public bool TryMove(string source, string destination, out string error)
    {
        error = string.Empty;
        return false;
    }

    public bool TryCopy(string source, string destination, out string error)
    {
        error = string.Empty;
        return false;
    }

    public bool TryDeleteFile(string path, out string error)
    {
        error = string.Empty;
        return false;
    }

    public bool IsReparsePoint(string path) => false;

    public bool HasReparsePointAncestor(string path) => false;

    public bool IsFileLocked(string path) => false;

    public string ComputeHash(string path, bool full) => string.Empty;
}

/// <summary>隔离区校验测试用假卷探针。</summary>
internal sealed class QuarantineFakeVolumeProbe : IVolumeProbe
{
    public HashSet<string> RemovableVolumes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, long> FreeBytes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string GetVolumeOf(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return path;
        }

        var root = Path.GetPathRoot(Path.GetFullPath(path)) ?? @"C:\";
        return root.EndsWith('\\') ? root : root + "\\";
    }

    public long GetFreeBytes(string volume) => FreeBytes.TryGetValue(volume, out var free) ? free : 0;

    public bool IsRemovable(string volume) => RemovableVolumes.Contains(volume);

    public bool IsUnc(string path) => path.StartsWith(@"\\", StringComparison.Ordinal);
}
