namespace CodeMemo.Services;

/// <summary>
/// 全局热键手势的解析与规范化（纯函数，可单测）。
/// 写法：修饰键 + 主键，例如 <c>Ctrl+Alt+C</c>、<c>Shift+F5</c>、<c>Alt+Space</c>；
/// 修饰键至少一个，主键支持 A-Z、0-9、F1-F24、Space。Modifiers / VirtualKey 直接喂给 Win32 RegisterHotKey。
/// 键盘按键 → 手势文本的转换在 <see cref="HotkeyRecorder"/>（那一层才知道 WPF 的 Key 枚举）。
/// </summary>
public sealed record HotkeyGesture(uint Modifiers, uint VirtualKey, string Text)
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    /// <summary>VK_SPACE。</summary>
    public const uint VkSpace = 0x20;

    /// <summary>解析手势；不合法（缺修饰键、主键不认识、重复修饰键等）返回 false。</summary>
    public static bool TryParse(string? text, out HotkeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Any(p => p.Length == 0))
        {
            return false;
        }

        uint modifiers = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var (name, flag) = parts[i].ToLowerInvariant() switch
            {
                "ctrl" or "control" => ("Ctrl", ModControl),
                "alt" => ("Alt", ModAlt),
                "shift" => ("Shift", ModShift),
                "win" or "windows" or "meta" => ("Win", ModWin),
                _ => ("", 0u),
            };

            if (flag == 0 || !seen.Add(name))
            {
                return false;
            }
            modifiers |= flag;
        }

        if (!TryParseKey(parts[^1], out var virtualKey, out var keyText))
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, virtualKey, Format(modifiers, keyText));
        return true;
    }

    /// <summary>规范化的显示文本（固定 Ctrl+Alt+Shift+Win 顺序）。</summary>
    public static string Format(uint modifiers, string keyText)
    {
        var parts = new List<string>();
        if ((modifiers & ModControl) != 0)
        {
            parts.Add("Ctrl");
        }
        if ((modifiers & ModAlt) != 0)
        {
            parts.Add("Alt");
        }
        if ((modifiers & ModShift) != 0)
        {
            parts.Add("Shift");
        }
        if ((modifiers & ModWin) != 0)
        {
            parts.Add("Win");
        }
        parts.Add(keyText);
        return string.Join('+', parts);
    }

    private static bool TryParseKey(string token, out uint virtualKey, out string keyText)
    {
        virtualKey = 0;
        keyText = "";

        if (token.Equals("space", StringComparison.OrdinalIgnoreCase))
        {
            virtualKey = VkSpace;
            keyText = "Space";
            return true;
        }

        if (token.Length == 1)
        {
            var upper = char.ToUpperInvariant(token[0]);
            if (upper is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = upper;
                keyText = upper.ToString();
                return true;
            }
            return false;
        }

        if ((token[0] is 'F' or 'f')
            && int.TryParse(token[1..], out var index)
            && index is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + index - 1);
            keyText = $"F{index}";
            return true;
        }

        return false;
    }
}
