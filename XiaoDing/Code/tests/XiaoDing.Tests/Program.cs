using System;
using XiaoDing.Core;

namespace XiaoDing.Tests
{
    /// <summary>
    /// 零依赖控制台测试：覆盖快速添加的时间识别与重复规则推进。
    /// 全部用固定基准时间，断言失败时退出码非 0。
    /// </summary>
    internal static class Program
    {
        private static int _failed;
        private static int _passed;

        /// <summary>基准时间：2026-11-23（周一）10:00 本地时间。</summary>
        private static readonly DateTime Now = new DateTime(2026, 11, 23, 10, 0, 0);

        private static int Main()
        {
            QuickParseTests();
            RecurrenceTests();

            Console.WriteLine();
            Console.WriteLine("通过 " + _passed + " 项，失败 " + _failed + " 项。");
            return _failed == 0 ? 0 : 1;
        }

        // ---------- 快速添加 ----------

        private static void QuickParseTests()
        {
            Console.WriteLine("== QuickParse ==");

            // README 里的输入样例
            CheckParse("明天9点 交周报", "交周报", string.Empty, Local(2026, 11, 24, 9, 0), true);
            CheckParse("周五下午3点半 开会", "开会", string.Empty, Local(2026, 11, 27, 15, 30), true);
            CheckParse("12月5日 还信用卡", "还信用卡", string.Empty, Local(2026, 12, 5, 9, 0), false);
            CheckParse("9:30 晨会", "晨会", string.Empty, Local(2026, 11, 24, 9, 30), true);
            CheckParse("交周报", "交周报", string.Empty, Local(2026, 11, 23, 11, 0), false);
            CheckParse("明天9点 交周报 #工作", "交周报", "工作", Local(2026, 11, 24, 9, 0), true);
            CheckParse("明天9点", "未命名任务", string.Empty, Local(2026, 11, 24, 9, 0), true);

            // 回归：「小时」不能被当成钟点消费
            CheckParse("2小时后 开会", "2小时后 开会", string.Empty, Local(2026, 11, 23, 11, 0), false);

            // 「9时」这种正常钟点写法仍要识别
            CheckParse("9时 会议", "会议", string.Empty, Local(2026, 11, 24, 9, 0), true);

            // 回归：「上周六」是过去时间，整段留在标题里，不误解析
            CheckParse("上周六 还书", "上周六 还书", string.Empty, Local(2026, 11, 23, 11, 0), false);

            // 回归：显式年份被整体消费，不残留在标题里
            CheckParse("2026年12月5日 还信用卡", "还信用卡", string.Empty, Local(2026, 12, 5, 9, 0), false);

            // 显式年份已过去：不消费该片段，整段留在标题里
            CheckParse("2025年1月5日 还信用卡", "2025年1月5日 还信用卡", string.Empty, Local(2026, 11, 23, 11, 0), false);
        }

        private static void CheckParse(string input, string title, string group, DateTime dueLocal, bool hasTime)
        {
            QuickParseResult result = QuickParse.Parse(input, Now);
            bool ok = result.Title == title
                && result.Group == group
                && result.DueLocal == dueLocal
                && result.HasExplicitTime == hasTime;
            Check(ok,
                "解析「" + input + "」",
                "标题「" + title + "」/分组「" + group + "」/" + dueLocal.ToString("yyyy-MM-dd HH:mm") + "/显式时间=" + hasTime,
                "标题「" + result.Title + "」/分组「" + result.Group + "」/" + result.DueLocal.ToString("yyyy-MM-dd HH:mm") + "/显式时间=" + result.HasExplicitTime);
        }

        // ---------- 重复规则 ----------

