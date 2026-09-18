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

    /// <summary>任意数据变更后统一落盘并刷新界面可读状态。</summary>
    public void Persist()
    {
        Store.SaveTasks(Data.Tasks);
        Store.SaveOccurrences(Data.Occurrences);
        Store.SaveSettings(Data.Settings);
    }
}
