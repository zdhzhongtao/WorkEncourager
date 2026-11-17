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
        /// <summary>本月应出勤总秒数是按 9 小时/天估算的（记录里没有真实作息数据）。</summary>
        public bool MonthScheduledIsEstimated { get; set; }
    }

    /// <summary>历史统计计算器。</summary>
    public class HistoryAnalyzer
    {
        /// <summary>
        /// 记录里没有当日应出勤时长时用的兜底值（9 小时/天）。
        /// 以前这个 9 直接写死在循环里，看起来像真实数据，其实是猜的。
        /// </summary>
        public const double DefaultDailyWorkSec = 9 * 3600;

        private readonly PayCalendar _calendar;

        public HistoryAnalyzer(PayCalendar calendar)
        {
            _calendar = calendar ?? new PayCalendar();
        }

        /// <summary>计算各区间的统计汇总。</summary>
        public HistorySummary Analyze(IReadOnlyList<DayRecord> records, DateTime now, int[] weekdays)
        {
            var s = new HistorySummary();

            // 本周起点：周一。
            // DayOfWeek 里周日是 0，直接 -(int)DayOfWeek 会让周一到周六都落到"上周日"，
            // 于是周日那天的数据会被错算进本周。用 (dow + 6) % 7 换算成"距本周一的天数"。
            var weekStart = now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7));

            s.Week = CsvExporter.Summarize(records, weekStart, now.Date);
            s.Month = CsvExporter.Summarize(records, now.Date.AddDays(1 - now.Day), now.Date);
            s.Year = CsvExporter.Summarize(records, new DateTime(now.Year, 1, 1), now.Date);
            s.All = CsvExporter.Summarize(records, DateTime.MinValue, DateTime.MaxValue);

            s.MonthPayDays = _calendar.CountPayDays(now.Year, now.Month, weekdays);

            // 本月应出勤总秒数：优先用记录里真实的每日应出勤时长（扣过午休的），
            // 记录还没有时才退回"计薪日 × 9 小时"的估算，并把估算值说明清楚。
            s.MonthScheduledSec = RecalcMonthScheduled(records, now);
            s.MonthScheduledIsEstimated = s.MonthScheduledSec <= 0;
            if (s.MonthScheduledIsEstimated) s.MonthScheduledSec = EstimateMonthScheduled(now, weekdays);

            return s;
        }

        /// <summary>按计薪日估算本月应出勤总秒数（只在记录里没有真实作息时使用）。</summary>
        private double EstimateMonthScheduled(DateTime now, int[] weekdays)
        {
            double scheduled = 0;
            int daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
            for (int d = 1; d <= daysInMonth; d++)
            {
                var dt = new DateTime(now.Year, now.Month, d);
                if (!_calendar.IsPayDay(dt, weekdays)) continue;
                scheduled += DefaultDailyWorkSec;
            }
            return scheduled;
        }

        /// <summary>
        /// 按每日实际应出勤时长（来自记录）累加本月应出勤总秒数，比固定 9 小时更准确。
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
