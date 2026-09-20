using System.Globalization;
using System.Text;
using SpaceMaid.Core.Models;

namespace SpaceMaid.Core.Reporting;

/// <summary>
/// 清单读回（复核阶段用）。
///
/// 为什么必须能从 csv 读回：复核发生在**清理之后**，很可能已经是另一个进程（用户关掉工具、
/// 执行完清理、再打开工具点"复核"）。此时内存里的 <c>CleanPlan</c> 早就没了，
/// 唯一能依赖的就是当时落盘的清单——所以清单不只是给人看的报告，它是**可机器复读的凭据**。
/// </summary>
public static class ManifestReader
{
    /// <summary>清单文件名（与 <see cref="ManifestWriter"/> 保持一致）。</summary>
    public const string CsvFileName = "清单.csv";

    /// <summary>
    /// 读取清单目录下的 csv。文件缺失或格式不符时返回 null 并把原因写入 <paramref name="error"/>。
    /// **格式不符一律判错，不猜**：这是机器生成的凭据，读不懂说明它被改过或损坏了。
    /// </summary>
    public static ManifestRowIndex? TryRead(string manifestDirectory, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(manifestDirectory) || !Directory.Exists(manifestDirectory))
        {
            error = $"清单目录不存在：{manifestDirectory}";
            return null;
        }

        var csvPath = Path.Combine(manifestDirectory, CsvFileName);
        if (!File.Exists(csvPath))
        {
            error = $"清单文件不存在：{csvPath}";
            return null;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(csvPath, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            error = $"读取清单失败：{ex.Message}";
            return null;
        }

        if (lines.Length == 0)
        {
            error = "清单文件为空";
            return null;
        }

        var header = ParseLine(lines[0].TrimStart('\uFEFF'));
        if (header.Count != 9)
        {
            error = $"清单表头列数不符（期望 9 列，实际 {header.Count} 列）";
            return null;
        }

        var rows = new List<ManifestRow>();
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var fields = ParseLine(lines[i]);
            if (fields.Count != 9)
            {
                error = $"清单第 {i + 1} 行列数不符（期望 9 列，实际 {fields.Count} 列），清单可能被修改过";
                return null;
            }

            var size = long.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;

            var lastWrite = DateTimeOffset.TryParse(
                fields[5],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedTime)
                ? parsedTime
                : DateTimeOffset.MinValue;

            rows.Add(new ManifestRow(
                fields[0],
                fields[1],
                fields[2],
                fields[3],
                size,
                lastWrite,
                fields[6],
                fields[7] == "是",
                fields[8]));
        }

        return new ManifestRowIndex(manifestDirectory, rows);
    }

    /// <summary>最小 CSV 解析：支持双引号包裹、字段内的逗号与转义的双引号（与写出侧对称）。</summary>
    internal static List<string> ParseLine(string line)
    {
        var fields = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        builder.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    builder.Append(ch);
                }
            }
            else if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == ',')
            {
                fields.Add(builder.ToString());
                builder.Clear();
            }
            else
            {
                builder.Append(ch);
            }
        }

        fields.Add(builder.ToString());
        return fields;
    }
}
