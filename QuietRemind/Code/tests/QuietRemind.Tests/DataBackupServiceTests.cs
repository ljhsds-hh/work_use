using System.IO;
using System.Text.Json;
using QuietRemind.Models;
using QuietRemind.Services;
using Xunit;

namespace QuietRemind.Tests;

public class DataBackupServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "QuietRemindTests", Guid.NewGuid().ToString("N"));

    public DataBackupServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // 测试清理失败不影响断言结果
        }
    }

    private static AppData SampleData() => new()
    {
        Tasks =
        [
            new ReminderTask
            {
                Content = "备份任务",
                Time = new TimeSpan(9, 30, 0),
                RecurrenceType = RecurrenceType.Weekly,
                WeekDays = [DayOfWeek.Monday, DayOfWeek.Friday],
                CreatedAt = new DateTime(2026, 9, 1, 8, 0, 0),
                UpdatedAt = new DateTime(2026, 9, 1, 8, 0, 0),
            },
        ],
        Occurrences =
        [
            new Occurrence
            {
                TaskId = Guid.NewGuid(),
                TriggerAt = new DateTime(2026, 9, 21, 9, 30, 0),
                OriginalTriggerAt = new DateTime(2026, 9, 21, 9, 30, 0),
                State = OccurrenceState.Completed,
                SettledAt = new DateTime(2026, 9, 21, 9, 31, 0),
            },
        ],
        Settings = new AppSettings
        {
            GuardEnabled = false,
            SnoozeMinutes = [7, 17, 37],
            ReminderDimPercent = 40,
            QuickAddHotkeyEnabled = false,
        },
    };

    [Fact]
    public void 导出导入_完整保留任务实例与设置()
    {
        var path = Path.Combine(_dir, "backup.json");
        var data = SampleData();

        DataBackupService.Export(path, data);
        var imported = DataBackupService.Import(path, out var warnings);

        Assert.Empty(warnings);
        var task = Assert.Single(imported.Tasks);
        Assert.Equal("备份任务", task.Content);
        Assert.Equal(RecurrenceType.Weekly, task.RecurrenceType);
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Friday }, task.WeekDays);

        var occurrence = Assert.Single(imported.Occurrences);
        Assert.Equal(OccurrenceState.Completed, occurrence.State);

        Assert.False(imported.Settings.GuardEnabled);
        Assert.False(imported.Settings.QuickAddHotkeyEnabled);
        Assert.Equal(new[] { 7, 17, 37 }, imported.Settings.SnoozeMinutes);
        Assert.Equal(40, imported.Settings.ReminderDimPercent);
    }

    [Fact]
    public void 导入_版本不支持_给出明确错误()
    {
        var path = Path.Combine(_dir, "future.json");
        File.WriteAllText(path, """{"Version":99,"Data":{"Tasks":[],"Occurrences":[],"Settings":{}}}""");

        var ex = Assert.Throws<InvalidDataException>(() => DataBackupService.Import(path, out _));
        Assert.Contains("版本", ex.Message);
    }

    [Fact]
    public void 导入_损坏JSON_不抛出底层解析异常()
    {
        var path = Path.Combine(_dir, "broken.json");
        File.WriteAllText(path, "{ not json");

        var ex = Assert.Throws<InvalidDataException>(() => DataBackupService.Import(path, out _));
        Assert.Contains("格式无效", ex.Message);
    }

    [Fact]
    public void 导入_普通设置文件或缺少字段_拒绝而不是当成空备份清空数据()
    {
        // 回归：BackupBundle 的 Version/Data 有默认值，曾把 settings.json 误判为合法空备份
        var settingsLike = Path.Combine(_dir, "settings-like.json");
        File.WriteAllText(settingsLike, """{"GuardEnabled":false,"SnoozeMinutes":[5,10,30]}""");
        var ex1 = Assert.Throws<InvalidDataException>(() => DataBackupService.Import(settingsLike, out _));
        Assert.Contains("备份", ex1.Message);

        var missingFields = Path.Combine(_dir, "missing-fields.json");
        File.WriteAllText(missingFields, """{"Version":1,"Data":{}}""");
        var ex2 = Assert.Throws<InvalidDataException>(() => DataBackupService.Import(missingFields, out _));
        Assert.Contains("缺少任务", ex2.Message);
    }

    [Fact]
    public void 导入_越界数据归一化并给出提示()
    {
        var path = Path.Combine(_dir, "dirty.json");
        File.WriteAllText(path, """
            {"Version":1,"Data":{"Tasks":[],"Occurrences":[],"Settings":{"SnoozeMinutes":[0],"ReminderDimPercent":500}}}
            """);

        var imported = DataBackupService.Import(path, out var warnings);

        Assert.NotEmpty(warnings);
        // 越界项钳制到 1；缺失的第 2/3 项按默认值补齐
        Assert.Equal(new[] { 1, 10, 30 }, imported.Settings.SnoozeMinutes);
        Assert.Equal(90, imported.Settings.ReminderDimPercent);
    }

    [Fact]
    public void 每日快照_同一天只写一次且保留数量有上限()
    {
        Assert.True(DataBackupService.TryCreateDailySnapshot(_dir, SampleData(), out var error));
        Assert.Null(error);

        var snapshotDir = Path.Combine(_dir, "snapshots");
        var todayFile = Path.Combine(snapshotDir, $"backup-{DateTime.Now:yyyyMMdd}.json");
        Assert.True(File.Exists(todayFile));
        var firstWrite = File.ReadAllText(todayFile);

        // 第二次调用不应覆盖当天快照
        var changed = SampleData();
        changed.Tasks[0].Content = "不应写入快照";
        Assert.True(DataBackupService.TryCreateDailySnapshot(_dir, changed, out _));
        Assert.Equal(firstWrite, File.ReadAllText(todayFile));

        // 预置更多历史快照，调用后应只保留最近的 5 份
        for (var i = 1; i <= 7; i++)
        {
            File.WriteAllText(Path.Combine(snapshotDir, $"backup-2020{i:00}01.json"), "{}");
        }
        Assert.True(DataBackupService.TryCreateDailySnapshot(_dir, changed, out _));
        Assert.Equal(DataBackupService.SnapshotKeep, Directory.GetFiles(snapshotDir, "backup-*.json").Length);
        Assert.True(File.Exists(todayFile));
    }

    [Fact]
    public void 导入前备份_文件存在且可再次导入()
    {
        var data = SampleData();
        var path = DataBackupService.CreatePreImportBackup(_dir, data);

        Assert.True(File.Exists(path));
        var restored = DataBackupService.Import(path, out _);
        Assert.Equal("备份任务", Assert.Single(restored.Tasks).Content);
    }

    [Fact]
    public void 导出覆盖_原子替换且不残留临时文件()
    {
        var path = Path.Combine(_dir, "backup.json");
        var first = SampleData();
        var second = SampleData();
        second.Tasks[0].Content = "第二次导出";

        DataBackupService.Export(path, first);
        DataBackupService.Export(path, second);

        Assert.Equal("第二次导出", Assert.Single(DataBackupService.Import(path, out _).Tasks).Content);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void 每日快照_当天残留空文件会被重写而不是永久跳过()
    {
        var snapshotDir = Path.Combine(_dir, "snapshots");
        Directory.CreateDirectory(snapshotDir);
        var todayFile = Path.Combine(snapshotDir, $"backup-{DateTime.Now:yyyyMMdd}.json");
        File.WriteAllText(todayFile, ""); // 模拟上次写入中断

        Assert.True(DataBackupService.TryCreateDailySnapshot(_dir, SampleData(), out var error));
        Assert.Null(error);
        Assert.True(new FileInfo(todayFile).Length > 0);
        Assert.Equal("备份任务", Assert.Single(DataBackupService.Import(todayFile, out _).Tasks).Content);
    }

    [Fact]
    public void 导入数据整体接管内存_后续修改与源数据保持同一引用()
    {
        var target = new AppData();
        var source = SampleData();

        target.ReplaceWith(source);

        Assert.Same(source.Tasks, target.Tasks);
        Assert.Same(source.Occurrences, target.Occurrences);
        Assert.Same(source.Settings, target.Settings);
        source.Tasks.Clear();
        Assert.Empty(target.Tasks);
    }
}
