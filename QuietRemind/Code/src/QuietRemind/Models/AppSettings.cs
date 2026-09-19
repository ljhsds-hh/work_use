namespace QuietRemind.Models;

public class AppSettings
{
    /// <summary>开机自启动与进程守护（注册/注销计划任务）。</summary>
    public bool GuardEnabled { get; set; } = true;
    /// <summary>稍后再提醒候选间隔（分钟），提醒窗口提供这三个选择。</summary>
    public int[] SnoozeMinutes { get; set; } = [5, 10, 30];

    /// <summary>提醒遮罩自定义背景图路径（null = 默认纯色底）；图片已拷贝到数据目录统一管理。</summary>
    public string? ReminderImagePath { get; set; }
    /// <summary>提醒遮罩暗化不透明度（0~90%，默认 66），叠加在背景之上保证卡片文字可读。</summary>
    public int ReminderDimPercent { get; set; } = 66;
    /// <summary>背景图片模糊半径（0~40px，默认 0 = 清晰）。</summary>
    public int ReminderBlurRadius { get; set; } = 0;
}
