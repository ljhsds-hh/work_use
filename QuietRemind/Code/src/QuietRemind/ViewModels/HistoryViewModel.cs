using System.Collections.ObjectModel;
using QuietRemind.Models;
using QuietRemind.Services;

namespace QuietRemind.ViewModels;

/// <summary>历史记录窗口视图模型：只读展示单个任务最近的实例，不做统计与编辑。</summary>
public sealed class HistoryViewModel
{
    /// <summary>窗口只加载最近这么多条，避免长期使用后一次性创建大量行。</summary>
    public const int MaxRows = 200;

    public HistoryViewModel(AppServices services, ReminderTask task)
    {
        TitleText = $"历史记录 · {task.Content}";
        var all = services.Data.Occurrences
            .Where(o => o.TaskId == task.Id)
            .OrderByDescending(o => o.OriginalTriggerAt)
            .ToList();

        foreach (var occurrence in all.Take(MaxRows))
        {
            Items.Add(new HistoryRowViewModel(occurrence));
        }

        SummaryText = all.Count > MaxRows
            ? $"共 {all.Count} 条，显示最近 {MaxRows} 条"
            : $"共 {all.Count} 条";
        HasNoHistory = all.Count == 0;
    }

    public string TitleText { get; }
    public string SummaryText { get; }
    public bool HasNoHistory { get; }
    public ObservableCollection<HistoryRowViewModel> Items { get; } = [];
}

/// <summary>历史记录单行：计划时刻、状态、处理时间与推迟/补提醒说明。</summary>
public sealed class HistoryRowViewModel
{
    private readonly Occurrence _occurrence;

    public HistoryRowViewModel(Occurrence occurrence) => _occurrence = occurrence;

    public OccurrenceState State => _occurrence.State;

    public string PlannedText => _occurrence.OriginalTriggerAt.ToString("yyyy-MM-dd HH:mm");

    public string StateText => _occurrence.State switch
    {
        OccurrenceState.Pending => "待提醒",
        OccurrenceState.Missed => "已错过",
        OccurrenceState.Completed => "已完成",
        OccurrenceState.Skipped => "已跳过",
        _ => "未知",
    };

    public string SettledText => _occurrence.SettledAt?.ToString("yyyy-MM-dd HH:mm") ?? "—";

    public string NoteText
    {
        get
        {
            if (_occurrence.State == OccurrenceState.Pending
                && _occurrence.TriggerAt != _occurrence.OriginalTriggerAt)
            {
                return $"已推迟至 {_occurrence.TriggerAt:yyyy-MM-dd HH:mm}";
            }
            if (_occurrence.State == OccurrenceState.Missed
                && _occurrence.ReminderShownAt is { } shown)
            {
                return $"补提醒于 {shown:MM-dd HH:mm}";
            }
            if (_occurrence.State == OccurrenceState.Pending
                && _occurrence.ReminderShownAt is { } pendingShown)
            {
                return $"已提醒，待处理（{pendingShown:MM-dd HH:mm}）";
            }
            return "—";
        }
    }
}
