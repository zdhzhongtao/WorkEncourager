using System;
using System.Collections.Generic;
using System.Globalization;

namespace MoneyCalc.Core
{
    /// <summary>
    /// 工作日日历：法定节假日放假，周末默认不上班，调休补班照常上班。
    /// 节假日表可以编辑，也可以一键清空只按周一到周五算。
    /// </summary>
    public sealed class HolidayCalendar
    {
        /// <summary>法定节假日（即使落在工作日也放假）。</summary>
        public HashSet<DateTime> Holidays { get; } = new HashSet<DateTime>();

        /// <summary>调休补班（即使落在周末也上班）。</summary>
        public HashSet<DateTime> MakeupWorkdays { get; } = new HashSet<DateTime>();

        /// <summary>数据说明，界面上要显示出来提醒用户可能过期。</summary>
        public string DataNote { get; set; } = "只用周一到周五";

        public bool IsWorkday(DateTime day)
        {
            var d = day.Date;
            if (MakeupWorkdays.Contains(d)) return true;
            if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) return false;
            return !Holidays.Contains(d);
        }

        /// <summary>
        /// 两日期之间的工作日数。含端点 / 不含端点两种口径。
        /// 反向（end 早于 start）返回负数，方便界面提示「你填反了」。
        /// </summary>
        public int CountWorkdays(DateTime start, DateTime end, bool includeEndpoints)
        {
            var a = start.Date;
            var b = end.Date;

            if (a == b) return includeEndpoints ? (IsWorkday(a) ? 1 : 0) : 0;

            int sign = 1;
            if (b < a)
            {
                var t = a; a = b; b = t;
                sign = -1;
            }

            if (!includeEndpoints)
            {
                a = a.AddDays(1);
                b = b.AddDays(-1);
            }

            if (b < a) return 0;

            int count = 0;
            for (var d = a; d <= b; d = d.AddDays(1))
                if (IsWorkday(d)) count++;
            return count * sign;
        }

        /// <summary>
        /// 从 start 起算 N 个工作日后的日期。
        /// includeStart = true 表示「起始日本身算第一个工作日」。
        /// N 为 0 时直接返回起始日；N 为负表示往前推。
        /// 如果日历被填成了一天不落全是节假日，不会死循环，而是抛 <see cref="FriendlyException"/>。
        /// </summary>
        public DateTime AddWorkdays(DateTime start, int workdays, bool includeStart)
        {
            if (workdays == 0) return start.Date;

            int sign = workdays > 0 ? 1 : -1;
            int remaining = Math.Abs(workdays);
            var current = start.Date;

            // 最多往后翻 50 年（再按要算的天数放宽）。正常日历几十天就够，
            // 翻到 50 年说明配置有问题，与其悄悄返回 2101 年某天，不如直接告诉用户。
            int budget = 366 * 50 + remaining * 3;

            if (!includeStart) current = current.AddDays(sign);
            else
            {
                // 含起始日：先把「起点不是工作日」这种情况跳过去
                while (!IsWorkday(current))
                {
                    current = current.AddDays(sign);
                    if (--budget <= 0) throw TooFewWorkdays();
                }
            }

            while (true)
            {
                if (IsWorkday(current))
                {
                    remaining--;
                    if (remaining == 0) return current;
                }
                current = current.AddDays(sign);
                if (--budget <= 0) throw TooFewWorkdays();
            }
        }

        private static FriendlyException TooFewWorkdays()
        {
            return new FriendlyException("这个日历里几乎每天都排成了节假日，往后翻 50 年也找不到工作日。请检查节假日设置，或者点「恢复内置」重来。");
        }

        public static string WeekdayName(DateTime day)
        {
            switch (day.DayOfWeek)
            {
                case DayOfWeek.Monday: return "星期一";
                case DayOfWeek.Tuesday: return "星期二";
                case DayOfWeek.Wednesday: return "星期三";
                case DayOfWeek.Thursday: return "星期四";
                case DayOfWeek.Friday: return "星期五";
                case DayOfWeek.Saturday: return "星期六";
                default: return "星期日";
            }
        }

        public static HolidayCalendar WeekdaysOnly()
        {
            return new HolidayCalendar { DataNote = "只用周一到周五（不含法定节假日）" };
        }

        /// <summary>内置 2025 / 2026 的中国法定节假日与调休。截止 2026-11。</summary>
        public static HolidayCalendar BuiltIn2025And2026()
        {
            var c = new HolidayCalendar { DataNote = "内置 2025–2026 节假日，截止 2026-11，可能已过期，请自行核对" };

            void Holiday(int y, int m, int from, int to)
            {
                for (int d = from; d <= to; d++) c.Holidays.Add(new DateTime(y, m, d));
            }

            // ---- 2025 ----
            c.Holidays.Add(new DateTime(2025, 1, 1));
            Holiday(2025, 1, 28, 31); Holiday(2025, 2, 1, 4);      // 春节 1/28–2/4
            c.MakeupWorkdays.Add(new DateTime(2025, 1, 26));
            c.MakeupWorkdays.Add(new DateTime(2025, 2, 8));
            Holiday(2025, 4, 4, 6);                                 // 清明
            Holiday(2025, 5, 1, 5);                                 // 劳动节
            c.MakeupWorkdays.Add(new DateTime(2025, 4, 27));
            Holiday(2025, 5, 31, 31); Holiday(2025, 6, 1, 2);       // 端午
            Holiday(2025, 10, 1, 8);                                // 国庆 + 中秋
            c.MakeupWorkdays.Add(new DateTime(2025, 9, 28));
            c.MakeupWorkdays.Add(new DateTime(2025, 10, 11));

            // ---- 2026（按已公布的安排整理，如有调整请以官方为准）----
            Holiday(2026, 1, 1, 3);                                 // 元旦
            Holiday(2026, 2, 16, 22);                               // 春节（除夕 2/16）
            c.MakeupWorkdays.Add(new DateTime(2026, 2, 14));
            c.MakeupWorkdays.Add(new DateTime(2026, 2, 28));
            Holiday(2026, 4, 4, 6);                                 // 清明
            Holiday(2026, 5, 1, 5);                                 // 劳动节
            c.MakeupWorkdays.Add(new DateTime(2026, 5, 9));
            Holiday(2026, 6, 19, 21);                               // 端午
            Holiday(2026, 9, 25, 27);                               // 中秋
            Holiday(2026, 10, 1, 7);                                // 国庆
            c.MakeupWorkdays.Add(new DateTime(2026, 9, 19));
            c.MakeupWorkdays.Add(new DateTime(2026, 10, 10));

            return c;
        }

        /// <summary>「2026-10-01」这样的字符串 ↔ 日期，给节假日编辑用。</summary>
        public static bool TryParseDate(string text, out DateTime date)
        {
            return DateTime.TryParse((text ?? string.Empty).Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out date);
        }
    }
}
