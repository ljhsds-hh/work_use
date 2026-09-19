using System.IO;
using System.Text.Json;
using QuietRemind.Models;

namespace QuietRemind.Services;

/// <summary>数据加载结果：Data 为加载到的数据（损坏/缺失域为空默认值），Problems 为逐文件的问题描述。</summary>
public sealed class LoadResult
{
    public AppData Data { get; init; } = new();
    public List<string> Problems { get; init; } = [];
}

/// <summary>
/// 本地 JSON 持久化（需求 8 章）：目录 %AppData%\QuietRemind\，
/// 原子写（临时文件 + 替换），任何状态变更实时落盘。
/// 损坏隔离：单个文件损坏只影响该域（备份后按空数据加载），本次会话内禁写该文件，
/// 其余完好的数据域照常加载与保存，避免一次损坏连带清空全部数据。
/// </summary>
public sealed class JsonStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly string _dir;
    private readonly string _markerPath;
    private readonly HashSet<string> _noWriteFiles = new(StringComparer.OrdinalIgnoreCase);

    public JsonStore(string directory, string? markerPath = null)
    {
        _dir = directory;
        // 默认与数据同目录；App 可指向日志目录。
        // 背景：部分环境（受限令牌的计划任务进程）对 %AppData% 新写入文件存在视图隔离，
        // 而 D:\logs 日志目录在用户进程与守护进程间视图一致，故退出标记存放日志目录。
        _markerPath = markerPath ?? Path.Combine(_dir, "exit.marker");
        Directory.CreateDirectory(_dir);
    }

    public LoadResult Load()
    {
        return new LoadResult
        {
            Data = new AppData
            {
                Tasks = LoadFile<List<ReminderTask>>("tasks.json") ?? [],
                Occurrences = LoadFile<List<Occurrence>>("occurrences.json") ?? [],
                Settings = LoadFile<AppSettings>("settings.json") ?? new AppSettings(),
            },
            Problems = [.. _problems],
        };
    }

    private readonly List<string> _problems = [];

    public void SaveTasks(IReadOnlyList<ReminderTask> tasks) => WriteAtomic("tasks.json", tasks);

    public void SaveOccurrences(IReadOnlyList<Occurrence> occurrences) => WriteAtomic("occurrences.json", occurrences);

    public void SaveSettings(AppSettings settings) => WriteAtomic("settings.json", settings);

    public void WriteExitMarker()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
        File.WriteAllText(_markerPath, DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));
    }

    public bool HasExitMarker() => File.Exists(_markerPath);

    public void ClearExitMarker()
    {
        try
        {
            if (File.Exists(_markerPath))
            {
                File.Delete(_markerPath);
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

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            // 瞬时读取失败（杀软扫描/短暂占用）：不按损坏处理，但本次会话禁写该文件，避免覆盖用户数据
            _noWriteFiles.Add(file);
            _problems.Add($"{file} 读取失败（{ex.Message}），本次会话不覆写该文件");
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(text, JsonOpts);
        }
        catch (Exception ex)
        {
            // 数据文件损坏：备份现场后按空数据加载，本次会话禁写该文件保留磁盘现场
            var backup = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            try
            {
                File.Copy(path, backup, overwrite: true);
            }
            catch (IOException)
            {
                backup = "（备份失败）";
            }
            _noWriteFiles.Add(file);
            _problems.Add($"{file} 损坏（已备份为 {backup}）：{ex.Message}");
            return null;
        }
    }

    private void WriteAtomic(string file, object payload)
    {
        if (_noWriteFiles.Contains(file))
        {
            // 损坏/读取失败的文件本次会话不覆写，保护磁盘现场（.corrupt 备份已生成，待用户处理）
            return;
        }
        var path = Path.Combine(_dir, file);
        var temp = path + ".tmp";
        Directory.CreateDirectory(_dir);
        File.WriteAllText(temp, JsonSerializer.Serialize(payload, JsonOpts));
        // 同目录内 Move+overwrite 为原子替换，断电/强杀不会留下半写文件
        File.Move(temp, path, overwrite: true);
    }
}
