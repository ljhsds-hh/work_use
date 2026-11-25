using System.IO;
using QuietRemind.Models;
using QuietRemind.Services;
using Xunit;

namespace QuietRemind.Tests;

/// <summary>主题切换的持久化链路：设置写入、重复切换不写盘、磁盘可回读。</summary>
public class ThemeServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly JsonStore _store;
    private readonly AppServices _services;

    public ThemeServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "qr-theme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _store = new JsonStore(_dir, Path.Combine(_dir, "exit.marker"));
        var clock = SystemClock.Instance;
        var log = new LogService(Path.Combine(_dir, "logs"));
        _services = new AppServices
        {
            Store = _store,
            Data = new AppData(),
            Clock = clock,
            Engine = new ReminderEngine(clock),
            Planner = new OccurrencePlanner(clock),
            Log = log,
            Guard = new TaskSchedulerGuard("QuietRemind.exe", log),
            Hotkey = new GlobalHotkeyService(),
            DataDir = _dir,
        };
    }

    public void Dispose()
    {
        _services.Hotkey.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // 测试临时目录清理失败不影响结论
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void 切换深色与主题色_写入设置并可回读()
    {
        var changed = ThemeService.ApplyAndPersist(_services, ThemeMode.Dark, ThemeAccentCatalog.Berry);

        Assert.True(changed);
        Assert.Equal(ThemeMode.Dark, _services.Data.Settings.ThemeMode);
        Assert.Equal(ThemeAccentCatalog.Berry, _services.Data.Settings.ThemeAccent);

        var reloaded = _store.Load().Data.Settings;
        Assert.Equal(ThemeMode.Dark, reloaded.ThemeMode);
        Assert.Equal(ThemeAccentCatalog.Berry, reloaded.ThemeAccent);
    }

    [Fact]
    public void 相同主题重复切换_不重复写盘()
    {
        Assert.True(ThemeService.ApplyAndPersist(_services, ThemeMode.Dark, ThemeAccentCatalog.Pine));
        var first = File.ReadAllText(Path.Combine(_dir, "settings.json"));

        Assert.False(ThemeService.ApplyAndPersist(_services, ThemeMode.Dark, ThemeAccentCatalog.Pine));
        Assert.Equal(first, File.ReadAllText(Path.Combine(_dir, "settings.json")));
    }

    [Fact]
    public void 未知主题色_归一为默认而不是写入脏值()
    {
        var changed = ThemeService.ApplyAndPersist(_services, ThemeMode.Light, "neon-unicorn");

        Assert.False(changed);
        Assert.Equal(ThemeAccentCatalog.DefaultKey, _services.Data.Settings.ThemeAccent);
    }

    [Fact]
    public void 主题设置以字符串枚举写入便于手工编辑()
    {
        ThemeService.ApplyAndPersist(_services, ThemeMode.Dark, ThemeAccentCatalog.Indigo);

        var json = File.ReadAllText(Path.Combine(_dir, "settings.json"));
        Assert.Contains("\"ThemeMode\": \"Dark\"", json);
        Assert.Contains("\"ThemeAccent\": \"indigo\"", json);
    }

    [Fact]
    public void 旧版设置缺少主题字段_按浅色松绿启动()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), """
        {
          "GuardEnabled": false,
          "SnoozeMinutes": [5, 10, 30]
        }
        """);

        var settings = _store.Load().Data.Settings;

        Assert.Equal(ThemeMode.Light, settings.ThemeMode);
        Assert.Equal(ThemeAccentCatalog.DefaultKey, settings.ThemeAccent);
    }
}
