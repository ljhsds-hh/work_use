using System.Windows;
using System.Windows.Media;
using QuietRemind.Models;

namespace QuietRemind.Services;

/// <summary>
/// 主题应用：把 <see cref="ThemePalette"/> 的结果写入 WPF 资源。
/// 界面统一用 DynamicResource 引用颜色（WPF 会冻结 ResourceDictionary 里的画刷，
/// 就地改 Color 对已加载界面无效），因此这里整键替换画刷实例，切换后即时刷新。
/// </summary>
public static class ThemeService
{
    private const string SkinLight = "pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml";
    private const string SkinDark = "pack://application:,,,/HandyControl;component/Themes/SkinDark.xaml";

    public static ThemeValues Apply(AppSettings settings) => Apply(settings.ThemeMode, settings.ThemeAccent);

    /// <summary>
    /// 切换并持久化主题。返回 false 表示设置未变化（避免滑块/重复点击产生无意义写盘）。
    /// 落盘失败由 <see cref="AppServices"/> 统一提示，界面保持已应用的主题。
    /// </summary>
    public static bool ApplyAndPersist(AppServices services, ThemeMode mode, string? accentKey)
    {
        var settings = services.Data.Settings;
        var normalized = ThemeAccentCatalog.Normalize(accentKey);
        if (settings.ThemeMode == mode
            && string.Equals(settings.ThemeAccent, normalized, StringComparison.Ordinal))
        {
            return false;
        }

        settings.ThemeMode = mode;
        settings.ThemeAccent = normalized;
        Apply(mode, normalized);
        services.PersistSettings();
        return true;
    }

    /// <summary>上一次已写入的画刷，用于跳过未变化的键（换主题色时中性色不重复通知）。</summary>
    private static Dictionary<string, Color> _applied = new(StringComparer.Ordinal);
    private static ThemeMode? _appliedMode;

    public static ThemeValues Apply(ThemeMode mode, string? accentKey)
    {
        var values = ThemePalette.Build(mode, accentKey);
        var app = Application.Current;
        if (app is not null)
        {
            foreach (var (key, color) in values.Brushes)
            {
                // 同模式下换主题色时，绝大部分中性色不变：跳过可把一次切换的资源通知量减少约七成
                if (_applied.TryGetValue(key, out var previous) && previous == color && app.Resources.Contains(key))
                {
                    continue;
                }
                app.Resources[key] = new SolidColorBrush(color);
            }

            // Color 键供 HandyControl 的 DynamicResource 使用（日期弹窗、焦点色）
            foreach (var (key, color) in values.Colors)
            {
                app.Resources[key] = color;
            }

            if (_appliedMode != mode)
            {
                ApplyHandyControlSkin(app, mode);
            }
            _applied = new Dictionary<string, Color>(values.Brushes, StringComparer.Ordinal);
            _appliedMode = mode;
        }
        return values;
    }

    /// <summary>
    /// HandyControl 自带浅/深两套皮肤：日期选择弹窗、数值控件按钮等内部模板
    /// 依赖皮肤中的中性色，仅改自有画刷无法覆盖，故随模式切换。
    /// </summary>
    private static void ApplyHandyControlSkin(Application app, ThemeMode mode)
    {
        var merged = app.Resources.MergedDictionaries;
        var dark = mode == ThemeMode.Dark;
        var target = new Uri(dark ? SkinDark : SkinLight, UriKind.Absolute);
        var marker = dark ? "SkinDark" : "SkinDefault";

        for (var i = 0; i < merged.Count; i++)
        {
            var source = merged[i].Source?.OriginalString;
            if (source is null
                || !source.Contains("HandyControl", StringComparison.OrdinalIgnoreCase)
                || !source.Contains("Skin", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!source.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                merged[i] = new ResourceDictionary { Source = target };
            }
            return;
        }

        // 未找到现有皮肤：插到最前，保持自有设计令牌优先级更高
        merged.Insert(0, new ResourceDictionary { Source = target });
    }
}
