using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace XiaoDing.Core
{
    public sealed class QuickParseResult
    {
        public string Title { get; set; }
        public string Group { get; set; }
        public DateTime DueLocal { get; set; }
        public bool HasExplicitTime { get; set; }
        public string Summary { get; set; }
    }

    /// <summary>
    /// 快速添加的轻量时间识别。只处理常见中文写法，不依赖任何模型：
    /// 今天/明天/后天、周X/下周X、X月X日、X点/X点半/X:XX，以及 上午/下午/晚上 等修饰。
    /// </summary>
    public static class QuickParse
    {
        private static readonly Regex GroupRegex = new Regex(@"#([^\s#]+)", RegexOptions.Compiled);
        private static readonly Regex DayKeywordRegex = new Regex(@"(大后天|后天|明天|今天)", RegexOptions.Compiled);
        // 「(?<!上个?)」排除「上周六」「上个星期五」这类过去时间，不误吃星期片段、不残留在标题里。
        private static readonly Regex WeekdayRegex = new Regex(
            @"(?<!上个?)(下下周|下周|本周|这周|下个?星期|这个?星期|下个?礼拜|这个?礼拜|星期|礼拜|周)([一二三四五六日天1-7])",
            RegexOptions.Compiled);
        private static readonly Regex MonthDayRegex = new Regex(@"(?:(\d{4})年)?(\d{1,2})月(\d{1,2})[日号]?", RegexOptions.Compiled);
        // 「(?<!小)时」避免把「2小时后」里的「小时」当成钟点消费。
        private static readonly Regex TimeRegex = new Regex(
            @"(凌晨|早上|上午|中午|下午|傍晚|晚上)?\s*(\d{1,2})\s*(?:[点:：]|(?<!小)时)\s*(\d{1,2})?\s*(分|半)?",
            RegexOptions.Compiled);
        private static readonly Regex SpaceRegex = new Regex(@"\s{2,}", RegexOptions.Compiled);

        public static QuickParseResult Parse(string input, DateTime now)
        {
            QuickParseResult result = new QuickParseResult();
            result.Title = (input ?? string.Empty).Trim();
            result.Group = string.Empty;

            if (result.Title.Length == 0)
            {
                result.DueLocal = now.AddHours(1);
                result.Summary = Describe(result.DueLocal, string.Empty);
                return result;
            }

            List<KeyValuePair<int, int>> spans = new List<KeyValuePair<int, int>>();

            Match groupMatch = GroupRegex.Match(result.Title);
            if (groupMatch.Success)
            {
                result.Group = groupMatch.Groups[1].Value;
                spans.Add(new KeyValuePair<int, int>(groupMatch.Index, groupMatch.Length));
            }

            DateTime date = now.Date;
            bool hasDate = false;

            Match monthDay = MonthDayRegex.Match(result.Title);
            if (monthDay.Success)
            {
                int month = int.Parse(monthDay.Groups[2].Value);
                int day = int.Parse(monthDay.Groups[3].Value);
                int year = 0;
                if (monthDay.Groups[1].Success)
                {
                    int.TryParse(monthDay.Groups[1].Value, out year);
                }

                // 只接受真实存在的日期：「2月30日」这类不消费该片段，避免被悄悄夹到 28 号。
                if (month >= 1 && month <= 12 && day >= 1)
                {
                    if (year >= 1 && year <= 9999)
                    {
                        // 显式年份原样使用，不做跨年滚动；已经过去则不消费该片段。
                        if (day <= DateTime.DaysInMonth(year, month))
                        {
                            DateTime candidate = new DateTime(year, month, day);
                            if (candidate >= now.Date)
                            {
                                date = candidate;
                                hasDate = true;
                                spans.Add(new KeyValuePair<int, int>(monthDay.Index, monthDay.Length));
                            }
                        }
                    }
                    else if (day <= DateTime.DaysInMonth(now.Year, month))
                    {
                        DateTime candidate = new DateTime(now.Year, month, day);
                        if (candidate < now.Date)
                        {
                            int nextYear = now.Year + 1;
                            if (day <= DateTime.DaysInMonth(nextYear, month))
                            {
                                candidate = new DateTime(nextYear, month, day);
                            }
                        }

                        if (candidate >= now.Date)
                        {
                            date = candidate;
                            hasDate = true;
                            spans.Add(new KeyValuePair<int, int>(monthDay.Index, monthDay.Length));
                        }
                    }
                }
            }

            if (!hasDate)
            {
                Match weekday = WeekdayRegex.Match(result.Title);
                if (weekday.Success && IsWeekdayMatchValid(result.Title, weekday))
                {
                    int target = WeekdayIndex(weekday.Groups[2].Value);
                    if (target >= 0)
                    {
                        int delta = target - (int)now.DayOfWeek;
                        if (delta < 0)
                        {
                            delta += 7;
                        }

                        DateTime candidate = now.Date.AddDays(delta);
                        string prefix = weekday.Groups[1].Value;
                        if (prefix == "下周" || prefix == "下个星期" || prefix == "下个礼拜" || prefix == "下下周")
                        {
                            candidate = candidate.AddDays(prefix == "下下周" ? 14 : 7);
                        }

                        date = candidate;
                        hasDate = true;
                        spans.Add(new KeyValuePair<int, int>(weekday.Index, weekday.Length));
                    }
                }
            }

            if (!hasDate)
            {
                Match dayKeyword = DayKeywordRegex.Match(result.Title);
                if (dayKeyword.Success)
                {
                    switch (dayKeyword.Groups[1].Value)
                    {
                        case "今天":
                            date = now.Date;
                            break;
                        case "明天":
                            date = now.Date.AddDays(1);
                            break;
                        case "后天":
                            date = now.Date.AddDays(2);
                            break;
                        case "大后天":
                            date = now.Date.AddDays(3);
                            break;
                    }

                    hasDate = true;
                    spans.Add(new KeyValuePair<int, int>(dayKeyword.Index, dayKeyword.Length));
                }
            }

            bool hasTime = false;
            int hour = 0;
            int minute = 0;

            Match time = TimeRegex.Match(result.Title);
            if (time.Success)
            {
                hour = int.Parse(time.Groups[2].Value);
                if (time.Groups[3].Success && time.Groups[3].Value.Length > 0)
                {
                    minute = int.Parse(time.Groups[3].Value);
                }
                else if (time.Groups[4].Success && time.Groups[4].Value == "半")
                {
                    minute = 30;
                }

                if (minute > 59)
                {
                    minute = 0;
                }

                string modifier = time.Groups[1].Success ? time.Groups[1].Value : string.Empty;
                hour = ApplyModifier(hour, modifier);

                if (hour >= 0 && hour <= 23)
                {
                    hasTime = true;
                    spans.Add(new KeyValuePair<int, int>(time.Index, time.Length));
                }
            }

            result.HasExplicitTime = hasTime;

            if (!hasDate && !hasTime)
            {
                result.DueLocal = now.AddHours(1);
            }
            else if (!hasTime)
            {
                result.DueLocal = date.AddHours(9);
            }
            else
            {
                DateTime due = date.AddHours(hour).AddMinutes(minute);
                if (!hasDate && due <= now)
                {
                    due = due.AddDays(1);
                }

                result.DueLocal = due;
            }

            result.Title = RemoveSpans(result.Title, spans);
            if (result.Title.Length == 0)
            {
                result.Title = "未命名任务";
            }

            result.Summary = Describe(result.DueLocal, result.Group);
            return result;
        }

        private static int ApplyModifier(int hour, string modifier)
        {
            if (hour > 12)
            {
                return hour;
            }

            switch (modifier)
            {
                case "下午":
                case "傍晚":
                    return hour < 12 ? hour + 12 : hour;
                case "晚上":
                case "半夜":
                    // 「晚上12点」口语指午夜 0 点。
                    return hour == 12 ? 0 : hour + 12;
                case "中午":
                    return hour < 12 ? hour + 12 : hour;
                case "凌晨":
                case "上午":
                case "早上":
                case "清晨":
                    // 「上午12点」按 0 点处理，12 点整应说「中午12点」。
                    return hour == 12 ? 0 : hour;
                default:
                    return hour;
            }
        }

        /// <summary>排除「每周一次」「每周1次」这类间隔描述，避免被误判成周一。</summary>
        private static bool IsWeekdayMatchValid(string input, Match match)
        {
            int next = match.Index + match.Length;
            return next >= input.Length || input[next] != '次';
        }

        private static int WeekdayIndex(string token)
        {
            switch (token)
            {
                case "一": return 1;
                case "二": return 2;
                case "三": return 3;
                case "四": return 4;
                case "五": return 5;
                case "六": return 6;
                case "日":
                case "天":
                case "7": return 0;
                case "1": return 1;
                case "2": return 2;
                case "3": return 3;
                case "4": return 4;
                case "5": return 5;
                case "6": return 6;
                default: return -1;
            }
        }

        private static string RemoveSpans(string input, List<KeyValuePair<int, int>> spans)
        {
            if (spans.Count == 0)
            {
                return input.Trim();
            }

            spans.Sort(delegate(KeyValuePair<int, int> left, KeyValuePair<int, int> right)
            {
                return left.Key.CompareTo(right.Key);
            });

            StringBuilder builder = new StringBuilder();
            int cursor = 0;
            foreach (KeyValuePair<int, int> span in spans)
            {
                if (span.Key < cursor)
                {
                    continue;
                }

                if (span.Key > cursor)
                {
                    builder.Append(input, cursor, span.Key - cursor);
                }

                cursor = span.Key + span.Value;
            }

            if (cursor < input.Length)
            {
                builder.Append(input, cursor, input.Length - cursor);
            }

            return SpaceRegex.Replace(builder.ToString(), " ").Trim();
        }

        private static string Describe(DateTime due, string group)
        {
            DateTime today = DateTime.Today;
            string day;
            if (due.Date == today)
            {
                day = "今天";
            }
            else if (due.Date == today.AddDays(1))
            {
                day = "明天";
            }
            else
            {
                day = due.ToString("M月d日");
            }

            string text = day + " " + due.ToString("HH:mm");
            if (!string.IsNullOrEmpty(group))
            {
                text += " · " + group;
            }

            return text;
        }
    }
}
