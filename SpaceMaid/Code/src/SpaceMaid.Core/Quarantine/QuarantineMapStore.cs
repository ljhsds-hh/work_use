using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpaceMaid.Core.Models;

namespace SpaceMaid.Core.Quarantine;

/// <summary>
/// 映射表（map.json）读写。
///
/// 为什么直接使用 System.IO 而不是 IFileSystem：
/// 映射表是隔离区的**账本**，它的读写必须与文件搬运在同一处实现，才能保证"先记账再搬运"的两阶段顺序；
/// 把它抽象出去只会让"账本 vs 文件系统"两份状态更难对齐。IO 只在 <see cref="QuarantineStore"/> 与这里出现（不变量 I-1）。
/// </summary>
public static class QuarantineMapStore
{
    /// <summary>映射表文件名。</summary>
    public const string FileName = "map.json";

    /// <summary>统一序列化入口：中文不转义（\uXXXX 会让用户无法审阅隔离区目录）。</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>
    /// 账本条目里的存放路径是否安全。
    ///
    /// **为什么必须校验**：账本位于用户可写目录，且被刻意设计成"可读可改"（JSON 不转义中文便于审阅）。
    /// 如果直接拿 <c>StoredAs</c> 去拼路径，就等于把"任意文件删除/移动"的能力交给了任何能写这个文件的进程：
    /// 一条 <c>storedAs = "..\\..\\..\\Windows\\System32\\drivers\\etc\\hosts"</c> 的假账目，
    /// 会在用户下次以管理员身份启动（或点"清空隔离区"）时被以管理员权限删掉。
    /// 因此这里只接受"**相对、无 .. 、不以分隔符或盘符开头**"的存放路径。
    /// </summary>
    public static bool IsSafeStoredAs(string? storedAs)
    {
        if (string.IsNullOrWhiteSpace(storedAs))
        {
            return false;
        }

        var candidate = storedAs.Trim();

        if (candidate.StartsWith(Path.DirectorySeparatorChar)
            || candidate.StartsWith(Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        if (Path.IsPathRooted(candidate))
        {
            return false;
        }

        // 盘符形式（C:...）也要挡住：Path.IsPathRooted("C:x") 为 true，但保险起见再查一次冒号
        if (candidate.Length >= 2 && candidate[1] == ':')
        {
            return false;
        }

        var segments = candidate.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        return segments.Length > 0 && !segments.Any(segment => segment == "..");
    }

    /// <summary>
    /// 读取映射表；文件缺失或损坏时返回 null（由调用方决定如何报告，不抛异常打断程序）。
    /// **账本内容视为不可信输入**：任何条目的存放路径不安全、或原始路径为空，整份账本判为不可用（失败关闭）——
    /// 宁可这个批次不释放/不还原，也不能按假账目去删一个不该删的文件。
    /// </summary>
    public static QuarantineMap? TryRead(string batchDirectory)
    {
        var path = Path.Combine(batchDirectory, FileName);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            var map = JsonSerializer.Deserialize<QuarantineMap>(json, Options);

            if (map is null)
            {
                return null;
            }

            foreach (var entry in map.Entries)
            {
                if (!IsSafeStoredAs(entry.StoredAs) || string.IsNullOrWhiteSpace(entry.OriginalPath))
                {
                    return null;
                }
            }

            return map;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>原子写入映射表：先写 .tmp 再替换，避免断电留下半截 JSON。</summary>
    public static bool TryWrite(string batchDirectory, QuarantineMap map, out string error)
    {
        error = string.Empty;
        try
        {
            Directory.CreateDirectory(batchDirectory);
            var target = Path.Combine(batchDirectory, FileName);
            var temp = target + ".tmp";

            File.WriteAllText(temp, JsonSerializer.Serialize(map, Options));
            File.Move(temp, target, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            error = $"写入映射表失败：{ex.Message}";
            return false;
        }
    }
}
