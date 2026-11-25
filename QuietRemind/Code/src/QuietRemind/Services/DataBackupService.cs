using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuietRemind.Models;

namespace QuietRemind.Services;

/// <summary>备份包：单一 JSON 覆盖任务、实例与设置；版本用于后续格式演进。</summary>
public sealed class BackupBundle
{
    public int Version { get; set; } = DataBackupService.CurrentVersion;
    public DateTime ExportedAt { get; set; }
    public AppData Data { get; set; } = new();
}

/// <summary>
/// 数据备份：手动导出/导入单一 JSON + 启动时每日滚动快照（保留少量）。
/// 导入前先做数据归一化校验；写入仍走 JsonStore 的原子写。
/// </summary>
public static class DataBackupService
{
    public const int CurrentVersion = 1;
    public const int SnapshotKeep = 5;
    public const int PreImportKeep = 10;
    private const long MaxImportBytes = 50L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// 导入用的严格信封：所有字段可空、无默认值，缺失字段会被识别为非法备份。
    /// 不能用 BackupBundle（其 Version/Data 有默认值），否则把 settings.json 等普通 JSON
    /// 误认成“空备份”并清空全部任务。
    /// </summary>
    private sealed class ImportEnvelope
    {
        public int? Version { get; set; }
        public ImportData? Data { get; set; }
    }

    private sealed class ImportData
    {
        public List<ReminderTask>? Tasks { get; set; }
        public List<Occurrence>? Occurrences { get; set; }
        public AppSettings? Settings { get; set; }
    }

    /// <summary>导出到指定路径（覆盖）。先写临时文件再替换，避免中途失败留下半份备份。</summary>
    public static void Export(string path, AppData data)
    {
        var bundle = new BackupBundle
        {
            ExportedAt = DateTime.Now,
            Data = data,
        };
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(bundle, JsonOpts));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch (Exception)
            {
                // 临时文件清理失败不掩盖导出主流程异常
            }
        }
    }

    /// <summary>
    /// 读取并校验备份文件，返回归一化后的数据（修正项写入 warnings）。
    /// 文件为空/超限/格式错误/版本不支持时抛出 InvalidDataException（消息可直接展示给用户）。
    /// </summary>
    public static AppData Import(string path, out List<string> warnings)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new InvalidDataException("备份文件不存在");
        }
        if (info.Length > MaxImportBytes)
        {
            throw new InvalidDataException($"备份文件超过 {MaxImportBytes / 1024 / 1024}MB，无法导入");
        }

        ImportEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ImportEnvelope>(File.ReadAllText(path), JsonOpts);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"备份文件格式无效：{ex.Message}", ex);
        }

        if (envelope is null)
        {
            throw new InvalidDataException("备份文件内容为空");
        }
        if (envelope.Version is not { } version)
        {
            throw new InvalidDataException("备份缺少版本信息，可能不是 QuietRemind 备份文件");
        }
        if (version != CurrentVersion)
        {
            throw new InvalidDataException($"备份版本 {version} 不受支持（当前支持版本 {CurrentVersion}）");
        }
        if (envelope.Data is not { } importedData
            || importedData.Tasks is null
            || importedData.Occurrences is null
            || importedData.Settings is null)
        {
            throw new InvalidDataException("备份缺少任务/实例/设置数据，可能不是 QuietRemind 备份文件");
        }

        var data = new AppData
        {
            Tasks = importedData.Tasks,
            Occurrences = importedData.Occurrences,
            Settings = importedData.Settings,
        };
        warnings = DataSanitizer.Normalize(data);
        return data;
    }

    /// <summary>导入前自动备份当前数据，返回备份文件路径。</summary>
    public static string CreatePreImportBackup(string dataDir, AppData data)
    {
        var dir = Path.Combine(dataDir, "backups");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"backup-before-import-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        Export(path, data);
        Cleanup(dir, "backup-before-import-*.json", PreImportKeep);
        return path;
    }

    /// <summary>启动时创建当日快照（同一天只写一次），失败由调用方记录日志。</summary>
    public static bool TryCreateDailySnapshot(string dataDir, AppData data, out string? error)
    {
        try
        {
            var dir = Path.Combine(dataDir, "snapshots");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"backup-{DateTime.Now:yyyyMMdd}.json");
            // 空文件视为上次写入中断的残留：重写一次，避免当天永远跳过快照
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                Export(path, data);
            }
            Cleanup(dir, "backup-*.json", SnapshotKeep);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void Cleanup(string dir, string pattern, int keep)
    {
        try
        {
            var files = Directory.GetFiles(dir, pattern)
                .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var file in files.Skip(keep))
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // 单个旧快照删除失败不影响本次备份
                }
            }
        }
        catch (IOException)
        {
            // 目录枚举失败不影响备份主流程
        }
    }
}
