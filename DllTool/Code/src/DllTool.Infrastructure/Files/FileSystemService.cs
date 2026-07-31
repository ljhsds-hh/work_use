using DllTool.Core.Abstractions;
using DllTool.Core.Models;
using Microsoft.Extensions.Logging;

namespace DllTool.Infrastructure.Files;

/// <summary>
/// 基于 System.IO 的文件系统服务实现。
/// </summary>
public sealed class FileSystemService(ILogger<FileSystemService> logger) : IFileSystemService
{
    /// <summary>
    /// 递归枚举目录下全部 DLL 文件，返回按相对路径（相对目录根、大小写敏感）排序的结果。
    /// </summary>
    public IReadOnlyList<KeyValuePair<RelativePath, string>> EnumerateDlls(string rootDirectory)
    {
        var result = new List<KeyValuePair<RelativePath, string>>();
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));

        foreach (var file in Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories))
        {
            string fullPath = Path.GetFullPath(file);
            string relative = Path.GetRelativePath(root, fullPath);
            try
            {
                result.Add(new KeyValuePair<RelativePath, string>(new RelativePath(relative), fullPath));
            }
            catch (ArgumentException)
            {
                logger.LogWarning("跳过无法解析相对路径的文件：{Path}", fullPath);
            }
        }

        return result;
    }

    /// <summary>
    /// 复制文件到目标路径；自动创建目标目录，源文件保留原位。
    /// 返回 null 表示成功，否则返回失败原因（权限不足、文件占用等）。
    /// </summary>
    public string? CopyFile(string sourcePath, string destinationPath)
    {
        try
        {
            string? destinationDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }
            File.Copy(sourcePath, destinationPath, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            logger.LogWarning("文件复制失败：{Source} → {Dest}，原因：{Reason}", sourcePath, destinationPath, ex.Message);
            return DescribeCopyError(ex);
        }
    }

    private static string DescribeCopyError(Exception ex)
    {
        if (ex is UnauthorizedAccessException)
            return $"拒绝访问：{ex.Message}";

        if (ex is IOException { Message: var msg })
        {
            if (msg.Contains("being used by another process", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("另一进程正在使用", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("sharing violation", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("共享冲突", StringComparison.OrdinalIgnoreCase))
            {
                return $"文件被占用：{msg}";
            }
        }

        return ex switch
        {
            NotSupportedException => $"路径格式不支持：{ex.Message}",
            ArgumentException => $"路径无效：{ex.Message}",
            _ => ex.Message
        };
    }

    public bool IsDirectory(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogWarning("目录检测失败：{Path}，原因：{Reason}", path, ex.Message);
            return false;
        }
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
}
