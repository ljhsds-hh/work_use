using QuietRemind.Services;

namespace QuietRemind.Models;

public class AppSettings
{
    /// <summary>开机自启动与进程守护（注册/注销计划任务）。</summary>
    public bool GuardEnabled { get; set; } = true;
    /// <summary>稍后再提醒候选间隔（分钟），提醒窗口提供这三个选择。</summary>
    public int[] SnoozeMinutes { get; set; } = [5, 10, 30];

    /// <summary>快速新增全局快捷键开关（Ctrl+Alt+N）。</summary>
    public bool QuickAddHotkeyEnabled { get; set; } = true;

    /// <summary>提前提醒分钟数（0 = 关闭）；仅托盘气泡，不改变全屏提醒行为。</summary>
    public int PreAlertMinutes { get; set; }

    /// <summary>提醒遮罩自定义背景图路径（null = 默认纯色底）；图片已拷贝到数据目录统一管理。</summary>
    public string? ReminderImagePath { get; set; }
    /// <summary>提醒遮罩暗化不透明度（0~90%，默认 66），叠加在背景之上保证卡片文字可读。</summary>
    public int ReminderDimPercent { get; set; } = 66;
    /// <summary>背景图片模糊半径（0~40px，默认 0 = 清晰）。</summary>
    public int ReminderBlurRadius { get; set; } = 0;

    /// <summary>界面明暗模式（默认浅色）。</summary>
    public ThemeMode ThemeMode { get; set; } = ThemeMode.Light;
    /// <summary>主题色标识（见 ThemeAccentCatalog；未知值回退默认松绿）。</summary>
    public string ThemeAccent { get; set; } = ThemeAccentCatalog.DefaultKey;

    /// <summary>上次成功规划实例的日期；用于识别系统日期回拨，避免重复生成已收尾日期。</summary>
    public DateOnly? LastPlannedDate { get; set; }
}
