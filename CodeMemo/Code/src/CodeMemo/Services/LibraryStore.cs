using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CodeMemo.Models;

namespace CodeMemo.Services;

/// <summary>
/// 命令库本地 JSON 持久化：目录 %AppData%\CodeMemo\，文件 commands.json。
/// 原子写（临时文件 + 替换），任何变更实时落盘；
/// 文件损坏时按空库加载并返回问题描述，避免损坏数据被覆盖丢失。
/// </summary>
public sealed class LibraryStore
{
    /// <summary>当前程序写出的数据结构版本（新增字段时 +1，并在 Migrate 里补迁移）。</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>导入备份保留份数（按时间保留最近 N 份）。</summary>
    public const int MaxBackups = 5;

    /// <summary>
    /// 统一序列化配置：缩进 + 不转义中文与 &lt; &gt; 等字符。
    /// 默认编码器会把中文写成 \uXXXX、把 &lt; 写成 \u003C，导致数据文件看不懂、没法手改、没法进 diff。
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _dir;
    private readonly string _filePath;

    public LibraryStore(string directory)
    {
        _dir = directory;
        _filePath = Path.Combine(_dir, "commands.json");
        Directory.CreateDirectory(_dir);
    }

    /// <summary>用户数据文件完整路径（导入导出提示用）。</summary>
    public string FilePath => _filePath;

    /// <summary>数据目录（「打开数据目录」用）。</summary>
    public string DirectoryPath => _dir;

    /// <summary>数据文件是否已存在（判断首次启动，用于种子导入）。</summary>
    public bool Exists => File.Exists(_filePath);

    /// <summary>加载命令库；文件缺失或损坏时返回空库，损坏 / 版本过高时给出 Problems 描述。</summary>
    public (CommandLibraryData Data, List<string> Problems) Load()
    {
        if (!Exists)
        {
            return (new CommandLibraryData(), []);
        }

        try
        {
            using var fs = File.OpenRead(_filePath);
            var data = JsonSerializer.Deserialize<CommandLibraryData>(fs, JsonOptions) ?? new CommandLibraryData();
            var problems = new List<string>();

            if (data.SchemaVersion > CurrentSchemaVersion)
            {
                // 更高版本写的文件：不擅自改写，只提示，尽量把能读的读出来
                problems.Add(
                    $"命令库文件版本为 v{data.SchemaVersion}，高于当前程序支持的 v{CurrentSchemaVersion}，" +
                    $"可能是更高版本的 CodeMemo 写入的。已尽力加载，建议先导出备份再升级程序（原文件保留在 {_filePath}）。");
            }
            else if (data.SchemaVersion < CurrentSchemaVersion)
            {
                Migrate(data, problems);
            }

            return (data, problems);
        }
        catch (Exception ex)
        {
            return (new CommandLibraryData(),
            [
                $"命令库文件损坏，已按空库启动（原文件保留在 {_filePath}，未被覆盖）：{ex.Message}",
            ]);
        }
    }

    /// <summary>旧版本数据升级到 CurrentSchemaVersion（新增字段时在此补齐默认值）。</summary>
    private static void Migrate(CommandLibraryData data, List<string> problems)
    {
        // v1 是首个正式版本，暂无字段级迁移；此处只做版本补齐，避免新字段默认值不一致。
        if (data.SchemaVersion < 1)
        {
            problems.Add($"命令库文件缺少版本号（原 v{data.SchemaVersion}），已按 v{CurrentSchemaVersion} 加载。");
        }
        data.SchemaVersion = CurrentSchemaVersion;
    }

    public void Save(CommandLibraryData data)
    {
        data.SchemaVersion = CurrentSchemaVersion;

        // 临时文件 + 替换，保证任意时刻磁盘上都有一份完整数据
        var tmpPath = _filePath + ".tmp";
        using (var fs = File.Create(tmpPath))
        {
            JsonSerializer.Serialize(fs, data, JsonOptions);
        }
        File.Move(tmpPath, _filePath, overwrite: true);
    }

    /// <summary>
    /// 导入前备份现有数据为带时间戳的 commands.yyyyMMdd-HHmmss.bak，
    /// 并按时间保留最近 MaxBackups 份（旧实现固定覆盖同一个 .bak，连续导入两次就没有退路了）。
    /// 返回备份文件路径；原先没有数据文件时返回 null。
    /// </summary>
    public string? BackupBeforeImport()
    {
        if (!Exists)
        {
            return null;
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var backupPath = Path.Combine(_dir, $"commands.{stamp}.bak");
        for (var i = 1; File.Exists(backupPath); i++)
        {
            backupPath = Path.Combine(_dir, $"commands.{stamp}-{i}.bak");
        }

        File.Copy(_filePath, backupPath, overwrite: true);
        PruneBackups();
        return backupPath;
    }

    private void PruneBackups()
    {
        // 备份名里带 yyyyMMdd-HHmmss，按名称倒序即按时间倒序（File.Copy 会保留源文件时间，不靠 LastWriteTime 排序）
        var stale = Directory.GetFiles(_dir, "commands.*.bak")
            .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
            .Skip(MaxBackups);

        foreach (var file in stale)
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // 备份清理失败不影响导入流程
            }
        }
    }
}

/// <summary>
/// 内置命令库种子（Assets/seed/commands.json 嵌入资源）：
/// 首次启动（用户数据文件不存在）时整体导入为用户数据，此后以用户文件为准，
/// 用户在 UI 上的增删改不会被后续版本升级覆盖。
/// </summary>
public static class SeedLibrary
{
    public static CommandLibraryData Load()
    {
        var json = ReadSeedJson()
            ?? throw new InvalidOperationException("内置命令库种子资源缺失（CodeMemo.Assets.seed.commands.json）");
        return JsonSerializer.Deserialize<CommandLibraryData>(json, LibraryStore.JsonOptions) ?? new CommandLibraryData();
    }

    internal static string? ReadSeedJson()
    {
        var asm = typeof(SeedLibrary).Assembly;
        // 嵌入资源全名随编译环境可能带不同默认命名空间，按后缀匹配兜底
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("Assets.seed.commands.json", StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return null;
        }
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
