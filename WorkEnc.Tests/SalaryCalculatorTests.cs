using System;
using System.Collections.Generic;
using WorkEnc.Core;
using Xunit;

namespace WorkEnc.Tests
{
    /// <summary>作息表与薪资折算的单元测试。</summary>
    public class SalaryCalculatorTests
    {
        private static WorkSchedule Simple(string start = "09:00", string end = "18:00", params string[] breaks)
        {
            return new WorkSchedule { Start = start, End = end, Breaks = new List<string>(breaks) };
        }

        [Fact]
        public void ParseMinutes_ValidAndInvalid()
        {
            Assert.True(WorkSchedule.TryParseMinutes("09:00", out int m));
            Assert.Equal(540, m);
            Assert.True(WorkSchedule.TryParseMinutes("18：30", out int m2));  // 全角冒号
            Assert.Equal(1110, m2);
            Assert.False(WorkSchedule.TryParseMinutes("25:00", out _));
            Assert.False(WorkSchedule.TryParseMinutes("9", out _));
            Assert.False(WorkSchedule.TryParseMinutes("", out _));
            Assert.False(WorkSchedule.TryParseMinutes(null, out _));
            Assert.False(WorkSchedule.TryParseMinutes("09:60", out _));
        }

        [Fact]
        public void FormatMinutes_RoundTrips()
        {
            Assert.Equal("09:00", WorkSchedule.FormatMinutes(540));
            Assert.Equal("23:59", WorkSchedule.FormatMinutes(1439));
            Assert.Equal("00:00", WorkSchedule.FormatMinutes(1440));   // 跨天回绕
        }

        [Fact]
        public void TotalWorkSeconds_NoBreak_IsNineHours()
        {
            var s = Simple();
            var day = new DateTime(2026, 11, 4);
            Assert.Equal(9 * 3600, s.GetTotalWorkSeconds(day), 0.5);
        }

        [Fact]
        public void TotalWorkSeconds_WithLunchBreak_IsEightHours()
        {
            var s = Simple("09:00", "18:00", "12:00-13:00");
            var day = new DateTime(2026, 11, 4);
            Assert.Equal(8 * 3600, s.GetTotalWorkSeconds(day), 0.5);
        }

        [Fact]
        public void TotalWorkSeconds_AcrossMidnight()
        {
            var s = Simple("22:00", "06:00");
            var day = new DateTime(2026, 11, 4);
            Assert.Equal(8 * 3600, s.GetTotalWorkSeconds(day), 0.5);
        }

        [Fact]
        public void ElapsedWorkSeconds_ExcludesLunch()
        {
            var s = Simple("09:00", "18:00", "12:00-13:00");
            var day = new DateTime(2026, 11, 4);
            // 12:30 时：09:00-12:00 工作 3 小时
            var noon = day.AddHours(12).AddMinutes(30);
            Assert.Equal(3 * 3600, s.GetElapsedWorkSeconds(day, noon), 0.5);
            // 14:00 时：3h + 1h = 4h
            var two = day.AddHours(14);
            Assert.Equal(4 * 3600, s.GetElapsedWorkSeconds(day, two), 0.5);
        }

        [Fact]
        public void DailyPay_MonthlySalaryDividedByPayDays()
        {
            var calc = new SalaryCalculator(Simple(), new PayCalendar());
            var now = new DateTime(2026, 11, 4, 10, 0, 0);
            var snap = calc.Compute(now, 2175, new[] { 1, 2, 3, 4, 5 }, 10, false, 0);

            Assert.Equal(100.0, snap.DailyPay, 4);           // 2175 / 21.75
            Assert.Equal(100.0 / 9 / 3600.0, snap.PerSec, 10);
            Assert.Equal(100.0 / 9, snap.Hourly, 6);
        }

        [Fact]
        public void Earned_AccumulatesWithTime()
        {
            var calc = new SalaryCalculator(Simple(), new PayCalendar());
            var day = new DateTime(2026, 11, 4);

            var at10 = calc.Compute(day.AddHours(10), 2175, new[] { 1, 2, 3, 4, 5 }, 10, false, 0);
            var at14 = calc.Compute(day.AddHours(14), 2175, new[] { 1, 2, 3, 4, 5 }, 10, false, 0);

            // 10 点已赚 1 小时，14 点已赚 5 小时
            Assert.Equal(at10.DailyPay / 9.0, at10.Earned, 4);
            Assert.Equal(at14.DailyPay / 9.0 * 5, at14.Earned, 4);
            Assert.Equal(55.55, at14.Pct, 1);
        }

        [Fact]
        public void Status_WaitBeforeStart()
        {
            var calc = new SalaryCalculator(Simple(), new PayCalendar());
            var snap = calc.Compute(new DateTime(2026, 11, 4, 8, 0, 0), 6000, new[] { 1, 2, 3, 4, 5 }, 10, false, 0);
            Assert.Equal("wait", snap.Status);
            Assert.Equal(0, snap.WorkedSec, 1);
        }

        [Fact]
        public void Status_OffAfterEnd()
        {
            var calc = new SalaryCalculator(Simple(), new PayCalendar());
            var snap = calc.Compute(new DateTime(2026, 11, 4, 19, 0, 0), 6000, new[] { 1, 2, 3, 4, 5 }, 10, false, 0);
            Assert.Equal("off", snap.Status);
            Assert.Equal(9 * 3600, snap.WorkedSec, 1);   // 封顶不超总工时
            Assert.Equal(100, snap.Pct, 1);
        }

        [Fact]
        public void Status_Paused()
        {
            var calc = new SalaryCalculator(Simple(), new PayCalendar());
            var snap = calc.Compute(new DateTime(2026, 11, 4, 14, 0, 0), 6000, new[] { 1, 2, 3, 4, 5 }, 10, true, 3600);
            Assert.Equal("paused", snap.Status);
        }

