using DllTool.Core.Abstractions;
using DllTool.Core.Models;
using Microsoft.Extensions.Logging;

namespace DllTool.Core.Backup;

/// <summary>
/// 解析本次专属备份目录。
/// 空目录直接使用；非空目录下创建不重复命名的子文件夹，避免与历史备份数据混淆。
/// </summary>
public sealed class BackupDirectoryResolver(IFileSystemService fileSystem, ILogger<BackupDirectoryResolver> logger)
{
    private const string SubFolderPrefix = "backup_";

    /// <summary>
    /// 根据用户选择的备份根目录解析本次专属备份目录。
    /// 返回创建完成（或已存在）的备份目录绝对路径。
    /// </summary>
    public string Resolve(string selectedRoot)
    {
        if (string.IsNullOrWhiteSpace(selectedRoot))
            throw new ArgumentException("备份根目录不能为空。", nameof(selectedRoot));

        fileSystem.CreateDirectory(selectedRoot);
        logger.LogInformation("备份根目录已确认：{Root}", selectedRoot);

        if (Directory.EnumerateFileSystemEntries(selectedRoot).Any())
        {
            string sub = CreateNonConflictingSubdirectory(selectedRoot);
            logger.LogInformation("备份根目录非空，创建专属子目录：{Sub}", sub);
            return sub;
        }

        logger.LogInformation("备份根目录为空，直接作为本次备份目录使用。");
        return selectedRoot;
    }

    private string CreateNonConflictingSubdirectory(string root)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            string candidate = Path.Combine(root, SubFolderPrefix + Guid.NewGuid().ToString("N")[..12]);
            try
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }
            catch (IOException)
            {
                // 极小概率命名冲突，重试。
            }
            catch (UnauthorizedAccessException)
            {
                throw new UnauthorizedAccessException($"无法在备份根目录下创建子目录：{candidate}");
            }
        }

        throw new InvalidOperationException("无法在备份根目录下创建不重复的专属子目录。");
    }
}
