using System.Text.Json.Serialization;

namespace CodeMemo.Models;

/// <summary>
/// 应用偏好（settings.json）：外观（皮肤 / 字号）与常驻行为（托盘 / 全局热键）。
/// 与命令库分开存，避免用户偏好污染命令数据，升级命令库结构时也不用动它。
/// </summary>
public sealed class AppSettings
{
    /// <summary>0 表示未标注版本（手写文件），写出时统一写成 <see cref="Services.SettingsStore.CurrentSchemaVersion"/>。</summary>
    [JsonPropertyOrder(-1)]
    public int SchemaVersion { get; set; }

    /// <summary>皮肤：Default（明亮）/ Dark（暗色）/ System（跟随系统亮暗），取值见 <see cref="AppSettingValues"/>。</summary>
    public string Theme { get; set; } = AppSettingValues.ThemeDefault;

    /// <summary>字号档位：Small / Normal / Large。</summary>
    public string FontSize { get; set; } = AppSettingValues.FontNormal;

    /// <summary>关闭窗口时收进托盘继续常驻（关托盘图标所在程序只能从托盘菜单退出）。</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>启用全局热键呼出窗口。</summary>
    public bool GlobalHotkeyEnabled { get; set; } = true;

    /// <summary>全局热键手势，写法如 Ctrl+Alt+C（解析见 <see cref="Services.HotkeyGesture"/>）。</summary>
    public string GlobalHotkey { get; set; } = AppSettingValues.DefaultHotkey;
}

/// <summary>下拉框选项：值 + 显示文案。</summary>
public sealed record SettingOption(string Value, string Label);

/// <summary>应用偏好的取值与归一化（纯逻辑，可单测）。</summary>
public static class AppSettingValues
{
    /// <summary>HandyControl 的 SkinType.Default 就是明亮皮肤（枚举只有 Default / Dark / Violet）。</summary>
    public const string ThemeDefault = "Default";

    public const string ThemeDark = "Dark";

    /// <summary>跟随系统亮暗：应用时按系统当前设置解析成 Default / Dark，系统切换时自动跟随。</summary>
    public const string ThemeSystem = "System";

    public const string FontSmall = "Small";
    public const string FontNormal = "Normal";
    public const string FontLarge = "Large";

    public const string DefaultHotkey = "Ctrl+Alt+C";

    public static IReadOnlyList<SettingOption> ThemeOptions { get; } =
    [
        new(ThemeDefault, "明亮"),
        new(ThemeDark, "暗色"),
        new(ThemeSystem, "跟随系统"),
    ];

    public static IReadOnlyList<SettingOption> FontSizeOptions { get; } =
    [
        new(FontSmall, "小"),
        new(FontNormal, "标准"),
        new(FontLarge, "大"),
    ];

    /// <summary>
    /// 全局热键候选（界面下拉用；想用别的组合可以在界面上「录制」）。
    /// 写法都按 HotkeyGesture 的规范顺序（Ctrl → Alt → Shift → Win），免得选了之后被规范化成另一个样子。
    /// </summary>
    public static IReadOnlyList<SettingOption> HotkeyOptions { get; } =
    [
        new(DefaultHotkey, DefaultHotkey),
        new("Ctrl+Alt+Space", "Ctrl+Alt+Space"),
        new("Ctrl+Shift+C", "Ctrl+Shift+C"),
        new("Alt+Space", "Alt+Space"),
        new("Ctrl+Shift+Space", "Ctrl+Shift+Space"),
    ];

    /// <summary>未知 / 空值一律归到明亮皮肤，避免配置文件被改坏后启动异常。</summary>
    public static string NormalizeTheme(string? theme)
        => theme is ThemeDark or ThemeSystem ? theme : ThemeDefault;

    /// <summary>是否「跟随系统」。</summary>
    public static bool IsSystemTheme(string? theme) => NormalizeTheme(theme) == ThemeSystem;

    public static string NormalizeFontSize(string? fontSize)
        => fontSize is FontSmall or FontLarge ? fontSize : FontNormal;

    public static SettingOption OptionOf(IReadOnlyList<SettingOption> options, string value)
        => options.FirstOrDefault(o => o.Value == value) ?? options[0];
}
