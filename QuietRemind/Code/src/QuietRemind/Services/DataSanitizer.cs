using QuietRemind.Models;

namespace QuietRemind.Services;

/// <summary>
/// 可人工编辑 JSON 的数据边界归一化：把缺失/越界字段修正为可运行值，返回修正说明。
/// 目标是不因一条脏记录导致启动失败、提醒窗创建异常或任务永久静默不提醒。
/// </summary>
public static class DataSanitizer
{
    private static readonly int[] DefaultSnooze = [5, 10, 30];

    public static List<string> Normalize(AppData data)
    {
        var warnings = new List<string>();

        if (data.Tasks is null)
        {
            data.Tasks = [];
            warnings.Add("任务列表缺失，已按空处理");
        }
        if (data.Occurrences is null)
        {
            data.Occurrences = [];
            warnings.Add("实例列表缺失，已按空处理");
        }
        if (data.Settings is null)
        {
            data.Settings = new AppSettings();
            warnings.Add("设置对象缺失，已使用默认设置");
        }

        // 任务：过滤空记录，修正越界字段
        var tasks = data.Tasks.Where(t => t is not null).ToList();
        if (tasks.Count != data.Tasks.Count)
        {
            warnings.Add("任务列表包含空记录，已忽略");
            data.Tasks = tasks;
        }

        var taskIds = new HashSet<Guid>();
        foreach (var task in data.Tasks)
        {
            // 主键必须唯一且非空：重复 ID 会让两个任务共享实例、互相触发/收尾
            if (task.Id == Guid.Empty || !taskIds.Add(task.Id))
            {
                var oldId = task.Id;
                task.Id = Guid.NewGuid();
                taskIds.Add(task.Id);
                warnings.Add(oldId == Guid.Empty
                    ? $"任务「{task.Content}」缺少 ID，已重新分配"
                    : $"任务「{task.Content}」ID 与其他任务重复，已重新分配（其历史实例归属可能不准确）");
            }
            if (task.Content is null)
            {
                task.Content = "";
            }
            if (task.WeekDays is null)
            {
                task.WeekDays = [];
                warnings.Add($"任务「{task.Content}」缺少星期配置，已按空处理");
            }
            else
            {
                // 人工编辑/旧版本可能写入未定义的枚举值：过滤后再参与匹配与编辑器回填，
                // 避免编辑窗按 (int)DayOfWeek 索引时数组越界
                var validDays = task.WeekDays
                    .Where(d => Enum.IsDefined(typeof(DayOfWeek), d))
                    .Distinct()
                    .ToArray();
                if (validDays.Length != task.WeekDays.Length)
                {
                    warnings.Add($"任务「{task.Content}」星期配置包含无效值，已忽略");
                    task.WeekDays = validDays;
                }
            }
            if (!Enum.IsDefined(typeof(RecurrenceType), task.RecurrenceType))
            {
                warnings.Add($"任务「{task.Content}」循环规则无效，已改为每天");
                task.RecurrenceType = RecurrenceType.Daily;
            }
            if (task.Time < TimeSpan.Zero || task.Time >= TimeSpan.FromDays(1))
            {
                warnings.Add($"任务「{task.Content}」提醒时刻越界，已调整为 09:00");
                task.Time = new TimeSpan(9, 0, 0);
            }
            if (task.RecurrenceType == RecurrenceType.Weekly && task.WeekDays.Length == 0)
            {
                warnings.Add($"任务「{task.Content}」每周规则未勾选星期，将不会生成实例");
            }
            if (task.RecurrenceType == RecurrenceType.Once && task.SingleDate is null)
            {
                warnings.Add($"任务「{task.Content}」单次规则缺少日期，将不会生成实例");
            }
            if (task.MonthDay is < 1 or > 31)
            {
                warnings.Add($"任务「{task.Content}」每月日期越界，已调整为 1 日");
                task.MonthDay = 1;
            }
            if (task.Interval is < 1 or > 365)
            {
                warnings.Add($"任务「{task.Content}」重复间隔越界，已调整为 1");
                task.Interval = 1;
            }
            if (task.Month is < 1 or > 12)
            {
                warnings.Add($"任务「{task.Content}」每年月份越界，已调整为 1 月");
                task.Month = 1;
            }
            if (task.Interval > 1 && task.StartDate is null
                && task.RecurrenceType is RecurrenceType.Daily or RecurrenceType.Weekly or RecurrenceType.Monthly)
            {
                warnings.Add($"任务「{task.Content}」设置了重复间隔但缺少开始日期，本次按每 1 个周期提醒");
            }
            if (task.StartDate is { } startDate && task.EndDate is { } endDate && endDate < startDate)
            {
                warnings.Add($"任务「{task.Content}」结束日期早于开始日期，已调整为结束当天");
                task.EndDate = startDate;
            }
        }

        // 实例：过滤空记录/缺少计划时刻的记录，未知状态按待提醒处理（后续错过扫描会兜底）
        var occurrences = new List<Occurrence>(data.Occurrences.Count);
        var occurrenceIds = new HashSet<Guid>();
        foreach (var occurrence in data.Occurrences)
        {
            if (occurrence is null)
            {
                continue;
            }
            if (occurrence.TriggerAt == default)
            {
                warnings.Add("实例缺少计划时刻，已忽略该记录");
                continue;
            }
            // 实例 ID 是运行期“已弹窗”幂等键：重复会使一条提醒被另一条错误抑制
            if (occurrence.Id == Guid.Empty || !occurrenceIds.Add(occurrence.Id))
            {
                occurrence.Id = Guid.NewGuid();
                occurrenceIds.Add(occurrence.Id);
                warnings.Add("实例 ID 缺失或重复，已重新分配");
            }
            if (occurrence.OriginalTriggerAt == default)
            {
                occurrence.OriginalTriggerAt = occurrence.TriggerAt;
            }
            if (!Enum.IsDefined(typeof(OccurrenceState), occurrence.State))
            {
                warnings.Add("实例状态无效，已按待提醒处理");
                occurrence.State = OccurrenceState.Pending;
            }
            occurrences.Add(occurrence);
        }
        if (occurrences.Count != data.Occurrences.Count)
        {
            warnings.Add("实例列表包含空记录或无效记录，已忽略");
            data.Occurrences = occurrences;
        }

        // 设置：稍后提醒固定为 3 个候选，1~120 分钟
        var current = data.Settings.SnoozeMinutes;
        var normalized = new int[3];
        for (var i = 0; i < normalized.Length; i++)
        {
            var value = current is not null && i < current.Length ? current[i] : DefaultSnooze[i];
            normalized[i] = Math.Clamp(value, 1, 120);
        }
        if (current is null || current.Length != normalized.Length || !current.SequenceEqual(normalized))
        {
            warnings.Add("稍后提醒间隔配置无效，已归一化为 1~120 分钟的 3 个候选值");
            data.Settings.SnoozeMinutes = normalized;
        }

        var dim = Math.Clamp(data.Settings.ReminderDimPercent, 0, 90);
        if (dim != data.Settings.ReminderDimPercent)
        {
            warnings.Add("遮罩暗化程度越界，已调整到 0~90%");
            data.Settings.ReminderDimPercent = dim;
        }
        var blur = Math.Clamp(data.Settings.ReminderBlurRadius, 0, 40);
        if (blur != data.Settings.ReminderBlurRadius)
        {
            warnings.Add("背景模糊半径越界，已调整到 0~40");
            data.Settings.ReminderBlurRadius = blur;
        }
        var preAlert = Math.Clamp(data.Settings.PreAlertMinutes, 0, 120);
        if (preAlert != data.Settings.PreAlertMinutes)
        {
            warnings.Add("提前提醒分钟数越界，已调整到 0~120（0 = 关闭）");
            data.Settings.PreAlertMinutes = preAlert;
        }

        // 主题：越界枚举值或未知配色标识回退默认，避免启动时资源键缺失导致界面空白
        if (!Enum.IsDefined(data.Settings.ThemeMode))
        {
            warnings.Add("主题模式取值无效，已回退浅色");
            data.Settings.ThemeMode = ThemeMode.Light;
        }
        var accent = ThemeAccentCatalog.Normalize(data.Settings.ThemeAccent);
        if (!string.Equals(accent, data.Settings.ThemeAccent, StringComparison.Ordinal))
        {
            warnings.Add($"主题色「{data.Settings.ThemeAccent}」不可用，已回退默认配色");
            data.Settings.ThemeAccent = accent;
        }

        return warnings;
    }
}
