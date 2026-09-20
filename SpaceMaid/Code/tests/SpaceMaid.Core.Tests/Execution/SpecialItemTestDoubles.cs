using SpaceMaid.Core.Abstractions;

namespace SpaceMaid.Core.Tests.Execution;

/// <summary>记录调用并按脚本回应的假命令执行器。</summary>
internal sealed class FakeCommandRunner : ICommandRunner
{
    public List<(string Exe, string Args)> Calls { get; } = new();

    public Func<string, string, CommandResult> Responder { get; set; } = (_, _) => new CommandResult(0, string.Empty, string.Empty);

    public int CallCount => Calls.Count;

    public CommandResult Run(string exe, string arguments, TimeSpan timeout)
    {
        Calls.Add((exe, arguments));
        return Responder(exe, arguments);
    }
}

/// <summary>
/// 脚本化假文件系统：只需要"文件在不在、多大"以及"有没有人调过删除"。
/// 其它成员一律抛异常，用来暴露误用。
/// </summary>
internal sealed class ScriptedFileSystem : IFileSystem
{
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, long> _sizes = new(StringComparer.OrdinalIgnoreCase);

    public List<string> DeletedPaths { get; } = new();

    public List<string> MovedPaths { get; } = new();

    public void AddFile(string path, long size = 1)
    {
        _files.Add(path);
        _sizes[path] = size;
    }

    public void RemoveFile(string path) => _files.Remove(path);

    public bool FileExists(string path) => _files.Contains(path);

    public long GetFileSize(string path) => _sizes.TryGetValue(path, out var size) ? size : 0;

    public bool DirectoryExists(string path) => false;

    public void CreateDirectory(string path)
    {
    }

    public DateTimeOffset GetLastWriteTime(string path) => DateTimeOffset.MinValue;

    public DateTimeOffset GetCreationTime(string path) => DateTimeOffset.MinValue;

    public IReadOnlyList<string> EnumerateFiles(string directory, string pattern, bool recurse) => Array.Empty<string>();

    public IReadOnlyList<string> EnumerateDirectories(string directory) => Array.Empty<string>();

    public Stream OpenRead(string path) => throw new NotSupportedException();

    public bool TryMove(string source, string destination, out string error)
    {
        MovedPaths.Add(source);
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
        DeletedPaths.Add(path);
        error = string.Empty;
        return false;
    }

    public bool IsReparsePoint(string path) => false;

    public bool HasReparsePointAncestor(string path) => false;

    public bool IsFileLocked(string path) => false;

    public string ComputeHash(string path, bool full) => string.Empty;
}

/// <summary>假环境探针：系统盘固定为 C:。</summary>
internal sealed class ExecutionFakeEnvironment : IEnvironmentProbe
{
    public bool IsElevated => true;

    public string SystemDrive => "C:";

    public string ExpandVariables(string raw) => raw;
}