        [Fact]
        public void PausedAccum_IsSubtractedFromWorked()
        {
            var calc = new SalaryCalculator(Simple(), new PayCalendar());
            var day = new DateTime(2026, 11, 4);
            // 暂停累计 1 小时，则 14 点时实际只算 4 小时
            var snap = calc.Compute(day.AddHours(14), 2175, new[] { 1, 2, 3, 4, 5 }, 10, true, 3600);
            Assert.Equal(4 * 3600, snap.WorkedSec, 1);
        }

        [Fact]
        public void MonthTotal_PastPayDaysFullPlusTodayActual()
        {
            var calc = new SalaryCalculator(Simple(), new PayCalendar());
            // 2026-11-04 是周三
            var now = new DateTime(2026, 11, 4, 14, 0, 0);
            var snap = calc.Compute(now, 2175, new[] { 1, 2, 3, 4, 5 }, 10, false, 0);

            // 11月共 21 个计薪日；今天之前已过 11/2、11/3 两天
            Assert.Equal(21, snap.MonthPayDays);
            Assert.Equal(2, snap.MonthElapsedPayDays);
            // 2 天满薪 + 今天实际已赚
            Assert.Equal(snap.DailyPay * 2 + snap.Earned, snap.MonthTotal, 4);
        }

        [Fact]
        public void Status_RestOnWeekend()
        {
            var calc = new SalaryCalculator(Simple(), new PayCalendar());
            // 2026-11-07 是周六：不是计薪日，状态应是"休息日"而不是"上班中"
            var snap = calc.Compute(new DateTime(2026, 11, 7, 14, 0, 0), 6000, new[] { 1, 2, 3, 4, 5 }, 10, false, 0);
            Assert.Equal("rest", snap.Status);
            Assert.False(snap.IsPayDay);
        }

        [Fact]
        public void MonthTotal_RestDay_ExcludesTodayEarned()
        {
            var calc = new SalaryCalculator(Simple(), new PayCalendar());
            var now = new DateTime(2026, 11, 7, 14, 0, 0);   // 周六
            var snap = calc.Compute(now, 2175, new[] { 1, 2, 3, 4, 5 }, 10, false, 0);

            // 11/2~11/6 共 5 个计薪日；休息日加班赚的钱不并入当月工资进度
            Assert.Equal(5, snap.MonthElapsedPayDays);
            Assert.Equal(snap.DailyPay * 5, snap.MonthTotal, 4);
        }

        [Fact]
        public void MonthPct_IsRatioOfMonthlySalary()
        {
            var calc = new SalaryCalculator(Simple(), new PayCalendar());
            var now = new DateTime(2026, 11, 30, 18, 0, 0);
            var snap = calc.Compute(now, 2175, new[] { 1, 2, 3, 4, 5 }, 10, false, 0);
            Assert.InRange(snap.MonthPct, 0, 100);
        }

        [Fact]
        public void NextPayDate_InThisMonth()
        {
            var calc = new SalaryCalculator(Simple(), new PayCalendar());
            var now = new DateTime(2026, 11, 4);
            var (next, days) = calc.ComputeNextPayDate(now, 10, new[] { 1, 2, 3, 4, 5 });
            Assert.Equal(new DateTime(2026, 11, 10), next);
            Assert.Equal(6, days);
        }

        [Fact]
        public void NextPayDate_RollsToNextMonth()
        {
            var calc = new SalaryCalculator(Simple(), new PayCalendar());
            var now = new DateTime(2026, 11, 20);
            var (next, days) = calc.ComputeNextPayDate(now, 10, new[] { 1, 2, 3, 4, 5 });
            Assert.Equal(new DateTime(2026, 12, 10), next);
            Assert.Equal(20, days);
        }

        [Fact]
        public void NextPayDate_ClampsToShortMonth()
        {
            var calc = new SalaryCalculator(Simple(), new PayCalendar());
            // 2月只有28天（2026非闰年），发薪日填31应夹到28
            var now = new DateTime(2026, 2, 5);
            var (next, _) = calc.ComputeNextPayDate(now, 31, new[] { 1, 2, 3, 4, 5 });
            Assert.Equal(new DateTime(2026, 2, 28), next);
        }

        [Fact]
        public void CountPayDaysBetween_ExcludesStartIncludesEnd()
        {
            var calc = new SalaryCalculator(Simple(), new PayCalendar());
            // 11月2日(周一) 到 11月6日(周五)：不含起始日、含结束日 → 4 个计薪日
            int n = calc.CountPayDaysBetween(new DateTime(2026, 11, 2), new DateTime(2026, 11, 6), new[] { 1, 2, 3, 4, 5 });
            Assert.Equal(4, n);
        }

        [Fact]
        public void CountPayDaysBetween_SameDay_ReturnsZero()
        {
            var calc = new SalaryCalculator(Simple(), new PayCalendar());
            int n = calc.CountPayDaysBetween(new DateTime(2026, 11, 4), new DateTime(2026, 11, 4), new[] { 1, 2, 3, 4, 5 });
            Assert.Equal(0, n);
        }

        [Fact]
        public void ZeroSalary_DoesNotCrash()
        {
            var calc = new SalaryCalculator(Simple(), new PayCalendar());
            var snap = calc.Compute(new DateTime(2026, 11, 4, 10, 0, 0), 0, new[] { 1, 2, 3, 4, 5 }, 10, false, 0);
            Assert.Equal(0, snap.DailyPay);
            Assert.Equal(0, snap.Earned);
            Assert.Equal(0, snap.MonthPct);
        }
    }
}
