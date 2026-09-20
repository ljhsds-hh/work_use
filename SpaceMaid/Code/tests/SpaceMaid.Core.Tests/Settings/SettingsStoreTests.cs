using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Logging;
using SpaceMaid.Core.Settings;
using SpaceMaid.Core.Tests.Abstractions;

namespace SpaceMaid.Core.Tests.Settings;

internal sealed class SettingsFakeClock : IClock
{
    public SettingsFakeClock(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }
}

public class SettingsStoreTests
{
    [Fact]
    public void Should_roundtrip_settings()
    {
        using var root = new TempRoot();
        var store = new SettingsStore(root.Combine("settings.json"));
        var settings = new AppSettings
        {
            QuarantineBasePath = @"D:\Q",
            RetentionDays = 14,
            IncludeOtherDriveRecycleBin = true,
            LogDirectory = @"D:\logs\SpaceMaid",
            ReportDirectory = @"D:\logs\SpaceMaid\清单"
        };

        Assert.True(store.TrySave(settings, out var error), error);
        var loaded = store.Load();

        Assert.Equal(@"D:\Q", loaded.QuarantineBasePath);
        Assert.Equal(14, loaded.RetentionDays);
        Assert.True(loaded.IncludeOtherDriveRecycleBin);
    }

    [Fact]
    public void Should_fall_back_to_defaults_on_corrupt_file()
    {
        using var root = new TempRoot();
        var path = root.WriteFile("settings.json", "{ 这不是合法 json ");
        var store = new SettingsStore(path);

        var loaded = store.Load();

        Assert.Equal(7, loaded.RetentionDays);
        Assert.False(loaded.IncludeOtherDriveRecycleBin);
        Assert.False(string.IsNullOrWhiteSpace(loaded.QuarantineBasePath));
    }

    [Fact]
    public void Should_normalize_out_of_range_values()
    {
        var normalized = SettingsStore.Normalize(new AppSettings
        {
            RetentionDays = 0,
            LogRetentionDays = -5,
            QuarantineBasePath = "   ",
            LogDirectory = string.Empty
        });

        Assert.Equal(1, normalized.RetentionDays);
        Assert.Equal(1, normalized.LogRetentionDays);
        Assert.Equal(AppSettings.DefaultQuarantineBasePath, normalized.QuarantineBasePath);
        Assert.Equal(AppSettings.DefaultLogDirectory, normalized.LogDirectory);

        var huge = SettingsStore.Normalize(new AppSettings { RetentionDays = 9999, LogRetentionDays = 99999 });
        Assert.Equal(365, huge.RetentionDays);
        Assert.Equal(3650, huge.LogRetentionDays);
    }

    [Fact]
    public void Should_use_default_path_under_appdata()
    {
        var path = SettingsStore.DefaultFilePath;

        Assert.Contains("SpaceMaid", path);
        Assert.EndsWith("settings.json", path);
    }
}

public class LogHousekeepingTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Should_prune_only_old_logs()
    {
        using var root = new TempRoot();
        var directory = root.Combine("logs");
        Directory.CreateDirectory(directory);

        var old = root.WriteFile(@"logs\spacemaid-20260101.log", "old");
        var recent = root.WriteFile(@"logs\spacemaid-20260919.log", "recent");
        var unknown = root.WriteFile(@"logs\notes.txt", "user file");

        var removed = LogHousekeeping.PruneOldLogs(directory, 30, new SettingsFakeClock(Now), new Platform.WindowsFileSystem());

        Assert.Equal(1, removed);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(unknown), "看不懂的文件一律不动");
    }

    [Fact]
    public void Should_do_nothing_when_directory_missing()
    {
        using var root = new TempRoot();

        var removed = LogHousekeeping.PruneOldLogs(root.Combine("nope"), 30, new SettingsFakeClock(Now), new Platform.WindowsFileSystem());

        Assert.Equal(0, removed);
    }
}
