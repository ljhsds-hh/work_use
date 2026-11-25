namespace QuietRemind.Models;

/// <summary>界面明暗模式。持久化到 settings.json（字符串枚举，便于人工编辑与向后兼容）。</summary>
public enum ThemeMode
{
    Light,
    Dark,
}
