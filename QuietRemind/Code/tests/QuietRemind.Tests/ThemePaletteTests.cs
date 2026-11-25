using System.Windows.Media;
using QuietRemind.Models;
using QuietRemind.Services;
using Xunit;

namespace QuietRemind.Tests;

/// <summary>
/// 主题配色的质量闸门：任何主题色/任何模式都不能产出读不清的文字，
/// 也不允许深色模式意外复用浅色色板（两者的画布亮度必须明显不同）。
/// </summary>
public class ThemePaletteTests
{
    public static TheoryData<string, ThemeMode> AllCombinations()
    {
        var data = new TheoryData<string, ThemeMode>();
        foreach (var accent in ThemeAccentCatalog.All)
        {
            data.Add(accent.Key, ThemeMode.Light);
            data.Add(accent.Key, ThemeMode.Dark);
        }
        return data;
    }

    public static TheoryData<string> AllAccents()
    {
        var data = new TheoryData<string>();
        foreach (var accent in ThemeAccentCatalog.All)
        {
            data.Add(accent.Key);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void 正文与画布对比度达到AAA(string accent, ThemeMode mode)
    {
        var p = ThemePalette.Build(mode, accent).Brushes;
        var ratio = ColorMath.ContrastRatio(p["BInk"], p["BCanvas"]);
        Assert.True(ratio >= 7, $"{accent}/{mode} 正文对比度仅 {ratio:F2}");
    }

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void 正文与卡片对比度达到AAA(string accent, ThemeMode mode)
    {
        var p = ThemePalette.Build(mode, accent).Brushes;
        var ratio = ColorMath.ContrastRatio(p["BInk"], p["BSurface"]);
        Assert.True(ratio >= 7, $"{accent}/{mode} 卡片正文对比度仅 {ratio:F2}");
    }

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void 说明文字与卡片对比度达到AA(string accent, ThemeMode mode)
    {
        var p = ThemePalette.Build(mode, accent).Brushes;
        var muted = ColorMath.ContrastRatio(p["BMuted"], p["BSurface"]);
        var faint = ColorMath.ContrastRatio(p["BFaint"], p["BSurface"]);
        Assert.True(muted >= 4.5, $"{accent}/{mode} 次要文字对比度仅 {muted:F2}");
        Assert.True(faint >= 4.5, $"{accent}/{mode} 辅助文字对比度仅 {faint:F2}");
    }

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void 主按钮文字与主色对比度达到AA(string accent, ThemeMode mode)
    {
        var p = ThemePalette.Build(mode, accent).Brushes;
        var ratio = ColorMath.ContrastRatio(p["BOnAccent"], p["BAccent"]);
        Assert.True(ratio >= 4.5, $"{accent}/{mode} 主按钮文字对比度仅 {ratio:F2}");
    }

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void 主题色在卡片上可辨识(string accent, ThemeMode mode)
    {
        var p = ThemePalette.Build(mode, accent).Brushes;
        var ratio = ColorMath.ContrastRatio(p["BAccent"], p["BSurface"]);
        Assert.True(ratio >= 3.0, $"{accent}/{mode} 主题色对比度仅 {ratio:F2}");
    }

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void 软底上的主题色文字达到AA(string accent, ThemeMode mode)
    {
        var p = ThemePalette.Build(mode, accent).Brushes;
        var ratio = ColorMath.ContrastRatio(p["BAccent"], p["BAccentSoft"]);
        Assert.True(ratio >= 4.5, $"{accent}/{mode} 软底文字对比度仅 {ratio:F2}");
    }

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void 侧栏文字在深色栏上可读(string accent, ThemeMode mode)
    {
        var p = ThemePalette.Build(mode, accent).Brushes;
        var rail = ColorMath.ContrastRatio(p["BRailText"], p["BRail"]);
        var active = ColorMath.ContrastRatio(p["BRailActiveText"], p["BRailActive"]);
        Assert.True(rail >= 3.5, $"{accent}/{mode} 侧栏文字对比度仅 {rail:F2}");
        Assert.True(active >= 4.5, $"{accent}/{mode} 侧栏选中文字对比度仅 {active:F2}：fg={p["BRailActiveText"]} bg={p["BRailActive"]}");
    }

    [Theory]
    [MemberData(nameof(AllAccents))]
    public void 深色模式必须比浅色模式更暗(string accent)
    {
        var light = ThemePalette.Build(ThemeMode.Light, accent).Brushes;
        var dark = ThemePalette.Build(ThemeMode.Dark, accent).Brushes;
        Assert.True(
            ColorMath.RelativeLuminance(dark["BCanvas"]) < ColorMath.RelativeLuminance(light["BCanvas"]) - 0.4);
        Assert.True(
            ColorMath.RelativeLuminance(dark["BSurface"]) < ColorMath.RelativeLuminance(light["BSurface"]) - 0.4);
    }

    [Fact]
    public void 未知主题色回退默认且配色稳定()
    {
        Assert.Equal(ThemeAccentCatalog.DefaultKey, ThemeAccentCatalog.Normalize("neon-unicorn"));
        Assert.Equal(ThemeAccentCatalog.DefaultKey, ThemeAccentCatalog.Normalize(null));
        Assert.Equal(ThemeAccentCatalog.Indigo, ThemeAccentCatalog.Normalize(" INDIGO "));

        var a = ThemePalette.Build(ThemeMode.Dark, "not-exist").Brushes;
        var b = ThemePalette.Build(ThemeMode.Dark, ThemeAccentCatalog.DefaultKey).Brushes;
        foreach (var (key, color) in b)
        {
            Assert.Equal(color, a[key]);
        }
    }

    [Fact]
    public void 六个主题色互不相同且覆盖浅色入口()
    {
        Assert.InRange(ThemeAccentCatalog.All.Count, 5, 7);
        var distinct = ThemeAccentCatalog.All.Select(a => a.Base.ToString()).Distinct().Count();
        Assert.Equal(ThemeAccentCatalog.All.Count, distinct);
    }

    [Fact]
    public void 配色覆盖界面引用的全部键()
    {
        var values = ThemePalette.Build(ThemeMode.Light, ThemeAccentCatalog.Pine);
        string[] required =
        [
            "BInk", "BInkSoft", "BMuted", "BFaint", "BCanvas", "BSurface", "BSurfaceAlt", "BSegmentTrack",
            "BHairline", "BHairlineSoft", "BAccent", "BAccentHover", "BAccentSoft", "BOnAccent",
            "BAmber", "BAmberHover", "BAmberSoft", "BDanger", "BDangerSoft", "BDangerHover",
            "BSuccess", "BSuccessSoft", "BInfo", "BInfoSoft", "BRail", "BRailHover", "BRailActive",
            "BRailText", "BRailActiveText", "BRailSurface", "BRailSurfaceHover", "BRailTextHover",
            "BRailStrongText", "BRailFaint", "BOnDark", "BBrandMarkInk", "BTrackOff", "BSwitchThumb", "BSegmentThumb",
            "BGhostBorder", "BGhostHover", "BGhostPressed", "BDarkButtonText", "BDarkButtonHover",
            "BTagBlueFg", "BTagBlueBg", "BTagGreenFg", "BTagGreenBg", "BTagOrangeFg", "BTagOrangeBg",
            "BTagGrayFg", "BTagGrayBg", "BTagRedFg", "BTagRedBg", "BTagPurpleFg", "BTagPurpleBg",
            "BSidebar", "BSidebarCard", "BSidebarCardHover", "BSidebarBorder", "BSidebarText",
            "BSidebarStrongText", "BSidebarFaint", "BSidebarHover", "BSidebarActive", "BSidebarActiveText",
            "BWarningLine", "BErrorLine", "BSuccessLine", "BOverlayBase",
            "BPrimary", "BPrimaryHover", "BPrimaryLight", "BBgPage", "BBgCard", "BBgSubtle",
            "BBorder", "BRowLine", "BTextMain", "BTextSub", "BTextMuted",
            "PrimaryBrush", "LightPrimaryBrush", "DarkPrimaryBrush", "PrimaryTextBrush", "AccentBrush",
        ];
        foreach (var key in required)
        {
            Assert.True(values.Brushes.ContainsKey(key), $"缺少画刷键 {key}");
        }
        foreach (var key in new[] { "PrimaryColor", "LightPrimaryColor", "DarkPrimaryColor", "PrimaryTextColor", "AccentColor", "BorderColor" })
        {
            Assert.True(values.Colors.ContainsKey(key), $"缺少颜色键 {key}");
        }
    }
}
