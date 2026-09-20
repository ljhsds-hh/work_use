using Microsoft.Win32;
using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Logging;
using SpaceMaid.Core.Safety;

namespace SpaceMaid.Core.Platform;

/// <summary>
/// <see cref="IInstalledProgramIndex"/> 的生产实现：读 Windows 卸载注册表项，判断"这个目录还被引用吗"。
///
/// 读取范围（需求 2.4 的"注册表卸载项"）：
/// <list type="bullet">
/// <item><c>HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall</c></item>
/// <item><c>HKLM\SOFTWARE\WOW6432Node\...\Uninstall</c>（32 位程序）</item>
/// <item><c>HKCU\SOFTWARE\...\Uninstall</c> 与对应的 <c>WOW6432Node</c> 键</item>
/// </list>
/// 每个子键只取三个真正表示"路径"的值：<c>InstallLocation</c>、<c>DisplayIcon</c>、<c>UninstallString</c>。
///
/// <para>
/// **判定方向永远是"宁可误判为被引用"**：只要有一条记录路径 P 与候选目录 D 满足
/// "D == P / D 是 P 的祖先 / P 是 D 的祖先"，就认为被引用（不列出）。
/// 读注册表失败（权限不足、键损坏、非 Windows 主机）时**返回 true**并把原因写进日志——
/// 也就是"注册表读不出来 → 这一项一个目录都不列"。这与需求"判定不确定的一律不列出"一致。
/// </para>
///
/// <para>
/// **只读**：本类只调用 <c>OpenSubKey</c> / <c>GetSubKeyNames</c> / <c>GetValue</c>，不做任何写入或删除。
/// 本类也不接受任何"跳过判定"的开关。
/// </para>
/// </summary>
public sealed class RegistryInstalledProgramIndex : IInstalledProgramIndex
{
    /// <summary>卸载项所在的两条相对键路径（主干 + Wow6432Node 32 位视图）。</summary>
    private static readonly string[] UninstallSubKeyPaths =
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    };

    /// <summary>真正表示"程序装在哪/从哪卸载/图标在哪"的三个值名。</summary>
    private static readonly string[] PathValueNames =
    {
        "InstallLocation",
        "DisplayIcon",
        "UninstallString"
    };

    /// <summary>用来把"未加引号的命令行"截断到可执行文件为止的扩展名。</summary>
    private static readonly string[] ExecutableExtensions =
    {
        ".exe", ".dll", ".com", ".bat", ".cmd", ".msi"
    };

    private readonly ILogSink _log;

    public RegistryInstalledProgramIndex(ILogSink? log = null) => _log = log ?? SilentLogSink.Instance;

    /// <inheritdoc />
    public bool IsReferenced(string directoryPath)
    {
        try
        {
            if (!TryNormalizeDirectory(directoryPath, out var candidate))
            {
                // 候选目录本身确定不了 -> 无从判断 -> 保守视为"被引用"
                _log.Warn($"卸载残留判定：候选目录无法规范化（{directoryPath ?? "<null>"}），按“被引用”处理");
                return true;
            }

            foreach (var recordValue in ReadPathValueTexts())
            {
                if (IsPathReferencedBy(recordValue, candidate))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            // 权限不足 / 键损坏 / 平台不支持：一律按"被引用"处理并记日志，绝不因为异常而放松判定。
            _log.Warn($"卸载残留判定：读取卸载注册表项失败，按“被引用”处理（保守，本项将不列出任何目录）：{ex.Message}");
            return true;
        }
    }

    /// <summary>
    /// 纯判定函数（**无 IO**，便于穷尽单测）：记录里的路径文本与候选目录是否存在"同一个 / 互为祖先"的关系。
    ///
    /// <paramref name="recordPath"/> 可以是注册表里存的原样字符串（带引号、带命令行参数、
    /// 形如 <c>"C:\Program Files\App\app.exe",0</c>），本函数会先抽出其中的路径文本。
    /// 记录文本里抽不出任何"看起来像路径"的东西 → 返回 false（这条记录不构成引用）。
    /// 候选目录侧无法规范化 → 返回 true（保守）。
    /// </summary>
    public static bool IsPathReferencedBy(string? recordPath, string? candidateDirectory)
    {
        if (!TryNormalizeDirectory(candidateDirectory, out var candidate))
        {
            return true; // 候选不确定 -> 保守视为被引用
        }

        foreach (var extracted in ExtractPathTexts(recordPath))
        {
            if (!TryNormalizeDirectory(extracted, out var record))
            {
                continue;
            }

            if (PathNormalizer.IsSameOrUnder(candidate, record)
                || PathNormalizer.IsSameOrUnder(record, candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>枚举全部卸载项的三个路径值（原样文本，未解析）。读不到就抛，由调用点统一转成"保守 true"。</summary>
    private IEnumerable<string> ReadPathValueTexts()
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            if (baseKey is null)
            {
                continue;
            }

            foreach (var subKeyPath in UninstallSubKeyPaths)
            {
                using var uninstallKey = baseKey.OpenSubKey(subKeyPath);
                if (uninstallKey is null)
                {
                    continue; // 键不存在（例如没有 HKCU 的卸载项）不算失败
                }

                foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                {
                    using var entry = uninstallKey.OpenSubKey(subKeyName);
                    if (entry is null)
                    {
                        continue;
                    }

                    foreach (var valueName in PathValueNames)
                    {
                        if (entry.GetValue(valueName) is string text && !string.IsNullOrWhiteSpace(text))
                        {
                            yield return text;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 从一段注册表值文本里抽出"可能指向文件/目录"的路径文本。
    /// 抽取得**宽**：多抽一条只会让判定更保守（更可能被判为"被引用"→ 不列出），不会造成误删。
    /// </summary>
    private static IReadOnlyList<string> ExtractPathTexts(string? raw)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return results;
        }

        var text = Environment.ExpandEnvironmentVariables(raw.Trim());

        // ① 引号里的内容最可靠：'"C:\Program Files\App\app.exe",0' / '"C:\...\unins000.exe" /S'
        var index = 0;
        while (index < text.Length)
        {
            var start = text.IndexOf('"', index);
            if (start < 0)
            {
                break;
            }

            var end = text.IndexOf('"', start + 1);
            if (end < 0)
            {
                break;
            }

            if (end > start + 1)
            {
                results.Add(text.Substring(start + 1, end - start - 1));
            }

            index = end + 1;
        }

        // ② 整段文本本身也当一条候选（覆盖"没引号的 InstallLocation = C:\Program Files\App"这类写法）
        results.Add(TrimTrailingArguments(text));

        return results;
    }

    /// <summary>
    /// 去掉命令行参数：截到第一个可执行扩展名结尾；没有任何扩展名时保留整串
    /// （<c>C:\Program Files\App</c> 里的空格不能当参数分隔符，否则会截成 <c>C:\Program</c> 而漏判）。
    /// </summary>
    private static string TrimTrailingArguments(string text)
    {
        foreach (var extension in ExecutableExtensions)
        {
            var position = text.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
            if (position >= 0)
            {
                return text.Substring(0, position + extension.Length);
            }
        }

        return text;
    }

    /// <summary>把路径文本规范化成可比较的绝对路径；失败（空/含变量/通配/相对路径/非法字符）返回 false。</summary>
    private static bool TryNormalizeDirectory(string? raw, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = raw.Trim();
        if (text.Contains('%') || text.IndexOfAny(new[] { '*', '?' }) >= 0 || !Path.IsPathRooted(text))
        {
            return false;
        }

        try
        {
            normalized = PathNormalizer.TrimTrailingSeparator(Path.GetFullPath(text));
        }
        catch (Exception)
        {
            return false;
        }

        return !string.IsNullOrEmpty(normalized);
    }
}
