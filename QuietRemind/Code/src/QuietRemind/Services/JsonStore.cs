using System.Globalization;
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
/// 原子写（唯一临时文件 + 替换），任何状态变更实时落盘。
/// 损坏隔离：单个文件损坏只影响该域。解析失败时把现场移动到 .corrupt-时间戳 备份，
/// 原路径重建为空数据后继续可写；读取失败（瞬时占用/权限）则保留原文件、本次会话禁写该域，
/// 后续写入抛出明确异常由上层提示，避免“看似保存成功、重启全部丢失”。
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
    private readonly List<string> _problems = [];

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
        _problems.Clear();
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

    public void SaveTasks(IReadOnlyList<ReminderTask> tasks) => WriteAtomic("tasks.json", tasks);

    public void SaveOccurrences(IReadOnlyList<Occurrence> occurrences) => WriteAtomic("occurrences.json", occurrences);

    public void SaveSettings(AppSettings settings) => WriteAtomic("settings.json", settings);

    public void WriteExitMarker(string? sessionId = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
        var time = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        // 原子写：避免退出瞬间断电/强杀留下半个标记，被下一次守护误判为“无标记”而重新拉起
        var temp = $"{_markerPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, $"{time}|{sessionId}");
            File.Move(temp, _markerPath, overwrite: true);
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
                // 临时文件清理失败不掩盖主流程异常
            }
        }
    }

    public bool HasExitMarker() => File.Exists(_markerPath);

    /// <summary>退出标记内容：写入时间（诊断用）+ 写入时的登录会话 LUID。</summary>
    public sealed record ExitMarkerInfo(DateTime? Time, string? SessionId);

    public ExitMarkerInfo ReadExitMarker()
    {
        try
        {
            if (!File.Exists(_markerPath))
            {
                return new ExitMarkerInfo(null, null);
            }
            var parts = File.ReadAllText(_markerPath).Trim().Split('|', 2);
            DateTime? time = null;
            if (DateTime.TryParseExact(parts[0], "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var exact))
            {
                time = exact;
            }
            else if (DateTime.TryParse(parts[0], CultureInfo.CurrentCulture, DateTimeStyles.None, out var loose))
            {
                time = loose;
            }
            var session = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])
                ? parts[1].Trim()
                : null;
            return new ExitMarkerInfo(time, session);
        }
        catch (Exception)
        {
            return new ExitMarkerInfo(null, null);
        }
    }

    /// <summary>读取退出标记时间（兼容旧格式）；文件缺失或时间不可解析时返回 null。</summary>
    public DateTime? ReadExitMarkerTime() => ReadExitMarker().Time;

    public void ClearExitMarker()
    {
        try
        {
            if (File.Exists(_markerPath))
            {
                File.Delete(_markerPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
            text = ReadWithRetry(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 读取失败（瞬时占用/权限受限/IO 抖动）不代表内容损坏：保留原文件，本次会话禁写该域，
            // 避免把可能完好的数据移走并以空数据覆盖。重启后文件可读时自动恢复。
            _noWriteFiles.Add(file);
            _problems.Add($"{file} 读取失败（{ex.Message}），本次会话不覆写该文件，请重启程序后重试");
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(text, JsonOpts);
        }
        catch (Exception ex)
        {
            // 数据文件损坏：把现场移动到备份路径，原路径释放后按空数据重建，避免整会话静默丢写
            var backup = TryMoveAside(path);
            if (backup is null)
            {
                _noWriteFiles.Add(file);
                _problems.Add($"{file} 损坏（已尝试备份但无法移走原文件）：{ex.Message}；本次会话不覆写该文件");
            }
            else
            {
                _problems.Add($"{file} 损坏，已备份为 {backup}，本次启动将重建该数据域：{ex.Message}");
            }
            return null;
        }
    }

    private static string ReadWithRetry(string path)
    {
        IOException? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException ex)
            {
                last = ex;
                if (attempt < 2)
                {
                    Thread.Sleep(50);
                }
            }
        }
        throw last!;
    }

    /// <summary>
    /// 把无法解析的文件移动到 .corrupt-时间戳 备份路径。
    /// 返回备份路径表示原路径已释放；返回 null 表示无法移走（文件仍被占用），调用方应禁写。
    /// </summary>
    private static string? TryMoveAside(string path)
    {
        // 毫秒级时间戳：同一秒内重复启动/多次损坏不会互相覆盖备份现场
        var backup = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmssfff}";
        try
        {
            File.Move(path, backup, overwrite: true);
            return backup;
        }
        catch (Exception)
        {
            // 移动失败时尽量再复制一份现场；复制成功也不能释放原路径，故仍返回 null
            try
            {
                File.Copy(path, backup, overwrite: true);
            }
            catch (Exception)
            {
                // 备份失败：由调用方记录问题
            }
            return null;
        }
    }

    private void WriteAtomic(string file, object payload)
    {
        if (_noWriteFiles.Contains(file))
        {
            // 读取失败且无法移走原文件：拒绝静默丢弃，抛出明确异常交给上层提示/重试
            throw new IOException($"{file} 在本次会话中不可写（加载失败且文件无法备份），请重启程序后重试");
        }
        var path = Path.Combine(_dir, file);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        Directory.CreateDirectory(_dir);
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(payload, JsonOpts));
            // 同目录内 Move+overwrite 为原子替换，断电/强杀不会留下半写文件
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
                // 临时文件清理失败不掩盖主流程异常
            }
        }
    }
}
