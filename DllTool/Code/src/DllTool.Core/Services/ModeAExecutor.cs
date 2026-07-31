using DllTool.Core.Abstractions;
using DllTool.Core.Backup;
using DllTool.Core.Models;
using Microsoft.Extensions.Logging;

namespace DllTool.Core.Services;

/// <summary>
/// 模式A执行器：数据源为【实体DLL文件夹】，备份 + 可选覆盖双流程。
/// 拆分为 Backup（静默备份）与 Overwrite（覆盖）两个阶段，覆盖确认弹窗由调用方在两者之间弹出。
/// </summary>
public sealed class ModeAExecutor(
    IFileSystemService fileSystem,
    BackupDirectoryResolver backupResolver,
    ILogger<ModeAExecutor> logger)
{
    private const string ManifestFileName = "backup_list.txt";

    /// <summary>
    /// 阶段1：静默备份。将两端相对路径重合的旧版DLL复制至本次备份目录，生成备份清单。
    /// </summary>
    /// <returns>备份结果；<see cref="ModeABackupResult.MatchedRelativePaths"/> 为空表示无重合文件。</returns>
    public ModeABackupResult Backup(string newDllDirectory, string targetDirectory, string backupRoot)
    {
        logger.LogInformation("===== 模式A·备份阶段开始 =====");
        logger.LogInformation("新DLL文件夹：{New}，目标目录：{Target}，备份根目录：{Backup}",
            newDllDirectory, targetDirectory, backupRoot);

        var result = new ModeABackupResult();

        var newDlls = fileSystem.EnumerateDlls(newDllDirectory);
        var targetDlls = fileSystem.EnumerateDlls(targetDirectory);
        logger.LogInformation("新DLL数量：{NewCount}，目标DLL数量：{TargetCount}", newDlls.Count, targetDlls.Count);

        var targetByRelative = targetDlls.ToDictionary(kv => kv.Key, kv => kv.Value);
        var newByRelative = newDlls.ToDictionary(kv => kv.Key, kv => kv.Value);

        var overlaps = newByRelative.Keys
            .Where(targetByRelative.ContainsKey)
            .OrderBy(r => r.Value, StringComparer.Ordinal)
            .ToList();

        result.MatchedRelativePaths.AddRange(overlaps);
        logger.LogInformation("两端相对路径重合的DLL数量：{OverlapCount}", overlaps.Count);

        if (overlaps.Count == 0)
        {
            logger.LogInformation("无重合文件，备份阶段结束。");
            return result;
        }

        string backupDirectory = backupResolver.Resolve(backupRoot);
        result.BackupDirectory = backupDirectory;

        foreach (var relative in overlaps)
        {
            string source = targetByRelative[relative];
            string destination = Path.Combine(backupDirectory, relative.Value);

            string? error = fileSystem.CopyFile(source, destination);
            if (error is null)
            {
                logger.LogInformation("备份成功：{Relative}（{Source} → {Dest}）", relative.Value, source, destination);
                result.Entries.Add(new OperationEntry
                {
                    RelativePath = relative.Value,
                    Action = OperationActions.Backup,
                    Status = OperationStatus.Success
                });
            }
            else
            {
                logger.LogWarning("备份失败：{Relative}（{Source}）原因：{Error}", relative.Value, source, error);
                result.Entries.Add(new OperationEntry
                {
                    RelativePath = relative.Value,
                    Action = OperationActions.Backup,
                    Status = OperationStatus.Failed,
                    Reason = error
                });
            }
        }

        WriteManifest(result);
        logger.LogInformation("备份清单已生成：{Manifest}", result.ManifestPath);
        logger.LogInformation("===== 模式A·备份阶段结束 =====");
        return result;
    }

    /// <summary>
    /// 阶段2：覆盖。使用新版DLL按相对路径逐一覆盖目标目录内对应旧文件。
    /// </summary>
    /// <param name="newDllDirectory">新DLL文件夹（相对路径基准）。</param>
    /// <param name="targetDirectory">目标目录。</param>
    /// <param name="backupResult">备份阶段结果（提供重合清单）。</param>
    /// <returns>合并备份与覆盖条目的完整操作结果。</returns>
    public OperationResult Overwrite(string newDllDirectory, string targetDirectory, ModeABackupResult backupResult)
    {
        logger.LogInformation("===== 模式A·覆盖阶段开始 =====");

        var result = new OperationResult
        {
            ModeName = "实体DLL文件夹",
            BackupDirectory = backupResult.BackupDirectory,
            ManifestPath = backupResult.ManifestPath,
            OverwriteExecuted = true
        };
        result.Entries.AddRange(backupResult.Entries);

        var newByRelative = fileSystem.EnumerateDlls(newDllDirectory).ToDictionary(kv => kv.Key, kv => kv.Value);
        var targetByRelative = fileSystem.EnumerateDlls(targetDirectory).ToDictionary(kv => kv.Key, kv => kv.Value);

        foreach (var relative in backupResult.MatchedRelativePaths)
        {
            if (!newByRelative.TryGetValue(relative, out var newSource) ||
                !targetByRelative.TryGetValue(relative, out var targetPath))
            {
                logger.LogWarning("覆盖跳过（文件已不在匹配清单中）：{Relative}", relative.Value);
                continue;
            }

            string? error = fileSystem.CopyFile(newSource, targetPath);
            if (error is null)
            {
                logger.LogInformation("覆盖成功：{Relative}", relative.Value);
                result.Entries.Add(new OperationEntry
                {
                    RelativePath = relative.Value,
                    Action = OperationActions.Overwrite,
                    Status = OperationStatus.Success
                });
            }
            else
            {
                logger.LogWarning("覆盖失败：{Relative}（{Target}）原因：{Error}", relative.Value, targetPath, error);
                result.Entries.Add(new OperationEntry
                {
                    RelativePath = relative.Value,
                    Action = OperationActions.Overwrite,
                    Status = OperationStatus.Failed,
                    Reason = error
                });
            }
        }

        logger.LogInformation("===== 模式A·覆盖阶段结束 =====");
        return result;
    }

    private void WriteManifest(ModeABackupResult result)
    {
        string manifestPath = Path.Combine(result.BackupDirectory!, ManifestFileName);
        using var writer = new StreamWriter(manifestPath, append: false, encoding: System.Text.Encoding.UTF8);
        foreach (var entry in result.Entries)
        {
            if (entry.Status == OperationStatus.Success)
            {
                writer.WriteLine(entry.RelativePath);
            }
            else
            {
                writer.WriteLine($"#FAILED {entry.RelativePath}");
            }
        }
        result.ManifestPath = manifestPath;
    }
}

/// <summary>
/// 模式A备份阶段的结果。
/// </summary>
public sealed class ModeABackupResult
{
    /// <summary>两端重合的相对路径清单（覆盖阶段据此执行）。</summary>
    public List<RelativePath> MatchedRelativePaths { get; } = [];

    /// <summary>备份条目记录。</summary>
    public List<OperationEntry> Entries { get; } = [];

    /// <summary>本次专属备份目录完整路径（无重合文件时为 null）。</summary>
    public string? BackupDirectory { get; set; }

    /// <summary>备份清单文件完整路径（无重合文件时为 null）。</summary>
    public string? ManifestPath { get; set; }

    public int BackupSucceeded => Entries.Count(e => e.Status == OperationStatus.Success);
    public int BackupFailed => Entries.Count(e => e.Status == OperationStatus.Failed);
}
