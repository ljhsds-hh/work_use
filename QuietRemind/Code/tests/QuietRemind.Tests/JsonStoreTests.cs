using System.IO;
using System.Text.Json;
using QuietRemind.Models;
using QuietRemind.Services;
using Xunit;

namespace QuietRemind.Tests;

public class JsonStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "QuietRemindTests", Guid.NewGuid().ToString("N"));
    private readonly JsonStore _store;

    public JsonStoreTests() => _store = new JsonStore(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void 空目录加载_返回空数据与默认设置()
    {
        var data = _store.Load();
        Assert.Empty(data.Tasks);
        Assert.Empty(data.Occurrences);
        Assert.True(data.Settings.GuardEnabled);
        Assert.Equal([5, 10, 30], data.Settings.SnoozeMinutes);
    }

    [Fact]
    public void 保存与加载往返一致()
    {
        var task = new ReminderTask
        {
            Content = "16:50 交日报",
            Time = new TimeSpan(16, 50, 0),
            RecurrenceType = RecurrenceType.Daily,
        };
        var occ = new Occurrence
        {
            TaskId = task.Id,
            TriggerAt = new DateTime(2026, 9, 18, 16, 50, 0),
            OriginalTriggerAt = new DateTime(2026, 9, 18, 16, 50, 0),
            State = OccurrenceState.Missed,
            ReminderShownAt = new DateTime(2026, 9, 19, 8, 0, 0),
        };

        _store.SaveTasks([task]);
        _store.SaveOccurrences([occ]);
        _store.SaveSettings(new AppSettings { GuardEnabled = false, SnoozeMinutes = [3, 7] });

        var data = _store.Load();
        var t = Assert.Single(data.Tasks);
        Assert.Equal("16:50 交日报", t.Content);
        Assert.Equal(RecurrenceType.Daily, t.RecurrenceType);

        var o = Assert.Single(data.Occurrences);
        Assert.Equal(OccurrenceState.Missed, o.State);
        Assert.NotNull(o.ReminderShownAt);

        Assert.False(data.Settings.GuardEnabled);
        Assert.Equal([3, 7], data.Settings.SnoozeMinutes);
    }

    [Fact]
    public void 退出标记写入与清除()
    {
        Assert.False(_store.HasExitMarker());
        _store.WriteExitMarker();
        Assert.True(_store.HasExitMarker());
        _store.ClearExitMarker();
        Assert.False(_store.HasExitMarker());
    }

    [Fact]
    public void 损坏数据文件_抛异常且备份现场()
    {
        File.WriteAllText(Path.Combine(_dir, "tasks.json"), "{ 这不是合法JSON ]");

        Assert.Throws<InvalidDataException>(() => _store.Load());

        var corruptFiles = Directory.GetFiles(_dir, "tasks.json.corrupt-*");
        Assert.NotEmpty(corruptFiles);
    }

    [Fact]
    public void 原子写_无残留临时文件()
    {
        _store.SaveTasks([new ReminderTask()]);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.Single(Directory.GetFiles(_dir, "tasks.json"));
    }

    [Fact]
    public void 序列化格式_TimeSpan与枚举可读()
    {
        var task = new ReminderTask { Time = new TimeSpan(16, 50, 0), RecurrenceType = RecurrenceType.Weekly };
        _store.SaveTasks([task]);

        var json = File.ReadAllText(Path.Combine(_dir, "tasks.json"));
        using var doc = JsonDocument.Parse(json);
        Assert.Contains("16:50:00", json);
        Assert.Contains("Weekly", json);
    }
}
