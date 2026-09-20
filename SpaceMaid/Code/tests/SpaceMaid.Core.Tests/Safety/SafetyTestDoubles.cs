using SpaceMaid.Core.Abstractions;

namespace SpaceMaid.Core.Tests.Safety;

/// <summary>
/// 安全测试用的最小假文件系统：只关心"重解析点"相关回答，其余成员抛异常以暴露误用。
/// </summary>
internal sealed class SafetyFakeFileSystem : IFileSystem
{
    public HashSet<string> ReparsePoints { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> ReparseAncestors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsReparsePoint(string path) => ReparsePoints.Contains(path);

    public bool HasReparsePointAncestor(string path) => ReparseAncestors.Contains(path);

    public bool FileExists(string path) => throw new NotSupportedException();

    public bool DirectoryExists(string path) => throw new NotSupportedException();

    public void CreateDirectory(string path) => throw new NotSupportedException();

    public long GetFileSize(string path) => throw new NotSupportedException();

    public DateTimeOffset GetLastWriteTime(string path) => throw new NotSupportedException();

    public DateTimeOffset GetCreationTime(string path) => throw new NotSupportedException();

    public IReadOnlyList<string> EnumerateFiles(string directory, string pattern, bool recurse) =>
        throw new NotSupportedException();

    public IReadOnlyList<string> EnumerateDirectories(string directory) => throw new NotSupportedException();

    public Stream OpenRead(string path) => throw new NotSupportedException();

    public bool TryMove(string source, string destination, out string error) => throw new NotSupportedException();

    public bool TryCopy(string source, string destination, out string error) => throw new NotSupportedException();

    public bool TryDeleteFile(string path, out string error) => throw new NotSupportedException();

    public bool IsFileLocked(string path) => throw new NotSupportedException();

    public string ComputeHash(string path, bool full) => throw new NotSupportedException();
}

/// <summary>安全测试用的假环境：变量展开与系统盘固定为可预期值。</summary>
internal sealed class SafetyFakeEnvironment : IEnvironmentProbe
{
    private readonly Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TEMP"] = @"C:\Users\test\AppData\Local\Temp",
        ["TMP"] = @"C:\Users\test\AppData\Local\Temp",
        ["SystemRoot"] = @"C:\Windows",
        ["windir"] = @"C:\Windows",
        ["USERPROFILE"] = @"C:\Users\test",
        ["LOCALAPPDATA"] = @"C:\Users\test\AppData\Local"
    };

    public bool IsElevated => true;

    public string SystemDrive => "C:";

    public string ExpandVariables(string raw)
    {
        var result = raw;
        foreach (var (key, value) in _variables)
        {
            result = result.Replace($"%{key}%", value, StringComparison.OrdinalIgnoreCase);
        }

        if (result.StartsWith("~", StringComparison.Ordinal))
        {
            result = @"C:\Users\test" + result[1..];
        }

        return result;
    }
}
