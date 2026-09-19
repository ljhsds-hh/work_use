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
        var data = _store.Load().Data;
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

        var data = _store.Load().Data;
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
    public void 退出标记_自定义路径生效()
    {
        var customDir = Path.Combine(_dir, "logdir");
        var store = new JsonStore(_dir, Path.Combine(customDir, "exit.marker"));
        Assert.False(store.HasExitMarker());
        store.WriteExitMarker();
        Assert.True(File.Exists(Path.Combine(customDir, "exit.marker")));
        Assert.True(store.HasExitMarker());
        store.ClearExitMarker();
        Assert.False(store.HasExitMarker());
        Assert.False(_store.HasExitMarker()); // 数据目录默认标记不受影响
    }

    [Fact]
    public void 损坏文件_按域隔离_其余域照常加载()
    {
        // settings.json 损坏不应连带丢失完好的 tasks/occurrences（一次损坏不清空全部数据）
        var task = new ReminderTask { Content = "完好任务", Time = new TimeSpan(9, 0, 0) };
        var occ = new Occurrence { TaskId = task.Id, TriggerAt = new DateTime(2026, 9, 18, 9, 0, 0), OriginalTriggerAt = new DateTime(2026, 9, 18, 9, 0, 0) };
        _store.SaveTasks([task]);
        _store.SaveOccurrences([occ]);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ 这不是合法JSON ]");

        var result = _store.Load();

        // 损坏域按空加载且登记问题
        Assert.Single(result.Problems, p => p.Contains("settings.json"));
        Assert.False(result.Data.Settings.GuardEnabled == false); // 设置为默认值
        // 完好域照常加载
        var t = Assert.Single(result.Data.Tasks);
        Assert.Equal("完好任务", t.Content);
        Assert.Single(result.Data.Occurrences);
        // 损坏文件已备份
        Assert.NotEmpty(Directory.GetFiles(_dir, "settings.json.corrupt-*"));
    }

    [Fact]
    public void 损坏文件_本次会话禁写_不覆盖磁盘现场()
    {
        const string corrupt = "{ 损坏 ]";
        File.WriteAllText(Path.Combine(_dir, "occurrences.json"), corrupt);
        _ = _store.Load();

        // 后续保存 occurrences 被静默跳过：磁盘上仍是损坏现场（.corrupt 备份已生成），不被空数据覆盖
        _store.SaveOccurrences([new Occurrence()]);
        Assert.Equal(corrupt, File.ReadAllText(Path.Combine(_dir, "occurrences.json")));
        Assert.NotEmpty(Directory.GetFiles(_dir, "occurrences.json.corrupt-*"));
        // 其他文件不受禁写影响
        _store.SaveTasks([new ReminderTask()]);
        Assert.True(File.Exists(Path.Combine(_dir, "tasks.json")));
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
