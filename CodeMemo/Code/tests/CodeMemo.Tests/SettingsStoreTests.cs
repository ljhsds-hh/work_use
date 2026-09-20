using System.IO;
using CodeMemo.Models;
using CodeMemo.Services;
using Xunit;

namespace CodeMemo.Tests;

/// <summary>偏好持久化与取值归一化：偏好坏了不能影响启动。</summary>
public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"codememo-settings-{Guid.NewGuid():N}");

    private string File_ => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void 文件缺失时返回默认偏好()
    {
        var settings = new SettingsStore(_dir).Load();

        Assert.Equal(AppSettingValues.ThemeDefault, settings.Theme);
        Assert.Equal(AppSettingValues.FontNormal, settings.FontSize);
        Assert.True(settings.MinimizeToTray);
        Assert.True(settings.GlobalHotkeyEnabled);
        Assert.Equal(AppSettingValues.DefaultHotkey, settings.GlobalHotkey);
    }

    [Fact]
    public void 保存后往返一致且写入版本号()
    {
        var store = new SettingsStore(_dir);
        store.Save(new AppSettings
        {
            Theme = AppSettingValues.ThemeDark,
            FontSize = AppSettingValues.FontLarge,
            MinimizeToTray = false,
            GlobalHotkeyEnabled = false,
            GlobalHotkey = "Shift+F9",
        });

        var loaded = store.Load();

        Assert.Equal(AppSettingValues.ThemeDark, loaded.Theme);
        Assert.Equal(AppSettingValues.FontLarge, loaded.FontSize);
        Assert.False(loaded.MinimizeToTray);
        Assert.False(loaded.GlobalHotkeyEnabled);
        Assert.Equal("Shift+F9", loaded.GlobalHotkey);
        Assert.Equal(SettingsStore.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    [Fact]
    public void 文件损坏时回落默认值()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, "{ 这不是合法 json");

        var settings = new SettingsStore(_dir).Load();

        Assert.Equal(AppSettingValues.ThemeDefault, settings.Theme);
    }

    [Fact]
    public void 取值非法时归一化_热键非法回落到默认()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, """
            { "Theme": "Neon", "FontSize": "Huge", "GlobalHotkey": "鼠标中键", "MinimizeToTray": false }
            """);

        var settings = new SettingsStore(_dir).Load();

        Assert.Equal(AppSettingValues.ThemeDefault, settings.Theme);
        Assert.Equal(AppSettingValues.FontNormal, settings.FontSize);
        Assert.Equal(AppSettingValues.DefaultHotkey, settings.GlobalHotkey);
        Assert.False(settings.MinimizeToTray);   // 合法取值照常保留
    }

    [Fact]
    public void 归一化函数_未知值一律回落()
    {
        Assert.Equal(AppSettingValues.ThemeDark, AppSettingValues.NormalizeTheme("Dark"));
        Assert.Equal(AppSettingValues.ThemeDefault, AppSettingValues.NormalizeTheme("dark"));
        Assert.Equal(AppSettingValues.ThemeDefault, AppSettingValues.NormalizeTheme(null));

        Assert.Equal(AppSettingValues.FontSmall, AppSettingValues.NormalizeFontSize("Small"));
        Assert.Equal(AppSettingValues.FontLarge, AppSettingValues.NormalizeFontSize("Large"));
        Assert.Equal(AppSettingValues.FontNormal, AppSettingValues.NormalizeFontSize("Big"));
    }

    [Fact]
    public void 找不到选项时回落到第一个()
    {
        var option = AppSettingValues.OptionOf(AppSettingValues.ThemeOptions, "不存在的皮肤");

        Assert.Equal(AppSettingValues.ThemeDefault, option.Value);
        Assert.Equal(3, AppSettingValues.ThemeOptions.Count);
        Assert.Equal(3, AppSettingValues.FontSizeOptions.Count);
    }

    [Fact]
    public void 主题候选含跟随系统_热键候选都能解析()
    {
        var system = AppSettingValues.ThemeOptions.Single(o => o.Value == AppSettingValues.ThemeSystem);
        Assert.Equal("跟随系统", system.Label);

        Assert.True(AppSettingValues.IsSystemTheme("System"));
        Assert.False(AppSettingValues.IsSystemTheme("Dark"));
        Assert.False(AppSettingValues.IsSystemTheme(null));
        Assert.Equal(AppSettingValues.ThemeSystem, AppSettingValues.NormalizeTheme("System"));
        Assert.Equal(AppSettingValues.ThemeDefault, AppSettingValues.NormalizeTheme("system"));   // 大小写不认识 → 明亮

        Assert.NotEmpty(AppSettingValues.HotkeyOptions);
        Assert.All(AppSettingValues.HotkeyOptions, o => Assert.True(HotkeyGesture.TryParse(o.Value, out _)));
    }

    [Fact]
    public void 跟随系统主题能往返保存()
    {
        var store = new SettingsStore(_dir);
        store.Save(new AppSettings { Theme = AppSettingValues.ThemeSystem });

        var loaded = store.Load();

        Assert.Equal(AppSettingValues.ThemeSystem, loaded.Theme);
        Assert.True(AppSettingValues.IsSystemTheme(loaded.Theme));
    }
}

/// <summary>字号档位 → 实际字号：三档要拉开差距且数值对齐到 0.5。</summary>
public class AppearanceScaleTests
{
    [Fact]
    public void 标准档就是基准字号()
    {
        var sizes = AppearanceScale.ResolveFontSizes(AppSettingValues.FontNormal);

        Assert.Equal(11d, sizes[AppearanceScale.FontBadgeKey]);
        Assert.Equal(12d, sizes[AppearanceScale.FontSmallKey]);
        Assert.Equal(13.5d, sizes[AppearanceScale.FontBodyKey]);
        Assert.Equal(18d, sizes[AppearanceScale.FontTitleKey]);
    }

    [Fact]
    public void 小档与高档按比例缩放且对齐到半点()
    {
        var small = AppearanceScale.ResolveFontSizes(AppSettingValues.FontSmall);
        var large = AppearanceScale.ResolveFontSizes(AppSettingValues.FontLarge);

        Assert.Equal(0.9, AppearanceScale.ScaleOf(AppSettingValues.FontSmall));
        Assert.Equal(1.2, AppearanceScale.ScaleOf(AppSettingValues.FontLarge));
        Assert.Equal(10d, small[AppearanceScale.FontBadgeKey]);
        Assert.Equal(16d, small[AppearanceScale.FontTitleKey]);
        Assert.Equal(13d, large[AppearanceScale.FontBadgeKey]);
        Assert.Equal(21.5d, large[AppearanceScale.FontTitleKey]);
        Assert.All(large.Values, v => Assert.Equal(Math.Round(v * 2) / 2, v));
    }

    [Fact]
    public void 未知档位按标准档处理()
    {
        Assert.Equal(1.0, AppearanceScale.ScaleOf("Huge"));
        Assert.Equal(1.0, AppearanceScale.ScaleOf(null));
    }
}
