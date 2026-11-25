using QuietRemind.Models;

namespace QuietRemind.Services;

/// <summary>应用级服务聚合：App 启动编排创建，注入 UI 与业务层共享。</summary>
public sealed class AppServices
{
    public required JsonStore Store { get; init; }
    public required AppData Data { get; init; }
    public required IClock Clock { get; init; }
    public required ReminderEngine Engine { get; init; }
    public required OccurrencePlanner Planner { get; init; }
    public required LogService Log { get; init; }
    public required TaskSchedulerGuard Guard { get; init; }
    /// <summary>快速新增全局快捷键（UI 线程创建；按设置注册/注销）。</summary>
    public required GlobalHotkeyService Hotkey { get; init; }
    /// <summary>数据目录（%AppData%\QuietRemind），供背景图等资源文件存放。</summary>
    public required string DataDir { get; init; }

    /// <summary>实际日志目录，诊断界面展示与打开日志使用。</summary>
    public string LogDir => Log.LogDirectory;

    /// <summary>本次会话最近一次落盘失败信息（诊断展示；成功后保留，便于回溯）。</summary>
    public string? LastPersistenceError { get; private set; }

    public DateTime? LastPersistenceErrorAt { get; private set; }

    /// <summary>
    /// 持久化失败通知（UI 线程）。调用方无需处理异常：保存失败已记录日志并通过该事件提示用户，
    /// 关键路径（提醒展示/收尾）不会被落盘异常阻断。
    /// </summary>
    public event Action<string>? PersistenceFailed;

    /// <summary>全部数据落盘（任务结构变更时使用）。</summary>
    public bool Persist()
    {
        var tasks = PersistTasks();
        var settings = PersistSettings();
        return tasks && settings;
    }

    /// <summary>按域落盘：仅实例状态变更（收尾、错过判定、实例补齐）。</summary>
    public bool PersistOccurrences()
        => TrySave("实例数据", () => Store.SaveOccurrences(Data.Occurrences));

    /// <summary>按域落盘：任务结构变更（新增/编辑/删除/启停，通常伴随实例变更）。</summary>
    public bool PersistTasks()
        => TrySave("任务与实例数据", () =>
        {
            Store.SaveTasks(Data.Tasks);
            Store.SaveOccurrences(Data.Occurrences);
        });

    /// <summary>按域落盘：设置变更。</summary>
    public bool PersistSettings()
        => TrySave("设置", () => Store.SaveSettings(Data.Settings));

    private bool TrySave(string what, Action save)
    {
        try
        {
            save();
            return true;
        }
        catch (Exception ex)
        {
            LastPersistenceError = $"{what}保存失败：{ex.Message}";
            LastPersistenceErrorAt = DateTime.Now;
            Log.Error($"{what}保存失败", ex);
            PersistenceFailed?.Invoke($"{what}保存失败，本次变更可能无法保留：{ex.Message}");
            return false;
        }
    }
}
