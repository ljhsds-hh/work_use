using DllTool.Core.Abstractions;
using DllTool.Core.Models;

namespace DllTool.Core.Validation;

/// <summary>
/// 流程启动前的输入校验。
/// </summary>
public sealed class FlowValidator(IFileSystemService fileSystem)
{
    /// <summary>
    /// 校验模式A：新DLL数据源目录与目标目录禁止相同或重合。
    /// </summary>
    public string? ValidateModeA(string? newDllDirectory, string? targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(newDllDirectory))
            return "请选择新DLL文件夹。";
        if (string.IsNullOrWhiteSpace(targetDirectory))
            return "请选择目标目录。";
        if (!fileSystem.IsDirectory(newDllDirectory))
            return "新DLL文件夹不存在或不是目录。";
        if (!fileSystem.IsDirectory(targetDirectory))
            return "目标目录不存在或不是目录。";
        if (PathsOverlap(newDllDirectory, targetDirectory))
            return "新DLL文件夹与目标目录不能是同一路径或相互包含，请重新选择。";
        return null;
    }

    /// <summary>
    /// 校验模式B：清单文件与目标目录。
    /// </summary>
    public string? ValidateModeB(string? manifestFile, string? targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(manifestFile))
            return "请选择DLL清单文本文件。";
        if (string.IsNullOrWhiteSpace(targetDirectory))
            return "请选择目标目录。";
        if (!File.Exists(manifestFile))
            return "DLL清单文件不存在。";
        if (!fileSystem.IsDirectory(targetDirectory))
            return "目标目录不存在或不是目录。";
        return null;
    }

    /// <summary>
    /// 校验备份根目录：禁止与目标目录或新DLL数据源目录相同/包含（规避自污染）。
    /// </summary>
    public string? ValidateBackupRoot(string backupRoot, string targetDirectory, string newDllDirectory)
    {
        if (string.IsNullOrWhiteSpace(backupRoot))
            return "请选择备份存储根目录。";

        if (PathsOverlap(backupRoot, targetDirectory))
            return "备份存储根目录不能位于目标目录内或与其相同（会自我污染备份数据），请重新选择。";

        if (PathsOverlap(backupRoot, newDllDirectory))
            return "备份存储根目录不能位于新DLL文件夹内或与其相同，请重新选择。";

        return null;
    }

    /// <summary>两个路径是否相同或互为包含关系（忽略末尾分隔符差异，大小写不敏感）。</summary>
    private static bool PathsOverlap(string first, string second)
    {
        string a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)).TrimEnd('\\');
        string b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)).TrimEnd('\\');

        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return true;

        string prefix = a + Path.DirectorySeparatorChar;
        string suffix = b + Path.DirectorySeparatorChar;
        return b.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || a.StartsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }
}
