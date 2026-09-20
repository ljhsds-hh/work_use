using System.Text.Encodings.Web;
using System.Text.Json;
using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Logging;

namespace SpaceMaid.Core.Settings;

/// <summary>
/// 设置的读写与归一化。
/// 为什么必须归一化：配置文件是用户可见、可手改的，任何越界值都不该让程序行为失控
/// （例如保留天数写成 0 会让隔离文件立刻消失）。
/// </summary>
public sealed class SettingsStore
{
    /// <summary>统一序列化入口：中文不转义，便于用户直接看。</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _filePath;
    private readonly ILogSink _log;

    public SettingsStore(string? filePath = null, ILogSink? log = null)
    {
        _filePath = filePath ?? DefaultFilePath;
        _log = log ?? SilentLogSink.Instance;
    }

    /// <summary>默认配置路径：%AppData%\SpaceMaid\settings.json。</summary>
    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SpaceMaid",
        "settings.json");

    public string FilePath => _filePath;

    /// <summary>读取设置；文件缺失或损坏时返回默认值并记日志（绝不抛异常打断启动）。</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return Normalize(new AppSettings());
            }

            var json = File.ReadAllText(_filePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return settings is null ? Normalize(new AppSettings()) : Normalize(settings);
        }
        catch (Exception ex)
        {
            _log.Warn($"设置文件读取失败，已回退默认值：{ex.Message}");
            return Normalize(new AppSettings());
        }
    }

    /// <summary>原子保存设置。</summary>
    public bool TrySave(AppSettings settings, out string error)
    {
        error = string.Empty;

        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var normalized = Normalize(settings);
            var temp = _filePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(normalized, JsonOptions));
            File.Move(temp, _filePath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            error = $"保存设置失败：{ex.Message}";
            _log.Error(error);
            return false;
        }
    }

    /// <summary>归一化：夹取取值范围、剔除空白路径。</summary>
    public static AppSettings Normalize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings with
        {
            RetentionDays = Math.Clamp(settings.RetentionDays, 1, 365),
            LogRetentionDays = Math.Clamp(settings.LogRetentionDays, 1, 3650),
            QuarantineBasePath = string.IsNullOrWhiteSpace(settings.QuarantineBasePath)
                ? AppSettings.DefaultQuarantineBasePath
                : settings.QuarantineBasePath.Trim(),
            LogDirectory = string.IsNullOrWhiteSpace(settings.LogDirectory)
                ? AppSettings.DefaultLogDirectory
                : settings.LogDirectory.Trim(),
            ReportDirectory = string.IsNullOrWhiteSpace(settings.ReportDirectory)
                ? AppSettings.DefaultReportDirectory
                : settings.ReportDirectory.Trim()
        };
    }
}
