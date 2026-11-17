using System;

namespace MoneyCalc.Core
{
    /// <summary>日期推算：加天/月/年（月末对齐）、整月/整年差。</summary>
    public static class DateMath
    {
        /// <summary>加 N 个月，目标月没有这一天就落到月末（1-31 + 1 月 = 2-28/29）。</summary>
        public static DateTime AddMonths(DateTime date, int months)
        {
            return date.Date.AddMonths(months);
        }

        /// <summary>加 N 年，2-29 + 1 年 = 2-28。</summary>
        public static DateTime AddYears(DateTime date, int years)
        {
            return date.Date.AddYears(years);
        }

        public static DateTime AddDays(DateTime date, int days)
        {
            return date.Date.AddDays(days);
        }

        public static int DaysBetween(DateTime a, DateTime b)
        {
            return (int)(b.Date - a.Date).TotalDays;
        }

        /// <summary>
        /// 整月数。1-31 → 2-28 算 1 个整月（两边都是月末就算满月）。
        /// </summary>
        public static int WholeMonthsBetween(DateTime start, DateTime end)
        {
            var a = start.Date;
            var b = end.Date;
            int sign = 1;
            if (b < a)
            {
                var t = a; a = b; b = t;
                sign = -1;
            }

            int months = (b.Year - a.Year) * 12 + (b.Month - a.Month);
            if (b.Day < a.Day && !(IsLastDayOfMonth(a) && IsLastDayOfMonth(b))) months--;
            return months * sign;
        }

        /// <summary>整年数。2-29 → 次年 2-28 算 1 年。</summary>
        public static int WholeYearsBetween(DateTime start, DateTime end)
        {
            var a = start.Date;
            var b = end.Date;
            int sign = 1;
            if (b < a)
            {
                var t = a; a = b; b = t;
                sign = -1;
            }

            int years = b.Year - a.Year;
            if (b.Month < a.Month || (b.Month == a.Month && b.Day < a.Day))
            {
                if (!(IsLastDayOfMonth(a) && IsLastDayOfMonth(b))) years--;
            }
            return years * sign;
        }

        public static bool IsLastDayOfMonth(DateTime date)
        {
            return date.Day == DateTime.DaysInMonth(date.Year, date.Month);
        }

        /// <summary>「3 年 2 个月 5 天」这种，给界面显示。</summary>
        public static string DescribeSpan(DateTime start, DateTime end)
        {
            var a = start.Date;
            var b = end.Date;
            bool negative = b < a;
            if (negative) { var t = a; a = b; b = t; }

            int years = WholeYearsBetween(a, b);
            var afterYears = AddYears(a, years);
            int months = WholeMonthsBetween(afterYears, b);
            var afterMonths = AddMonths(afterYears, months);
            int days = DaysBetween(afterMonths, b);

            string text = years + " 年 " + months + " 个月 " + days + " 天";
            return negative ? "负 " + text : text;
        }
    }
}
