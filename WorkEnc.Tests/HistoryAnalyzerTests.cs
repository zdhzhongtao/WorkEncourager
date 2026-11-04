using System;
using System.Collections.Generic;
using System.Linq;
using WorkEnc.Core;
using Xunit;

namespace WorkEnc.Tests
{
    /// <summary>历史统计测试。</summary>
    public class HistoryAnalyzerTests
    {
        private static readonly int[] Workdays = { 1, 2, 3, 4, 5 };

        private static List<DayRecord> MakeRecords(params (string date, double worked, double earned)[] items)
        {
            return items.Select(i => new DayRecord
            {
                Date = i.date,
                WorkedSec = i.worked,
                ScheduledSec = 9 * 3600,
                Earned = i.earned,
                IsPayDay = true
            }).ToList();
        }

        [Fact]
        public void Analyze_SplitsWeekMonthYear()
        {
            // 2026-11-04 是周三
            var now = new DateTime(2026, 11, 4, 12, 0, 0);
            var recs = MakeRecords(
                ("2026-11-02", 8 * 3600, 100),   // 周一，本周
                ("2026-11-03", 9 * 3600, 120),   // 周二，本周
                ("2026-10-30", 7 * 3600, 80),   // 上周五，上周但同月
                ("2026-07-01", 6 * 3600, 60));  // 今年更早

            var a = new HistoryAnalyzer(new PayCalendar());
            var s = a.Analyze(recs, now, Workdays);

            // 本周 = 11/2(周一) ~ 11/4(周三) → 2 天
            Assert.Equal(2, s.Week.DayCount);
            Assert.Equal(17 * 3600, s.Week.TotalWorkedSec, 1);
            Assert.Equal(220, s.Week.TotalEarned, 3);

            // 本月 = 11/1 ~ 11/4 → 11/2、11/3 两天（10/30 属于上月）
            Assert.Equal(2, s.Month.DayCount);
            Assert.Equal(17 * 3600, s.Month.TotalWorkedSec, 1);

            // 本年 = 4 天
            Assert.Equal(4, s.Year.DayCount);

            // 全部 = 4 天
            Assert.Equal(4, s.All.DayCount);
        }

        [Fact]
        public void Analyze_WeekStartsOnMonday()
        {
            // 2026-11-08 是周日，本周起点应为 11/2（周一），不是 11/1
            var now = new DateTime(2026, 11, 8, 12, 0, 0);
            var recs = MakeRecords(
                ("2026-11-01", 8 * 3600, 100),   // 周日，属于上一周
                ("2026-11-02", 8 * 3600, 100),   // 周一，本周
                ("2026-11-08", 8 * 3600, 100));  // 周日，本周

            var a = new HistoryAnalyzer(new PayCalendar());
            var s = a.Analyze(recs, now, Workdays);

            Assert.Equal(2, s.Week.DayCount);   // 11/2 和 11/8，11/1 不算
            Assert.Equal(16 * 3600, s.Week.TotalWorkedSec, 1);
        }

        [Fact]
        public void Analyze_BestDayIsHighestEarned()
        {
            var now = new DateTime(2026, 11, 4);
            var recs = MakeRecords(
                ("2026-11-02", 8 * 3600, 100),
                ("2026-11-03", 9 * 3600, 250),
                ("2026-11-04", 7 * 3600, 180));

            var a = new HistoryAnalyzer(new PayCalendar());
            var s = a.Analyze(recs, now, Workdays);

            Assert.Equal("2026-11-03", s.All.BestDayDate);
            Assert.Equal(250, s.All.BestDayEarned, 3);
            Assert.Equal(9 * 3600, s.All.BestDayWorkedSec, 1);
        }

        [Fact]
        public void Analyze_MonthPayDaysCount()
        {
            var now = new DateTime(2026, 11, 4);
            var a = new HistoryAnalyzer(new PayCalendar());
            var s = a.Analyze(new List<DayRecord>(), now, Workdays);
            Assert.Equal(21, s.MonthPayDays);   // 2026年11月
        }

        [Fact]
        public void Analyze_EmptyRecords_ZeroValues()
        {
            var now = new DateTime(2026, 11, 4);
            var a = new HistoryAnalyzer(new PayCalendar());
            var s = a.Analyze(new List<DayRecord>(), now, Workdays);

            Assert.Equal(0, s.All.DayCount);
            Assert.Equal(0, s.All.TotalWorkedSec);
            Assert.Equal(0, s.All.TotalEarned);
            Assert.Equal("", s.All.BestDayDate);
        }

        [Fact]
        public void RecalcMonthScheduled_UsesActualRecords()
        {
            var now = new DateTime(2026, 11, 4);
            var recs = MakeRecords(("2026-11-02", 8 * 3600, 100));
            recs[0].ScheduledSec = 8 * 3600;   // 当天有午休，应出勤 8 小时

            var a = new HistoryAnalyzer(new PayCalendar());
            double total = a.RecalcMonthScheduled(recs, now);
            Assert.Equal(8 * 3600, total, 1);
        }

        [Fact]
        public void Analyze_OvertimeSummed()
        {
            var now = new DateTime(2026, 11, 4);
            var recs = MakeRecords(("2026-11-02", 9 * 3600, 100), ("2026-11-03", 10 * 3600, 120));
            recs[0].OvertimeSec = 1800;
            recs[1].OvertimeSec = 3600;

            var a = new HistoryAnalyzer(new PayCalendar());
            var s = a.Analyze(recs, now, Workdays);
            Assert.Equal(5400, s.All.TotalOvertimeSec, 1);
        }
    }
}
