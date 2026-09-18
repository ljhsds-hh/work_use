namespace QuietRemind.Models;

/// <summary>应用内存数据全集的载体，由 JsonStore 统一持久化。</summary>
public class AppData
{
    public List<ReminderTask> Tasks { get; set; } = [];
    public List<Occurrence> Occurrences { get; set; } = [];
    public AppSettings Settings { get; set; } = new();
}
