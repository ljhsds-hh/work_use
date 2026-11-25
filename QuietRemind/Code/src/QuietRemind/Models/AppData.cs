namespace QuietRemind.Models;

/// <summary>应用内存数据全集的载体，由 JsonStore 统一持久化。</summary>
public class AppData
{
    public List<ReminderTask> Tasks { get; set; } = [];
    public List<Occurrence> Occurrences { get; set; } = [];
    public AppSettings Settings { get; set; } = new();

    /// <summary>
    /// 整体接管另一份数据（导入备份后使用）：替换引用而非复制，
    /// 保证后续所有保存都基于导入后的数据，部分保存失败也不会被旧内存数据回写覆盖。
    /// </summary>
    public void ReplaceWith(AppData source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Tasks = source.Tasks ?? [];
        Occurrences = source.Occurrences ?? [];
        Settings = source.Settings ?? new AppSettings();
    }
}
