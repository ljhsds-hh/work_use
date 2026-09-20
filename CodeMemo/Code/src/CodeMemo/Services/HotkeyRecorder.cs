using System.Windows.Input;

namespace CodeMemo.Services;

/// <summary>
/// 键盘按键 → 热键手势文本（界面「录制」用）。
/// 单独一层是因为 <see cref="HotkeyGesture"/> 要保持不依赖 WPF；
/// 这里只负责把 Key + ModifierKeys 拼成文本，合法与否最终仍由 <see cref="HotkeyGesture.TryParse"/> 判定。
/// </summary>
public static class HotkeyRecorder
{
    /// <summary>
    /// 把一次按键转成手势文本；按下的只是修饰键、没有修饰键、或主键不被支持时返回 null。
    /// 注意 WPF 里 Alt 组合键会以 <see cref="Key.System"/> 上报，调用方要先用 SystemKey 还原真实主键。
    /// </summary>
    public static string? FromKeyboard(Key key, ModifierKeys modifiers)
    {
        if (IsModifierOnly(key))
        {
            return null;
        }

        var keyText = KeyText(key);
        if (keyText is null)
        {
            return null;
        }

        var parts = ModifierParts(modifiers);
        if (parts.Count == 0)
        {
            return null;
        }

        parts.Add(keyText);
        return string.Join('+', parts);
    }

    /// <summary>按 Win32 习惯固定 Ctrl → Alt → Shift → Win 顺序，和 HotkeyGesture.Format 一致。</summary>
    private static List<string> ModifierParts(ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }
        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }
        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }
        return parts;
    }

    private static bool IsModifierOnly(Key key)
        => key is Key.None or Key.System
            or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin;

    private static string? KeyText(Key key) => key switch
    {
        >= Key.A and <= Key.Z => ((char)('A' + (key - Key.A))).ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (key - Key.NumPad0))).ToString(),
        >= Key.F1 and <= Key.F24 => $"F{key - Key.F1 + 1}",
        Key.Space => "Space",
        _ => null,
    };
}
