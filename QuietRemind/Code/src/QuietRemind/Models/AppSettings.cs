namespace QuietRemind.Models;

public class AppSettings
{
    /// <summary>开机自启动与进程守护（注册/注销计划任务）。</summary>
    public bool GuardEnabled { get; set; } = true;
    /// <summary>稍后再提醒候选间隔（分钟），提醒窗口提供这三个选择。</summary>
    public int[] SnoozeMinutes { get; set; } = [5, 10, 30];
}
