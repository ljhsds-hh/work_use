using System.Windows.Media;
using QuietRemind.Models;

namespace QuietRemind.Services;

/// <summary>
/// 一次主题计算的结果：<see cref="Brushes"/> 是画刷键（就地改色即可刷新界面），
/// <see cref="Colors"/> 是 HandyControl 与渐变使用的 Color 键（整键覆盖）。
/// </summary>
public sealed record ThemeValues(
    IReadOnlyDictionary<string, Color> Brushes,
    IReadOnlyDictionary<string, Color> Colors);

/// <summary>
/// 主题配色计算：输入明暗模式 + 主题色，输出完整语义色板。
/// 纯函数、无 UI 依赖，便于单元测试校验对比度。
/// </summary>
public static class ThemePalette
{
    /// <summary>深色模式下画刷是否整体偏亮；用于断言与调试。</summary>
    public static ThemeValues Build(ThemeMode mode, string? accentKey)
    {
        var accentInfo = ThemeAccentCatalog.Resolve(accentKey);
        var (h, s, l) = ColorMath.ToHsl(accentInfo.Base);

        var brushes = new Dictionary<string, Color>(StringComparer.Ordinal);
        var colors = new Dictionary<string, Color>(StringComparer.Ordinal);

        void B(string key, Color value) => brushes["B" + key] = value;
        void BC(string key, Color value)
        {
            brushes["B" + key] = value;
            colors["C" + key] = value;
        }
        void C(string key, Color value) => colors[key] = value;

        // 主题色三档：浅色用原始色，深色提亮到深底可读；软底为同色相浅染。
        var accent = mode == ThemeMode.Light ? accentInfo.Base : ColorMath.FromHsl(h, s * 0.75, 0.60);
        var accentHover = mode == ThemeMode.Light
            ? ColorMath.FromHsl(h, s, Math.Max(0.14, l - 0.065))
            : ColorMath.FromHsl(h, s * 0.75, 0.68);
        var accentSoft = mode == ThemeMode.Light
            ? ColorMath.FromHsl(h, Math.Min(s * 0.50, 0.30), 0.93)
            : ColorMath.FromHsl(h, Math.Min(s * 0.35, 0.20), 0.15);
        // 选中态文字：主色与白色对比不足时改用深墨，避免深色模式亮色底上出现白字糊成一片。
        var onAccent = ColorMath.ContrastRatio(accent, ColorMath.White) >= 4.0
            ? ColorMath.White
            : Color.FromRgb(0x10, 0x17, 0x13);
        var railActiveText = mode == ThemeMode.Light
            ? ColorMath.FromHsl(h, Math.Min(s * 0.80, 0.50), 0.74)
            : ColorMath.FromHsl(h, Math.Min(s * 0.80, 0.50), 0.77);

        BC("Accent", accent);
        BC("AccentHover", accentHover);
        BC("AccentSoft", accentSoft);
        B("OnAccent", onAccent);

        if (mode == ThemeMode.Light)
        {
            BC("Ink", Rgb(0x18, 0x21, 0x1D));
            BC("InkSoft", Rgb(0x3C, 0x47, 0x42));
            BC("Muted", Rgb(0x55, 0x61, 0x5B));
            BC("Faint", Rgb(0x66, 0x71, 0x6B));
            BC("Canvas", Rgb(0xF6, 0xF3, 0xEC));
            BC("Surface", Rgb(0xFF, 0xFD, 0xF9));
            BC("SurfaceAlt", Rgb(0xFA, 0xF7, 0xF0));
            BC("SegmentTrack", Rgb(0xEF, 0xEA, 0xE0));
            BC("Hairline", Rgb(0xE4, 0xDE, 0xD2));
            BC("HairlineSoft", Rgb(0xF0, 0xEB, 0xE2));

            BC("Amber", Rgb(0xA9, 0x57, 0x0F));
            BC("AmberHover", Rgb(0x8A, 0x43, 0x0A));
            BC("AmberSoft", Rgb(0xFB, 0xF1, 0xE1));
            BC("Danger", Rgb(0xB4, 0x23, 0x18));
            BC("DangerSoft", Rgb(0xFD, 0xF0, 0xEF));
            BC("Success", Rgb(0x2F, 0x7D, 0x5A));
            BC("SuccessSoft", Rgb(0xE9, 0xF4, 0xEE));
            BC("Info", Rgb(0x2B, 0x5F, 0x86));
            BC("InfoSoft", Rgb(0xE9, 0xF1, 0xF7));

            BC("Rail", Rgb(0x10, 0x19, 0x16));
            BC("RailHover", Rgb(0x1A, 0x26, 0x21));
            BC("RailActive", Rgb(0x22, 0x32, 0x2B));
            BC("RailText", Rgb(0x9C, 0xAE, 0xA5));
            B("RailActiveText", railActiveText);

            // 侧栏：与主画布同源的纸色，靠发丝线与淡一档的底色分区
            B("Sidebar", Rgb(0xF1, 0xED, 0xE4));
            B("SidebarCard", Rgb(0xFF, 0xFD, 0xF9));
            B("SidebarCardHover", Rgb(0xF8, 0xF4, 0xEB));
            B("SidebarBorder", Rgb(0xE1, 0xDB, 0xCF));
            B("SidebarText", Rgb(0x55, 0x61, 0x5B));
            B("SidebarStrongText", Rgb(0x18, 0x21, 0x1D));
            B("SidebarFaint", Rgb(0x84, 0x80, 0x74));
            B("SidebarHover", Rgb(0xEA, 0xE5, 0xDA));
            B("SidebarActive", ColorMath.Blend(accent, Rgb(0xFF, 0xFD, 0xF9), 0.14));
            B("SidebarActiveText", accent);

            B("RailSurface", Rgb(0x17, 0x22, 0x1E));
            B("RailSurfaceHover", Rgb(0x1D, 0x2C, 0x25));
            B("RailTextHover", Rgb(0xDC, 0xE6, 0xE1));
            B("RailStrongText", Rgb(0xE9, 0xEF, 0xEA));
            B("RailFaint", Rgb(0x71, 0x85, 0x7B));
            B("OnDark", Rgb(0xF1, 0xF4, 0xF2));
            B("BrandMarkInk", Rgb(0x17, 0x23, 0x1E));

            B("SegmentThumb", Rgb(0xFF, 0xFD, 0xF9));
            B("TrackOff", Rgb(0xD8, 0xD3, 0xCA));
            B("SwitchThumb", ColorMath.White);
            B("GhostBorder", Rgb(0xD5, 0xD0, 0xC7));
            B("GhostHover", Rgb(0xED, 0xEA, 0xE3));
            B("GhostPressed", Rgb(0xE2, 0xDE, 0xD5));
            B("DangerHover", Rgb(0x8F, 0x1D, 0x14));
            B("DarkButtonText", Rgb(0xE7, 0xEF, 0xEA));
            B("DarkButtonHover", Rgb(0x0F, 0x15, 0x13));

            B("TagGrayBg", Rgb(0xF1, 0xEF, 0xEA));
            B("TagPurpleFg", Rgb(0x6B, 0x4E, 0x8E));
            B("TagPurpleBg", Rgb(0xF3, 0xEE, 0xF8));
            B("WarningLine", Rgb(0xEA, 0xD7, 0xB9));
            B("ErrorLine", Rgb(0xF1, 0xCC, 0xC8));
            B("SuccessLine", Rgb(0xCD, 0xE7, 0xD8));
            B("OverlayBase", Rgb(0x0B, 0x0F, 0x14));
        }
        else
        {
            BC("Ink", Rgb(0xF0, 0xEE, 0xE6));
            BC("InkSoft", Rgb(0xD2, 0xD5, 0xCB));
            BC("Muted", Rgb(0xA4, 0xA7, 0x9E));
            BC("Faint", Rgb(0x8B, 0x8F, 0x86));
            BC("Canvas", Rgb(0x12, 0x15, 0x11));
            BC("Surface", Rgb(0x1A, 0x1E, 0x19));
            BC("SurfaceAlt", Rgb(0x21, 0x26, 0x20));
            BC("SegmentTrack", Rgb(0x27, 0x2D, 0x26));
            BC("Hairline", Rgb(0x33, 0x3A, 0x31));
            BC("HairlineSoft", Rgb(0x2A, 0x30, 0x28));

            BC("Amber", Rgb(0xE0, 0xA4, 0x5F));
            BC("AmberHover", Rgb(0xEE, 0xB9, 0x7C));
            BC("AmberSoft", Rgb(0x33, 0x2A, 0x1D));
            BC("Danger", Rgb(0xF0, 0x73, 0x6A));
            BC("DangerSoft", Rgb(0x3A, 0x24, 0x22));
            BC("Success", Rgb(0x5F, 0xC7, 0x9C));
            BC("SuccessSoft", Rgb(0x1E, 0x35, 0x2B));
            BC("Info", Rgb(0x7F, 0xB6, 0xDD));
            BC("InfoSoft", Rgb(0x21, 0x32, 0x3F));

            BC("Rail", Rgb(0x0A, 0x0D, 0x0B));
            BC("RailHover", Rgb(0x15, 0x1B, 0x16));
            BC("RailActive", Rgb(0x1E, 0x2A, 0x23));
            BC("RailText", Rgb(0x97, 0xA6, 0x9D));
            B("RailActiveText", railActiveText);

            B("Sidebar", Rgb(0x18, 0x1C, 0x17));
            B("SidebarCard", Rgb(0x20, 0x25, 0x1E));
            B("SidebarCardHover", Rgb(0x27, 0x2D, 0x25));
            B("SidebarBorder", Rgb(0x2C, 0x33, 0x2A));
            B("SidebarText", Rgb(0xA4, 0xA7, 0x9E));
            B("SidebarStrongText", Rgb(0xF0, 0xEE, 0xE6));
            B("SidebarFaint", Rgb(0x80, 0x86, 0x7C));
            B("SidebarHover", Rgb(0x24, 0x2A, 0x22));
            B("SidebarActive", ColorMath.Blend(accent, Rgb(0x18, 0x1C, 0x17), 0.20));
            B("SidebarActiveText", accent);

            B("RailSurface", Rgb(0x14, 0x1A, 0x16));
            B("RailSurfaceHover", Rgb(0x1B, 0x23, 0x1D));
            B("RailTextHover", Rgb(0xE3, 0xEA, 0xE4));
            B("RailStrongText", Rgb(0xED, 0xF2, 0xED));
            B("RailFaint", Rgb(0x7C, 0x8B, 0x82));
            B("OnDark", Rgb(0xF1, 0xF4, 0xF2));
            B("BrandMarkInk", Rgb(0x17, 0x23, 0x1E));

            B("SegmentThumb", Rgb(0x3D, 0x46, 0x3C));
            B("TrackOff", Rgb(0x3A, 0x42, 0x3A));
            B("SwitchThumb", Rgb(0xED, 0xF1, 0xEC));
            B("GhostBorder", Rgb(0x45, 0x4D, 0x43));
            B("GhostHover", Rgb(0x26, 0x2C, 0x25));
            B("GhostPressed", Rgb(0x2E, 0x35, 0x2D));
            B("DangerHover", Rgb(0xF7, 0x92, 0x89));
            B("DarkButtonText", Rgb(0xED, 0xF2, 0xED));
            B("DarkButtonHover", Rgb(0x11, 0x16, 0x12));

            B("TagGrayBg", Rgb(0x27, 0x2D, 0x26));
            B("TagPurpleFg", Rgb(0xC0, 0xA6, 0xE4));
            B("TagPurpleBg", Rgb(0x2A, 0x24, 0x36));
            B("WarningLine", Rgb(0x4A, 0x3A, 0x26));
            B("ErrorLine", Rgb(0x4A, 0x2A, 0x27));
            B("SuccessLine", Rgb(0x26, 0x40, 0x2F));
            B("OverlayBase", Rgb(0x07, 0x0A, 0x08));
        }

        // 标签色：与状态色同源，避免出现两套绿/红
        B("TagBlueFg", brushes["BInfo"]);
        B("TagBlueBg", brushes["BInfoSoft"]);
        B("TagGreenFg", brushes["BAccent"]);
        B("TagGreenBg", brushes["BAccentSoft"]);
        B("TagOrangeFg", brushes["BAmber"]);
        B("TagOrangeBg", brushes["BAmberSoft"]);
        B("TagGrayFg", brushes["BMuted"]);
        B("TagRedFg", brushes["BDanger"]);
        B("TagRedBg", brushes["BDangerSoft"]);

        // 旧键名映射：部分窗口仍按旧名引用
        B("Primary", accent);
        B("PrimaryHover", accentHover);
        B("PrimaryLight", accentSoft);
        B("BgPage", brushes["BCanvas"]);
        B("BgCard", brushes["BSurface"]);
        B("BgSubtle", brushes["BSurfaceAlt"]);
        B("Border", brushes["BHairline"]);
        B("RowLine", brushes["BHairlineSoft"]);
        B("TextMain", brushes["BInk"]);
        B("TextSub", brushes["BMuted"]);
        B("TextMuted", brushes["BFaint"]);

        // HandyControl 主题键：让输入框焦点、日期/时间弹窗跟随主题色与明暗
        C("PrimaryColor", accent);
        C("LightPrimaryColor", accentSoft);
        C("DarkPrimaryColor", accentHover);
        C("PrimaryTextColor", onAccent);
        C("AccentColor", brushes["BAmber"]);
        C("BorderColor", brushes["BHairline"]);
        brushes["PrimaryBrush"] = accent;
        brushes["LightPrimaryBrush"] = accentSoft;
        brushes["DarkPrimaryBrush"] = accentHover;
        brushes["PrimaryTextBrush"] = brushes["BInk"];
        brushes["AccentBrush"] = brushes["BAmber"];

        // HandyControl 中性键：输入框图标、日期/时间弹窗、次级文字统一到自有色板，
        // 否则深色皮肤下日历/时钟图标与微调箭头会偏暗到几乎看不见。
        brushes["RegionBrush"] = brushes["BSurface"];
        brushes["SecondaryRegionBrush"] = brushes["BSurfaceAlt"];
        brushes["ThirdlyRegionBrush"] = brushes["BSurfaceAlt"];
        brushes["BackgroundBrush"] = brushes["BCanvas"];
        brushes["ControlBackgroundBrush"] = brushes["BSurface"];
        brushes["DefaultBrush"] = brushes["BSurface"];
        brushes["TitleBrush"] = brushes["BInk"];
        brushes["PrimaryTextBrush"] = brushes["BInk"];
        brushes["SecondaryTextBrush"] = brushes["BMuted"];
        brushes["ThirdlyTextBrush"] = brushes["BFaint"];
        brushes["TextIconBrush"] = brushes["BInkSoft"];
        brushes["ReverseTextBrush"] = brushes["BOnAccent"];
        brushes["SecondaryBorderBrush"] = brushes["BHairlineSoft"];
        // HC 的输入框内嵌图标（日历/时钟）取 BorderBrush 作为前景，需保持可见
        brushes["BorderBrush"] = mode == ThemeMode.Dark ? Rgb(0x6E, 0x78, 0x69) : Rgb(0xAD, 0xA7, 0x9A);

        return new ThemeValues(brushes, colors);
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}

/// <summary>颜色计算工具：HSL 转换、混色与 WCAG 对比度。</summary>
public static class ColorMath
{
    public static Color White => Color.FromRgb(0xFF, 0xFF, 0xFF);

