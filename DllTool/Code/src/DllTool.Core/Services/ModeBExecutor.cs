using DllTool.Core.Abstractions;
using DllTool.Core.Backup;
using DllTool.Core.Models;
using Microsoft.Extensions.Logging;

namespace DllTool.Core.Services;

/// <summary>
/// 模式B执行器：数据源为【DLL清单文本文件】，仅备份、无覆盖流程。
/// </summary>
public sealed class ModeBExecutor(
    IFileSystemService fileSystem,
    BackupDirectoryResolver backupResolver,
    ILogger<ModeBExecutor> logger)
{
    private const string ManifestFileName = "backup_list.txt";

    /// <summary>
    /// 执行完整流程。
    /// </summary>
    /// <param name="manifestFile">清单文本文件路径。</param>
    /// <param name="targetDirectory">目标目录。</param>
    /// <param name="backupRoot">备份根目录。</param>
    public OperationResult Execute(string manifestFile, string targetDirectory, string backupRoot)
    {
        logger.LogInformation("===== 模式B开始 =====");
        logger.LogInformation("清单文件：{Manifest}，目标目录：{Target}，备份根目录：{Backup}",
            manifestFile, targetDirectory, backupRoot);

        var result = new OperationResult { ModeName = "DLL清单文件" };

        var lines = ReadManifestLines(manifestFile);
        logger.LogInformation("清单条目总数：{Count}", lines.Count);

        var targetDlls = fileSystem.EnumerateDlls(targetDirectory);
        var targetByRelative = targetDlls.ToDictionary(kv => kv.Key, kv => kv.Value);

        // 严格精确匹配（区分大小写）：与目标目录内文件相对路径完全一致才纳入。
        var matched = new List<RelativePath>();
        foreach (var line in lines)
        {
            var relative = new RelativePath(line);
            if (targetByRelative.ContainsKey(relative))
            {
                matched.Add(relative);
            }
            else
            {
                logger.LogInformation("清单条目未匹配（目标目录无此相对路径）：{Entry}", line);
                result.Entries.Add(new OperationEntry
                {
                    RelativePath = line,
                    Action = OperationActions.Backup,
                    Status = OperationStatus.Skipped,
                    Reason = "目标目录中不存在"
                });
            }
        }

        logger.LogInformation("匹配成功的清单条目数量：{Count}", matched.Count);

        if (matched.Count == 0)
        {
            logger.LogInformation("无匹配文件，流程结束。");
            return result;
        }

        string backupDirectory = backupResolver.Resolve(backupRoot);
        result.BackupDirectory = backupDirectory;

        foreach (var relative in matched)
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

        WriteManifest(result, backupDirectory);
        logger.LogInformation("备份清单已生成：{Manifest}", result.ManifestPath);

        logger.LogInformation("===== 模式B结束 =====");
        return result;
    }

    private static List<string> ReadManifestLines(string manifestFile)
    {
        var lines = new List<string>();
        foreach (var raw in File.ReadLines(manifestFile))
        {
            string line = raw.Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith('#'))
            {
                // 视为注释行，跳过（兼容含说明的清单）。
                continue;
            }

            lines.Add(line);
        }
        return lines;
    }

    private void WriteManifest(OperationResult result, string backupDirectory)
    {
        string manifestPath = Path.Combine(backupDirectory, ManifestFileName);
        using var writer = new StreamWriter(manifestPath, append: false, encoding: System.Text.Encoding.UTF8);
        // 成功条目正常罗列；复制失败的条目以失败状态标记；未匹配（跳过）条目不写入。
        foreach (var entry in result.Entries.Where(e => e.Action == OperationActions.Backup))
        {
            if (entry.Status == OperationStatus.Success)
            {
                writer.WriteLine(entry.RelativePath);
            }
            else if (entry.Status == OperationStatus.Failed)
            {
                writer.WriteLine($"#FAILED {entry.RelativePath}");
            }
        }
        result.ManifestPath = manifestPath;
    }
}
