using System.Windows.Media;

namespace QuietRemind.Services;

/// <summary>
/// 一个可选主题色。<see cref="Base"/> 是浅色模式下的主色，深色模式由 <see cref="ThemePalette"/>
/// 按同一色相提亮得到，保证两种模式的气质一致。
/// </summary>
/// <param name="Key">持久化标识（写入 settings.json，勿随意改名）。</param>
/// <param name="Name">界面显示名。</param>
/// <param name="Base">浅色模式基准色。</param>
public sealed record ThemeAccentInfo(string Key, string Name, Color Base);

/// <summary>用户可见的主题色清单。新增配色只需在此追加一项，UI 与配色会自动跟随。</summary>
public static class ThemeAccentCatalog
{
    public const string Pine = "pine";
    public const string Indigo = "indigo";
    public const string Clay = "clay";
    public const string Plum = "plum";
    public const string Graphite = "graphite";
    public const string Berry = "berry";

    public const string DefaultKey = Pine;

    public static IReadOnlyList<ThemeAccentInfo> All { get; } =
    [
        new(Pine, "松绿", Color.FromRgb(0x1D, 0x5B, 0x4E)),
        new(Indigo, "墨蓝", Color.FromRgb(0x2A, 0x4E, 0x7E)),
        new(Clay, "赭陶", Color.FromRgb(0xA2, 0x4A, 0x2E)),
        new(Plum, "紫檀", Color.FromRgb(0x6B, 0x4E, 0x8E)),
        new(Graphite, "石墨", Color.FromRgb(0x3D, 0x4A, 0x52)),
        new(Berry, "莓果", Color.FromRgb(0xA8, 0x3E, 0x5B)),
    ];

    /// <summary>把外部（磁盘/导入）来的值归一到已知标识；未知值回退默认色，避免启动即崩。</summary>
    public static string Normalize(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return DefaultKey;
        }
        foreach (var item in All)
        {
            if (string.Equals(item.Key, key.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return item.Key;
            }
        }
        return DefaultKey;
    }

    public static ThemeAccentInfo Resolve(string? key)
    {
        var normalized = Normalize(key);
        foreach (var item in All)
        {
            if (string.Equals(item.Key, normalized, StringComparison.Ordinal))
            {
                return item;
            }
        }
        return All[0];
    }
}
