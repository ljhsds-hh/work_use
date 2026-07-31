using System.IO;
using System.Text.Json;

namespace DllTool.App.Services;

/// <summary>
/// 应用设置持久化：当前背景图片路径。
/// 存储于 %AppData%\DllTool\settings.json。
/// </summary>
public sealed class AppSettings
{
    private const string AppDataFolder = @"DllTool";
    private readonly string _settingsPath;

    public AppSettings()
    {
        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _settingsPath = Path.Combine(baseDir, AppDataFolder, "settings.json");
    }

    /// <summary>当前背景图片路径（未设置时为 null）。</summary>
    public string? BackgroundImagePath { get; set; }

    /// <summary>背景显示强度（0-100）。</summary>
    public double BackgroundStrength { get; set; } = 40;

    public void Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                string json = File.ReadAllText(_settingsPath);
                var data = JsonSerializer.Deserialize<AppSettings>(json);
                if (data is not null)
                {
                    BackgroundImagePath = data.BackgroundImagePath;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // 设置读取失败时使用默认值。
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            string json = JsonSerializer.Serialize(this);
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // 设置保存失败时忽略。
        }
    }
}
