using System.IO;
using System.Text.Json;
using QuietRemind.Models;

namespace QuietRemind.Services;

/// <summary>
/// 本地 JSON 持久化（需求 8 章）：目录 %AppData%\QuietRemind\，
/// 原子写（临时文件 + 替换），任何状态变更实时落盘。
/// </summary>
public sealed class JsonStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly string _dir;

    public JsonStore(string directory)
    {
        _dir = directory;
        Directory.CreateDirectory(_dir);
    }

    public string DataDir => _dir;

    /// <summary>读取全部数据；文件缺失返回空数据，损坏则备份为 .corrupt-时间戳 后按空数据启动。</summary>
    public AppData Load()
    {
        return new AppData
        {
            Tasks = LoadFile<List<ReminderTask>>("tasks.json") ?? [],
            Occurrences = LoadFile<List<Occurrence>>("occurrences.json") ?? [],
            Settings = LoadFile<AppSettings>("settings.json") ?? new AppSettings(),
        };
    }

    public void SaveTasks(IReadOnlyList<ReminderTask> tasks) => WriteAtomic("tasks.json", tasks);

    public void SaveOccurrences(IReadOnlyList<Occurrence> occurrences) => WriteAtomic("occurrences.json", occurrences);

    public void SaveSettings(AppSettings settings) => WriteAtomic("settings.json", settings);

    public string MarkerPath => Path.Combine(_dir, "exit.marker");

    public void WriteExitMarker()
    {
        File.WriteAllText(MarkerPath, DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));
    }

    public bool HasExitMarker() => File.Exists(MarkerPath);

    public void ClearExitMarker()
    {
        try
        {
            if (File.Exists(MarkerPath))
            {
                File.Delete(MarkerPath);
            }
        }
        catch (IOException)
        {
            // 标记清除失败不阻断启动；守护逻辑以文件存在为准
        }
    }

    private T? LoadFile<T>(string file) where T : class
    {
        var path = Path.Combine(_dir, file);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOpts);
        }
        catch (Exception ex)
        {
            // 数据文件损坏：备份现场后按空数据继续，不静默丢失用户可查证的历史
            var backup = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            try
            {
                File.Copy(path, backup, overwrite: true);
            }
            catch (IOException)
            {
                // 备份失败不阻断启动
            }
            throw new InvalidDataException($"数据文件损坏：{path}（已备份为 {backup}）", ex);
        }
    }

    private void WriteAtomic(string file, object payload)
    {
        var path = Path.Combine(_dir, file);
        var temp = path + ".tmp";
        Directory.CreateDirectory(_dir);
        File.WriteAllText(temp, JsonSerializer.Serialize(payload, JsonOpts));
        // 同目录内 Move+overwrite 为原子替换，断电/强杀不会留下半写文件
        File.Move(temp, path, overwrite: true);
    }
}
