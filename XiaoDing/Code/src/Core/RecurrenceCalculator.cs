using System;

namespace XiaoDing.Core
{
    /// <summary>
    /// 重复规则计算。按本地时间推进周期，避免夏令时切换导致的偏差。
    /// </summary>
    public static class RecurrenceCalculator
    {
        private const int MaxIterations = 800;

        /// <summary>
        /// 从当前到期时间推算下一个晚于 <paramref name="afterUtc"/> 的到期时间。
        /// 无重复规则时原样返回。
        /// </summary>
        public static DateTime NextAfter(
            DateTime dueUtc,
            RecurrenceKind kind,
            int weekdaysMask,
            int interval,
            DateTime afterUtc)
        {
            if (kind == RecurrenceKind.None)
            {
                return dueUtc;
            }

            if (kind == RecurrenceKind.Custom && weekdaysMask == 0)
            {
                return afterUtc.AddDays(1);
            }

            int step = kind == RecurrenceKind.EveryNDays ? Math.Max(1, interval) : 1;
            int monthDay = kind == RecurrenceKind.MonthlyDay ? Math.Max(1, Math.Min(31, interval)) : 1;

            DateTime cursor = ToLocal(dueUtc);

            for (int i = 0; i < MaxIterations; i++)
            {
                cursor = Advance(cursor, kind, weekdaysMask, step, monthDay);
                DateTime candidate = ToUtc(cursor);
                if (candidate > afterUtc)
                {
                    return candidate;
                }
            }

            // 理论上不会走到这里，作为兜底避免死循环。
            return afterUtc.AddDays(1);
        }

        private static DateTime Advance(DateTime cursor, RecurrenceKind kind, int weekdaysMask, int step, int monthDay)
        {
            switch (kind)
            {
                case RecurrenceKind.Daily:
                    return cursor.AddDays(1);

                case RecurrenceKind.Weekly:
                    return cursor.AddDays(7);

                case RecurrenceKind.EveryNDays:
                    return cursor.AddDays(step);

                case RecurrenceKind.Weekdays:
                    do
                    {
                        cursor = cursor.AddDays(1);
                    }
                    while (cursor.DayOfWeek == DayOfWeek.Saturday || cursor.DayOfWeek == DayOfWeek.Sunday);
                    return cursor;

                case RecurrenceKind.Custom:
                    do
                    {
                        cursor = cursor.AddDays(1);
                    }
                    while ((weekdaysMask & (1 << (int)cursor.DayOfWeek)) == 0);
                    return cursor;

                case RecurrenceKind.MonthlyDay:
                {
                    DateTime next = cursor.AddMonths(1);
                    int day = Math.Min(monthDay, DateTime.DaysInMonth(next.Year, next.Month));
                    return new DateTime(next.Year, next.Month, day, next.Hour, next.Minute, next.Second);
                }

                default:
                    return cursor.AddDays(1);
            }
        }

        private static DateTime ToLocal(DateTime utc)
        {
            return DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        }

        private static DateTime ToUtc(DateTime local)
        {
            return DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
        }

        /// <summary>把重复方式转成适合界面展示的文字。</summary>
        public static string Describe(RecurrenceKind kind, int weekdaysMask, int interval)
        {
            switch (kind)
            {
                case RecurrenceKind.Daily:
                    return "每天";
                case RecurrenceKind.Weekly:
                    return "每周";
                case RecurrenceKind.Weekdays:
                    return "工作日";
                case RecurrenceKind.Custom:
                    return DescribeWeekdays(weekdaysMask);
                case RecurrenceKind.MonthlyDay:
                    return "每月 " + Math.Max(1, Math.Min(31, interval)) + " 日";
                case RecurrenceKind.EveryNDays:
                    return "每 " + Math.Max(1, interval) + " 天";
                default:
                    return "不重复";
            }
        }

        private static string DescribeWeekdays(int mask)
        {
            if (mask == 0)
            {
                return "自定义";
            }

            string[] names = { "日", "一", "二", "三", "四", "五", "六" };
            System.Text.StringBuilder builder = new System.Text.StringBuilder("每周");
            for (int i = 0; i < names.Length; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    builder.Append(names[i]);
                }
            }

            return builder.ToString();
        }
    }
}
