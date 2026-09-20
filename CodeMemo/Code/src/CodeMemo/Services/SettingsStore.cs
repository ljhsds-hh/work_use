using System.IO;
using System.Text.Json;
using CodeMemo.Models;

namespace CodeMemo.Services;

/// <summary>
/// 用户偏好持久化：目录 %AppData%\CodeMemo\，文件 settings.json。
/// 与命令库一样原子写；读取时对取值做归一化（配置文件被改坏也不影响启动）。
/// </summary>
public sealed class SettingsStore
{
    public const int CurrentSchemaVersion = 1;

    private readonly string _filePath;

    public SettingsStore(string directory)
    {
        _filePath = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
    }

    public string FilePath => _filePath;

    public static AppSettings Defaults() => new();

    /// <summary>读取偏好：文件缺失、损坏或取值非法时回落到默认值，绝不因为偏好问题打断启动。</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return Defaults();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath), LibraryStore.JsonOptions)
                ?? Defaults();

            settings.Theme = AppSettingValues.NormalizeTheme(settings.Theme);
            settings.FontSize = AppSettingValues.NormalizeFontSize(settings.FontSize);
            if (!HotkeyGesture.TryParse(settings.GlobalHotkey, out _))
            {
                settings.GlobalHotkey = AppSettingValues.DefaultHotkey;
            }
            return settings;
        }
        catch (Exception)
        {
            return Defaults();
        }
    }

    public void Save(AppSettings settings)
    {
        settings.SchemaVersion = CurrentSchemaVersion;

        var tmpPath = _filePath + ".tmp";
        using (var fs = File.Create(tmpPath))
        {
            JsonSerializer.Serialize(fs, settings, LibraryStore.JsonOptions);
        }
        File.Move(tmpPath, _filePath, overwrite: true);
    }
}

/// <summary>外观相关的纯逻辑：字号档位 → 实际 FontSize 数值。</summary>
public static class AppearanceScale
{
    public const string FontBadgeKey = "CodeMemoFontSizeBadge";
    public const string FontSmallKey = "CodeMemoFontSizeSmall";
    public const string FontBodyKey = "CodeMemoFontSizeBody";
    public const string FontTitleKey = "CodeMemoFontSizeTitle";

    private const double BaseBadge = 11;
    private const double BaseSmall = 12;
    private const double BaseBody = 13.5;
    private const double BaseTitle = 18;

    /// <summary>字号档位对应的缩放系数。</summary>
    public static double ScaleOf(string? fontSize) => AppSettingValues.NormalizeFontSize(fontSize) switch
    {
        AppSettingValues.FontSmall => 0.9,
        AppSettingValues.FontLarge => 1.2,
        _ => 1.0,
    };

    /// <summary>按档位算出四个字号资源的值（对齐到 0.5，避免歪数让排版抖动）。</summary>
    public static IReadOnlyDictionary<string, double> ResolveFontSizes(string? fontSize)
    {
        var scale = ScaleOf(fontSize);
        return new Dictionary<string, double>
        {
            [FontBadgeKey] = Round(BaseBadge * scale),
            [FontSmallKey] = Round(BaseSmall * scale),
            [FontBodyKey] = Round(BaseBody * scale),
            [FontTitleKey] = Round(BaseTitle * scale),
        };
    }

    private static double Round(double value) => Math.Round(value * 2, MidpointRounding.AwayFromZero) / 2;
}
