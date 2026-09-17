using System.IO;
using CreateTpl.Helpers;

namespace CreateTpl.Services;

/// <summary>
/// 工程名称输入解析器：负责批量名称的分行解析、去重与合法性校验（需求 3.1 / 3.6）。
/// 纯静态逻辑，不依赖 UI 类型，可完整单元测试。
/// </summary>
public static class InputParser
{
    /// <summary>单个工程名最大长度（预留深层路径余量，避免超出 Windows 路径上限）。</summary>
    public const int MaxNameLength = 64;

    /// <summary>单次批量创建的工程数量上限（防止误粘贴超大批量导致界面假死）。</summary>
    public const int MaxBatchCount = 200;

    /// <summary>解析结果：合法名称列表（已去重、保持输入顺序），以及全部中文错误信息。</summary>
    public sealed record ParseResult(IReadOnlyList<string> Names, IReadOnlyList<string> Errors)
    {
        /// <summary>校验是否全部通过，通过后才允许开始生成。</summary>
        public bool IsValid => Errors.Count == 0;
    }

    /// <summary>解析多行工程名输入：忽略空行、去首尾空格、去重、逐行校验。</summary>
    public static ParseResult Parse(string input)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // Windows 文件系统大小写不敏感，按此口径去重
        var errors = new List<string>();

        var lines = (input ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var name = lines[i].Trim();
            if (name.Length == 0)
            {
                continue; // 忽略空行
            }

            var error = ValidateName(name);
            if (error != null)
            {
                errors.Add($"第 {i + 1} 行 \"{name}\"：{error}");
                continue;
            }

            if (seen.Add(name))
            {
                names.Add(name);
            }
        }

        if (errors.Count == 0 && names.Count == 0)
        {
            errors.Add("请至少输入一个工程名称。");
        }

        if (errors.Count == 0 && names.Count > MaxBatchCount)
        {
            errors.Add($"单次批量创建数量不能超过 {MaxBatchCount} 个，当前 {names.Count} 个。");
        }

        return new ParseResult(names, errors);
    }

    /// <summary>校验单个工程名；非法返回中文原因，合法返回 null。</summary>
    public static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "工程名称不能为空。";
        }

        if (name.Length > MaxNameLength)
        {
            return $"长度超过 {MaxNameLength} 字符上限。";
        }

        if (name is "." or "..")
        {
            return "不能使用相对目录特殊名。";
        }

        if (name.EndsWith('.') || name.EndsWith(' '))
        {
            return "不能以点号或空格结尾（Windows 目录名限制）。";
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return "包含 Windows 不允许的字符（如 \\ / : * ? \" < > | 等）。";
        }

        if (ReservedNames.IsReserved(name))
        {
            return "为 Windows 保留设备名（如 CON、NUL、COM1 等），不可使用。";
        }

        return null;
    }

    /// <summary>校验工程根目录路径；非法返回中文原因，合法返回 null。</summary>
    public static string? ValidateRootPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "工程根目录不能为空。";
        }

        // 需求示例使用 / 分隔（如 D:/Projects/），统一按 Windows 分隔符处理
        var trimmed = path.Trim().Replace('/', '\\');

        if (!Path.IsPathRooted(trimmed))
        {
            return "请输入绝对路径（如 D:\\Projects）。";
        }

        try
        {
            var full = Path.GetFullPath(trimmed);

            // GetFullPath 在 .NET Core 后不再校验非法字符，需显式检查各路径段
            //（跳过首段盘符，如 "D:"；目录段复用文件名非法字符集，覆盖 < > * | " 等及控制字符）
            var invalidChars = Path.GetInvalidFileNameChars();
            var segments = full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            for (var s = 1; s < segments.Length; s++)
            {
                if (segments[s].IndexOfAny(invalidChars) >= 0)
                {
                    return "路径包含 Windows 不允许的字符（如 < > * ? \" | 等）。";
                }
            }

            var driveRoot = Path.GetPathRoot(full);

            if (string.IsNullOrEmpty(driveRoot) || !Directory.Exists(driveRoot))
            {
                return $"路径所属的磁盘分区不存在：{driveRoot}";
            }

            if (full.Length > 240)
            {
                return "路径过长，请缩短根目录路径。";
            }

            return null;
        }
        catch (Exception)
        {
            return "路径格式非法，请检查是否包含非法字符。";
        }
    }
}