    public static (double H, double S, double L) ToHsl(Color color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2.0;
        if (Math.Abs(max - min) < 1e-9)
        {
            return (0, 0, l);
        }
        var d = max - min;
        var s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        double h;
        if (Math.Abs(max - r) < 1e-9)
        {
            h = ((g - b) / d + (g < b ? 6 : 0)) * 60;
        }
        else if (Math.Abs(max - g) < 1e-9)
        {
            h = ((b - r) / d + 2) * 60;
        }
        else
        {
            h = ((r - g) / d + 4) * 60;
        }
        return (h, s, l);
    }

    public static Color FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        if (s < 1e-9)
        {
            var v = ToByte(l * 255);
            return Color.FromRgb(v, v, v);
        }
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var hp = h / 60.0;
        var x = c * (1 - Math.Abs(hp % 2 - 1));
        (double r, double g, double b) = hp switch
        {
            < 1 => (c, x, 0d),
            < 2 => (x, c, 0d),
            < 3 => (0d, c, x),
            < 4 => (0d, x, c),
            < 5 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        var m = l - c / 2;
        return Color.FromRgb(ToByte((r + m) * 255), ToByte((g + m) * 255), ToByte((b + m) * 255));
    }

    /// <summary>按权重混合：amount 为 <paramref name="foreground"/> 的占比（0~1）。</summary>
    public static Color Blend(Color foreground, Color background, double amount)
    {
        var a = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            ToByte(foreground.R * a + background.R * (1 - a)),
            ToByte(foreground.G * a + background.G * (1 - a)),
            ToByte(foreground.B * a + background.B * (1 - a)));
    }

    /// <summary>WCAG 相对亮度对比度，用于保证文字在任意主题色下都可读。</summary>
    public static double ContrastRatio(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var light = Math.Max(la, lb);
        var dark = Math.Min(la, lb);
        return (light + 0.05) / (dark + 0.05);
    }

    public static double RelativeLuminance(Color color)
    {
        static double Channel(byte value)
        {
            var s = value / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
