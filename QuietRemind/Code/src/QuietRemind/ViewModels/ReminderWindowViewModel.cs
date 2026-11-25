using System.Collections.ObjectModel;
using QuietRemind.Helpers;
using QuietRemind.Models;
using QuietRemind.Services;

namespace QuietRemind.ViewModels;

/// <summary>提醒浮层窗口视图模型：合并展示多条到期实例，逐条收尾，全部收尾后关闭（需求 3.4）。</summary>
public sealed class ReminderWindowViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private string _headerTime = "--:--";

    public ReminderWindowViewModel(AppServices services, IEnumerable<(ReminderTask Task, Occurrence Occ)> entries,
        bool isPreview = false)
    {
        _services = services;
        IsPreview = isPreview;
        AddEntries(entries);
    }

    /// <summary>预览模式：同一套主屏全屏样式，但不写数据、不要求逐条收尾。</summary>
    public bool IsPreview { get; }

    /// <summary>底部说明：预览模式明确提示不会落盘。</summary>
    public string FooterText => IsPreview
        ? "预览模式：只展示提醒样式，不会写入任何数据"
        : "请对每条任务做出处理（完成 / 稍后再提醒 / 跳过）";

    public ObservableCollection<ReminderItemViewModel> Items { get; } = [];

    /// <summary>头部展示的计划时刻：最早一条未处理提醒的 HH:mm。</summary>
    public string HeaderTime
    {
        get => _headerTime;
        private set => SetProperty(ref _headerTime, value);
    }

    /// <summary>设置访问（提醒窗应用背景/暗化/模糊配置）。</summary>
    public AppSettings Settings => _services.Data.Settings;

    /// <summary>全部条目收尾完成，窗口应关闭。</summary>
    public event Action? AllSettled;

    /// <summary>向已打开的提醒窗追加新到期条目（避免多个全屏窗堆叠）。</summary>
    public void AddEntries(IEnumerable<(ReminderTask Task, Occurrence Occ)> entries)
    {
        var added = false;
        foreach (var (task, occ) in entries)
        {
            var item = new ReminderItemViewModel(task, occ, _services.Engine,
                _services.Data.Settings.SnoozeMinutes, IsPreview);
            item.Settled += OnItemSettled;
            Items.Add(item);
            added = true;
        }
        if (added)
        {
            UpdateHeaderTime();
        }
    }

    private void UpdateHeaderTime()
    {
        var earliest = Items.Select(i => i.Occurrence.OriginalTriggerAt).OrderBy(t => t).FirstOrDefault();
        HeaderTime = earliest == default ? "--:--" : earliest.ToString("HH:mm");
    }

    private void OnItemSettled(ReminderItemViewModel item)
    {
        // 收尾即实时落盘（需求 8.2，按域仅实例文件）。
        // 落盘失败由 AppServices 捕获并提示，绝不阻断这里的窗口关闭判断。
        _services.PersistOccurrences();
        _services.Log.Info($"提醒收尾：任务「{item.Content}」计划 {item.TimeText}（状态 {item.Occurrence.State}）");
        if (Items.All(i => i.IsSettled))
        {
            AllSettled?.Invoke();
        }
    }
}
