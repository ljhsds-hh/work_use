using System.Text.RegularExpressions;

namespace CodeMemo.Services;

/// <summary>
/// 命令占位符（可替换参数）解析与填充（纯函数，可单测）。
/// 约定：占位符统一写成成对英文尖括号 &lt;xxx&gt;（种子数据测试强制校验成对）。
/// 填充时只替换「填了值」的占位符，留空的原样保留 —— 复制出去的仍是一条能看出缺什么的命令。
/// </summary>
public static class CommandPlaceholders
{
    /// <summary>匹配 &lt;xxx&gt;；不允许跨行、不允许嵌套。</summary>
    private static readonly Regex Token = new("<[^<>\r\n]+>", RegexOptions.Compiled);

    /// <summary>按出现顺序提取去重后的占位符名（不含尖括号，已 Trim）。</summary>
    public static IReadOnlyList<string> Extract(string? command)
    {
        if (string.IsNullOrEmpty(command))
        {
            return [];
        }

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Token.Matches(command))
        {
            var name = Inner(match.Value);
            if (name.Length > 0 && seen.Add(name))
            {
                names.Add(name);
            }
        }
        return names;
    }

    public static bool HasPlaceholder(string? command) => Extract(command).Count > 0;

    /// <summary>把填了值的占位符替换进命令；值为空白的占位符保持原样。</summary>
    public static string Fill(string? command, IReadOnlyDictionary<string, string>? values)
    {
        if (string.IsNullOrEmpty(command))
        {
            return "";
        }

        return Token.Replace(command, match =>
        {
            var name = Inner(match.Value);
            if (values is not null && values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
            return match.Value;
        });
    }

    private static string Inner(string token) => token[1..^1].Trim();
}
