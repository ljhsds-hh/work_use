using DllTool.Core.Models;

namespace DllTool.Core.Abstractions;

/// <summary>
/// 文件系统操作抽象，供领域层调用、由基础设施层实现。
/// </summary>
public interface IFileSystemService
{
    /// <summary>递归枚举目录下全部 DLL 文件，返回相对路径与绝对路径。</summary>
    IReadOnlyList<KeyValuePair<RelativePath, string>> EnumerateDlls(string rootDirectory);

    /// <summary>复制文件到目标路径；源文件保留原位。返回 null 表示成功，否则返回失败原因。</summary>
    string? CopyFile(string sourcePath, string destinationPath);

    /// <summary>判断路径是否为已存在的目录。</summary>
    bool IsDirectory(string path);

    /// <summary>创建目录（含中间层）。</summary>
    void CreateDirectory(string path);
}
