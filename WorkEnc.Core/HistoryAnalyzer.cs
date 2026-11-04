using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WorkEnc.Core
{
    /// <summary>历史统计汇总结果。</summary>
    public class HistorySummary
    {
        /// <summary>本周（周一至周日）。</summary>
        public StatsSummary Week { get; set; } = new StatsSummary();
        /// <summary>本月。</summary>
        public StatsSummary Month { get; set; } = new StatsSummary();
        /// <summary>本年。</summary>
        public StatsSummary Year { get; set; } = new StatsSummary();
        /// <summary>全部历史。</summary>
        public StatsSummary All { get; set; } = new StatsSummary();

        /// <summary>本月计薪日总数。</summary>
        public int MonthPayDays { get; set; }
        /// <summary>本月应出勤总秒数。</summary>
        public double MonthScheduledSec { get; set; }
    }

    /// <summary>历史统计计算器。</summary>
    public class HistoryAnalyzer
    {
        private readonly PayCalendar _calendar;

        public HistoryAnalyzer(PayCalendar calendar)
        {
            _calendar = calendar ?? new PayCalendar();
        }

        /// <summary>计算各区间的统计汇总。</summary>
        public HistorySummary Analyze(IReadOnlyList<DayRecord> records, DateTime now, int[] weekdays)
        {
            var s = new HistorySummary();

            // 本周起点：周一
            var weekStart = now.Date.AddDays(-(int)now.DayOfWeek);
            if (now.DayOfWeek == DayOfWeek.Sunday) weekStart = now.Date.AddDays(-6);

            s.Week = CsvExporter.Summarize(records, weekStart, now.Date);
            s.Month = CsvExporter.Summarize(records, now.Date.AddDays(1 - now.Day), now.Date);
            s.Year = CsvExporter.Summarize(records, new DateTime(now.Year, 1, 1), now.Date);
            s.All = CsvExporter.Summarize(records, DateTime.MinValue, DateTime.MaxValue);

            s.MonthPayDays = _calendar.CountPayDays(now.Year, now.Month, weekdays);

            // 本月应出勤总秒数：逐个计薪日累加
            double scheduled = 0;
            int daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
            for (int d = 1; d <= daysInMonth; d++)
            {
                var dt = new DateTime(now.Year, now.Month, d);
                if (!_calendar.IsPayDay(dt, weekdays)) continue;
                scheduled += 9 * 3600;   // 每日默认 9 小时，由调用方覆盖时另算
            }
            s.MonthScheduledSec = scheduled;

            return s;
        }

        /// <summary>
        /// 按每日实际应出勤时长（来自记录）重算本月应出勤总秒数，比固定 9 小时更准确。
        /// </summary>
        public double RecalcMonthScheduled(IReadOnlyList<DayRecord> records, DateTime now)
        {
            double sum = 0;
            foreach (var r in records)
            {
                if (r.ScheduledSec <= 0) continue;
                if (!DateTime.TryParseExact(r.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var dt)) continue;
                if (dt.Year == now.Year && dt.Month == now.Month) sum += r.ScheduledSec;
            }
            return sum;
        }
    }
}
