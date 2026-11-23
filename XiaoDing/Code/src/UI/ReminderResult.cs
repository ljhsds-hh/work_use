using System;
using XiaoDing.Core;

namespace XiaoDing.UI
{
    public enum ReminderActionKind
    {
        Complete = 0,
        Snooze = 1
    }

    public sealed class ReminderResult
    {
        private ReminderResult(ReminderActionKind kind, int minutes)
        {
            Kind = kind;
            Minutes = minutes;
        }

        public ReminderActionKind Kind { get; private set; }

        public int Minutes { get; private set; }

        public static ReminderResult Complete()
        {
            return new ReminderResult(ReminderActionKind.Complete, 0);
        }

        public static ReminderResult Snooze(int minutes)
        {
            return new ReminderResult(ReminderActionKind.Snooze, Math.Max(1, minutes));
        }
    }

    /// <summary>提醒文案格式化。</summary>
    internal static class ReminderText
    {
        public static string DescribeDue(TodoTask task, bool isLate)
        {
            DateTime dueLocal = DateTime.SpecifyKind(task.DueUtc, DateTimeKind.Utc).ToLocalTime();
            string due = DayLabel(dueLocal) + " " + dueLocal.ToString("HH:mm");

            DateTime now = DateTime.UtcNow;
            if (task.DueUtc > now)
            {
                return due + " · 还有 " + Humanize(task.DueUtc - now);
            }

            return due + (isLate ? " · 已错过" : " · 已到期");
        }

        public static string DescribeInterval(TimeSpan span)
        {
            return Humanize(span);
        }

        private static string Humanize(TimeSpan span)
        {
            int totalMinutes = (int)Math.Round(Math.Abs(span.TotalMinutes));
            if (totalMinutes < 1)
            {
                return "不到 1 分钟";
            }

            if (totalMinutes < 60)
            {
                return totalMinutes + " 分钟";
            }

            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;
            if (minutes == 0)
            {
                return hours + " 小时";
            }

            return hours + " 小时 " + minutes + " 分钟";
        }

        private static string DayLabel(DateTime local)
        {
            DateTime today = DateTime.Today;
            if (local.Date == today)
            {
                return "今天";
            }

            if (local.Date == today.AddDays(1))
            {
                return "明天";
            }

            if (local.Date == today.AddDays(-1))
            {
                return "昨天";
            }

            return local.ToString("M月d日");
        }
    }
}
