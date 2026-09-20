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

    /// <summary>读取映射表；文件缺失或损坏时返回 null（由调用方决定如何报告，不抛异常打断程序）。</summary>
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
            return JsonSerializer.Deserialize<QuarantineMap>(json, Options);
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
