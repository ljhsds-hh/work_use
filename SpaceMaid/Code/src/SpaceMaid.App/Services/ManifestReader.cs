using System.IO;
using SpaceMaid.Core.Models;

namespace SpaceMaid.App.Services;

/// <summary>
/// 清单目录相关的小工具。
///
/// **csv 的解析一律走内核 <see cref="SpaceMaid.Core.Reporting.ManifestReader"/>**：
/// 界面层曾经自己又写了一份解析器，两份实现对引号/转义的宽容度还不完全一致——
/// "同一份清单被两个解析器读出不同结果"是最不该出现的事，所以这里只保留内核做不到的那一件事（列目录）。
/// </summary>
public static class ManifestFiles
{
    /// <summary>读取清单目录；清单缺失、损坏或**被改过**时返回 null（由调用方给出人话提示）。</summary>
    public static ManifestRowIndex? Read(string directory) =>
        Core.Reporting.ManifestReader.TryRead(directory, out _);

    /// <summary>本工具导出的清单目录名形如 yyyyMMdd-HHmmss-fff，倒序即"最近的清单在最前"。</summary>
    public static IReadOnlyList<string> ListManifestDirectories(string reportRoot)
    {
        if (!Directory.Exists(reportRoot))
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory.GetDirectories(reportRoot)
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }
}
