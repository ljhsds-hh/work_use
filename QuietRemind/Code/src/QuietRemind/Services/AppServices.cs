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

    /// <summary>全部数据落盘（任务结构变更时使用）。</summary>
    public void Persist()
    {
        PersistTasks();
        PersistOccurrences();
        PersistSettings();
    }

    /// <summary>按域落盘：仅实例状态变更（收尾、错过判定、实例补齐）。</summary>
    public void PersistOccurrences() => Store.SaveOccurrences(Data.Occurrences);

    /// <summary>按域落盘：任务结构变更（新增/编辑/删除/启停，通常伴随实例变更）。</summary>
    public void PersistTasks()
    {
        Store.SaveTasks(Data.Tasks);
        Store.SaveOccurrences(Data.Occurrences);
    }

    /// <summary>按域落盘：设置变更。</summary>
    public void PersistSettings() => Store.SaveSettings(Data.Settings);
}
