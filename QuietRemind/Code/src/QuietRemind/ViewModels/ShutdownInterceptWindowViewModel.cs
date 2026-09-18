using System.Collections.ObjectModel;
using QuietRemind.Helpers;
using QuietRemind.Models;

namespace QuietRemind.ViewModels;

public sealed class ShutdownItemViewModel(string content, string timeText)
{
    public string Content { get; } = content;
    public string TimeText { get; } = timeText;
}

/// <summary>关机前拦截提醒窗口视图模型（需求 4 章）：逐条列出关机后将错过的任务。</summary>
public sealed class ShutdownInterceptWindowViewModel : ViewModelBase
{
    public ShutdownInterceptWindowViewModel(IEnumerable<(ReminderTask Task, Occurrence Occ)> entries)
    {
        foreach (var (task, occ) in entries)
        {
            Items.Add(new ShutdownItemViewModel(task.Content, occ.OriginalTriggerAt.ToString("yyyy-MM-dd HH:mm")));
        }
    }

    public ObservableCollection<ShutdownItemViewModel> Items { get; } = [];
}