        private static void RecurrenceTests()
        {
            Console.WriteLine("== RecurrenceCalculator ==");

            CheckLocal("不重复原样返回",
                Local(2026, 11, 23, 9, 0),
                RecurrenceCalculator.NextAfter(Utc(2026, 11, 23, 9, 0), RecurrenceKind.None, 0, 0, Utc(2026, 11, 23, 10, 0)));

            CheckLocal("每天",
                Local(2026, 11, 24, 9, 0),
                RecurrenceCalculator.NextAfter(Utc(2026, 11, 23, 9, 0), RecurrenceKind.Daily, 0, 0, Utc(2026, 11, 23, 10, 0)));

            CheckLocal("每周",
                Local(2026, 11, 30, 9, 0),
                RecurrenceCalculator.NextAfter(Utc(2026, 11, 23, 9, 0), RecurrenceKind.Weekly, 0, 0, Utc(2026, 11, 23, 10, 0)));

            CheckLocal("工作日（周五跳到下周一）",
                Local(2026, 11, 30, 9, 0),
                RecurrenceCalculator.NextAfter(Utc(2026, 11, 27, 9, 0), RecurrenceKind.Weekdays, 0, 0, Utc(2026, 11, 27, 10, 0)));

            // 自定义星期：周一 + 周三（bit1=周一、bit3=周三）
            CheckLocal("自定义星期（周一、周三）",
                Local(2026, 11, 25, 9, 0),
                RecurrenceCalculator.NextAfter(Utc(2026, 11, 23, 9, 0), RecurrenceKind.Custom, (1 << 1) | (1 << 3), 0, Utc(2026, 11, 23, 10, 0)));

            CheckLocal("每月 31 日（2 月夹到 28）",
                Local(2026, 2, 28, 9, 0),
                RecurrenceCalculator.NextAfter(Utc(2026, 1, 31, 9, 0), RecurrenceKind.MonthlyDay, 0, 31, Utc(2026, 2, 1, 0, 0)));

            CheckLocal("每 3 天",
                Local(2026, 11, 26, 9, 0),
                RecurrenceCalculator.NextAfter(Utc(2026, 11, 23, 9, 0), RecurrenceKind.EveryNDays, 0, 3, Utc(2026, 11, 23, 10, 0)));

            // 自定义星期但没勾任何天：当前兜底是「基准 +1 天」
            CheckLocal("自定义星期为空的兜底",
                Local(2026, 11, 24, 10, 0),
                RecurrenceCalculator.NextAfter(Utc(2026, 11, 23, 9, 0), RecurrenceKind.Custom, 0, 0, Utc(2026, 11, 23, 10, 0)));

            Check(RecurrenceCalculator.Describe(RecurrenceKind.Weekdays, 0, 0) == "工作日",
                "描述-工作日",
                "工作日",
                RecurrenceCalculator.Describe(RecurrenceKind.Weekdays, 0, 0));
            Check(RecurrenceCalculator.Describe(RecurrenceKind.MonthlyDay, 0, 31) == "每月 31 日",
                "描述-每月 31 日",
                "每月 31 日",
                RecurrenceCalculator.Describe(RecurrenceKind.MonthlyDay, 0, 31));
        }

        private static void CheckLocal(string name, DateTime expectedLocal, DateTime actualUtc)
        {
            DateTime actualLocal = DateTime.SpecifyKind(actualUtc, DateTimeKind.Utc).ToLocalTime();
            Check(actualLocal == expectedLocal,
                name,
                expectedLocal.ToString("yyyy-MM-dd HH:mm"),
                actualLocal.ToString("yyyy-MM-dd HH:mm"));
        }

        // ---------- 断言与时间辅助 ----------

        private static void Check(bool condition, string name, string expected, string actual)
        {
            if (condition)
            {
                _passed++;
                Console.WriteLine("  [通过] " + name);
            }
            else
            {
                _failed++;
                Console.WriteLine("  [失败] " + name);
                Console.WriteLine("        期望: " + expected);
                Console.WriteLine("        实际: " + actual);
            }
        }

        private static DateTime Local(int year, int month, int day, int hour, int minute)
        {
            return new DateTime(year, month, day, hour, minute, 0);
        }

        private static DateTime Utc(int year, int month, int day, int hour, int minute)
        {
            return DateTime.SpecifyKind(Local(year, month, day, hour, minute), DateTimeKind.Local).ToUniversalTime();
        }
    }
}
