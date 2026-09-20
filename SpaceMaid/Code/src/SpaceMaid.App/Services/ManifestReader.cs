using System.Globalization;
using System.IO;
using System.Text;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Reporting;

namespace SpaceMaid.App.Services;

/// <summary>
/// 读回已导出的 `清单.csv`，得到复核用的清单索引（需求 3.9-4）。
///
/// 为什么需要它：复核必须针对**用户审阅过的那份清单**，而不是内存里的临时对象——
/// 两者在"用户导出后又改了勾选"的场景下会不一致。csv 是内核写出的穷尽清单，
/// 因此复核的比对基准就是 csv 本身。
/// 本类只解析，不写盘、不删除任何东西。
/// </summary>
public static class ManifestReader
{
    public static ManifestRowIndex Read(string directory)
    {
        var csvPath = Path.Combine(directory, "清单.csv");
        if (!File.Exists(csvPath))
        {
            return new ManifestRowIndex(directory, Array.Empty<ManifestRow>());
        }

        var rows = new List<ManifestRow>();
        foreach (var line in File.ReadAllLines(csvPath, Encoding.UTF8).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var cells = SplitCsv(line);
            if (cells.Count < 8 || cells[0].Equals("分级", StringComparison.Ordinal))
            {
                continue; // 表头或残行：跳过而不是猜
            }

            rows.Add(new ManifestRow(
                cells[0],
                cells[1],
                cells[2],
                cells[3],
                ParseLong(cells[4]),
                ParseTime(cells[5]),
                cells[6],
                cells[7] is "是" or "true" or "True",
                cells.Count > 8 ? cells[8] : string.Empty));
        }

        return new ManifestRowIndex(directory, rows);
    }

    /// <summary>本工具导出的清单目录名形如 yyyyMMdd-HHmmss-fff，倒序即"最近的清单在最前"。</summary>
    public static IReadOnlyList<string> ListManifestDirectories(string reportRoot)
    {
        if (!Directory.Exists(reportRoot))
        {
            return Array.Empty<string>();
        }

        return Directory.GetDirectories(reportRoot)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
    }

    private static long ParseLong(string text)
        => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static DateTimeOffset ParseTime(string text)
        => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value
            : DateTimeOffset.MinValue;

    private static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var builder = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        builder.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    builder.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                cells.Add(builder.ToString());
                builder.Clear();
            }
            else
            {
                builder.Append(c);
            }
        }

        cells.Add(builder.ToString());
        return cells;
    }
}
