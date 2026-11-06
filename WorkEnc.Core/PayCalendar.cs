using System;
using System.Collections.Generic;

namespace WorkEnc.Core
{
    /// <summary>
    /// 法定节假日日历（依据《国务院办公厅关于2026年部分节假日安排的通知》国办发明电〔2025〕7号）。
    /// 支持用户自定义覆盖日期（公司调休等）。
    /// </summary>
    public class PayCalendar
    {
        /// <summary>内置的法定放假日（不含调休上班日）。</summary>
        private static readonly Dictionary<int, HashSet<string>> BuiltInHolidays = new()
        {
            [2026] = new HashSet<string>(StringComparer.Ordinal)
            {
                // 元旦 1月1日-3日
                "2026-01-01", "2026-01-02", "2026-01-03",
                // 春节 2月15日-23日
                "2026-02-15", "2026-02-16", "2026-02-17", "2026-02-18", "2026-02-19",
                "2026-02-20", "2026-02-21", "2026-02-22", "2026-02-23",
                // 清明 4月4日-6日
                "2026-04-04", "2026-04-05", "2026-04-06",
                // 劳动节 5月1日-5日
                "2026-05-01", "2026-05-02", "2026-05-03", "2026-05-04", "2026-05-05",
                // 端午 6月19日-21日
                "2026-06-19", "2026-06-20", "2026-06-21",
                // 中秋 9月25日-27日
                "2026-09-25", "2026-09-26", "2026-09-27",
                // 国庆 10月1日-7日
                "2026-10-01", "2026-10-02", "2026-10-03", "2026-10-04",
                "2026-10-05", "2026-10-06", "2026-10-07",
            },
            // 2025 年（部分，仅用于历史数据回溯）
            [2025] = new HashSet<string>(StringComparer.Ordinal)
            {
                "2025-01-01",
                "2025-01-28", "2025-01-29", "2025-01-30", "2025-01-31", "2025-02-01",
                "2025-02-02", "2025-02-03", "2025-02-04",
                "2025-04-04", "2025-04-05", "2025-04-06",
                "2025-05-01", "2025-05-02", "2025-05-03", "2025-05-04", "2025-05-05",
                "2025-05-31", "2025-06-01", "2025-06-02",
                "2025-10-01", "2025-10-02", "2025-10-03", "2025-10-04",
                "2025-10-05", "2025-10-06", "2025-10-07", "2025-10-08",
            },
        };

        /// <summary>调休上班日（周末但需上班）。</summary>
        private static readonly Dictionary<int, HashSet<string>> BuiltInMakeupWorkdays = new()
        {
            [2026] = new HashSet<string>(StringComparer.Ordinal)
            {
                "2026-01-04", // 元旦：1月4日(周日)上班
                "2026-02-14", // 春节：2月14日(周六)上班
                "2026-02-28", // 春节：2月28日(周六)上班
                "2026-05-09", // 劳动节：5月9日(周六)上班
                "2026-09-20", // 国庆：9月20日(周日)上班
                "2026-10-10", // 国庆：10月10日(周六)上班
            },
            [2025] = new HashSet<string>(StringComparer.Ordinal)
            {
                "2025-01-26", "2025-02-08", "2025-04-27", "2025-09-28", "2025-10-11",
            },
        };

        /// <summary>用户额外指定的放假日（覆盖默认判定）。</summary>
        public HashSet<string> ExtraHolidays { get; } = new(StringComparer.Ordinal);

        /// <summary>用户额外指定的调休上班日（覆盖默认判定）。</summary>
        public HashSet<string> ExtraWorkdays { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// 该年度是否内置了节假日数据。
        /// 内置表只覆盖到 2026；之后的年份需要用户在设置里补自定义假日，
        /// 否则 10/1、1/1 这类固定假日会被当成工作日算进月累计（UI 会提示）。
        /// </summary>
        public static bool HasBuiltInData(int year) =>
            BuiltInHolidays.ContainsKey(year) || BuiltInMakeupWorkdays.ContainsKey(year);

        /// <summary>是否为法定放假日（不含用户自定义）。</summary>
        public static bool IsBuiltInHoliday(DateTime date)        {
            string key = date.ToString("yyyy-MM-dd");
            if (BuiltInHolidays.TryGetValue(date.Year, out var set) && set.Contains(key)) return true;
            return false;
        }

        /// <summary>是否为调休上班日（内置数据）。</summary>
        public static bool IsBuiltInMakeupWorkday(DateTime date)
        {
            string key = date.ToString("yyyy-MM-dd");
            if (BuiltInMakeupWorkdays.TryGetValue(date.Year, out var set) && set.Contains(key)) return true;
            return false;
        }

        /// <summary>是否放假：内置法定假日，或用户额外指定。</summary>
        public bool IsHoliday(DateTime date)
        {
            string key = date.ToString("yyyy-MM-dd");
            if (ExtraHolidays.Contains(key)) return true;
            if (ExtraWorkdays.Contains(key)) return false;
            return IsBuiltInHoliday(date);
        }

        /// <summary>是否为调休上班日：内置数据，或用户额外指定。</summary>
        public bool IsMakeupWorkday(DateTime date)
        {
            string key = date.ToString("yyyy-MM-dd");
            if (ExtraWorkdays.Contains(key)) return true;
            if (ExtraHolidays.Contains(key)) return false;
            return IsBuiltInMakeupWorkday(date);
        }

        /// <summary>
        /// 是否为计薪日。规则：
        /// 1. 调休上班日 → 是
        /// 2. 法定放假日 → 否
        /// 3. 其余按用户设置的星期几判断
        /// </summary>
        public bool IsPayDay(DateTime date, int[] weekdays)
        {
            if (IsMakeupWorkday(date)) return true;
            if (IsHoliday(date)) return false;
            if (weekdays == null || weekdays.Length == 0) return false;
            return Array.IndexOf(weekdays, (int)date.DayOfWeek) >= 0;
        }

        /// <summary>统计某月内的计薪日数量。</summary>
        public int CountPayDays(int year, int month, int[] weekdays)
        {
            int count = 0;
            int days = DateTime.DaysInMonth(year, month);
            for (int d = 1; d <= days; d++)
            {
                if (IsPayDay(new DateTime(year, month, d), weekdays)) count++;
            }
            return count;
        }

        /// <summary>找出某月下一个计薪日（从 startDate 起，含当天）。</summary>
        public DateTime? NextPayDay(int year, int month, int[] weekdays, DateTime startDate)
        {
            int days = DateTime.DaysInMonth(year, month);
            for (int d = startDate.Day; d <= days; d++)
            {
                var dt = new DateTime(year, month, d);
                if (IsPayDay(dt, weekdays)) return dt;
            }
            return null;
        }

        /// <summary>找出某月上一个计薪日（不含当天，向前找）。</summary>
        public DateTime? PrevPayDay(int year, int month, int[] weekdays, DateTime startDate)
        {
            for (int d = startDate.Day - 1; d >= 1; d--)
            {
                var dt = new DateTime(year, month, d);
                if (IsPayDay(dt, weekdays)) return dt;
            }
            return null;
        }
    }
}
