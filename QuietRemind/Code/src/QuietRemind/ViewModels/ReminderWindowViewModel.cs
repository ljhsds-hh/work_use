using System.Collections.ObjectModel;
using QuietRemind.Helpers;
using QuietRemind.Models;
using QuietRemind.Services;

namespace QuietRemind.ViewModels;

/// <summary>全屏提醒窗口视图模型：合并展示多条到期实例，逐条收尾，全部收尾后关闭（需求 3.4）。</summary>
public sealed class ReminderWindowViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public ReminderWindowViewModel(AppServices services, IEnumerable<(ReminderTask Task, Occurrence Occ)> entries)
    {
        _services = services;
        foreach (var (task, occ) in entries)
        {
            var item = new ReminderItemViewModel(task, occ, services.Engine, services.Data.Settings.SnoozeMinutes);
            item.Settled += OnItemSettled;
            Items.Add(item);
        }
    }

    public ObservableCollection<ReminderItemViewModel> Items { get; } = [];

    public bool IsMissedWindow => Items.Any(i => i.IsMissed);

    /// <summary>全部条目收尾完成，窗口应关闭。</summary>
    public event Action? AllSettled;

    private void OnItemSettled(ReminderItemViewModel item)
    {
        // 收尾即实时落盘（需求 8.2）
        _services.Persist();
        _services.Log.Info($"提醒收尾：任务「{item.Content}」计划 {item.TimeText}（状态 {item.Occurrence.State}）");
        if (Items.All(i => i.IsSettled))
        {
            AllSettled?.Invoke();
        }
    }
}
