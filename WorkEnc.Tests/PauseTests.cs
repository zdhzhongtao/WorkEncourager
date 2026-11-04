using System;
using System.Collections.Generic;
using WorkEnc.Core;
using Xunit;

namespace WorkEnc.Tests
{
    /// <summary>
    /// 暂停语义测试。
    ///
    /// 背景：早期实现在 Calculate 里写了 `DateTime effNow = paused ? now : now;` —— 两个分支相同，
    /// 于是暂停期间工时仍随墙上时钟增长，界面上"今日已赚/进度/倒计时"一直在跳，
    /// 而且错误值还会被写进 CSV 的"当日已赚"列。
    /// 修复后：暂停时以"暂停那一刻"为基准冻结全部派生数值。
    /// </summary>
    public class PauseTests
    {
        private static readonly int[] Weekdays = { 1, 2, 3, 4, 5 };
        private const double Salary = 21750;   // 21.75 天 × 1000，日薪正好 1000，方便心算

        private static WorkSchedule Simple(string start = "09:00", string end = "18:00", params string[] breaks)
        {
            return new WorkSchedule { Start = start, End = end, Breaks = new List<string>(breaks) };
        }

        [Fact]
        public void 暂停后金额进度工时全部冻结()
        {
            var calc = new SalaryCalculator(Simple(), new PayCalendar());
            var pauseAt = new DateTime(2026, 11, 4, 10, 30, 0);

            var atPause = calc.Compute(pauseAt, Salary, Weekdays, 10, true, 0, pauseAt);
            // 暂停 3 小时后再取一次快照，数值必须一模一样
            var later = calc.Compute(pauseAt.AddHours(3), Salary, Weekdays, 10, true, 0, pauseAt);

            Assert.Equal(atPause.Earned, later.Earned, 9);
            Assert.Equal(atPause.WorkedSec, later.WorkedSec, 6);
            Assert.Equal(atPause.RemainSec, later.RemainSec, 6);
            Assert.Equal(atPause.Pct, later.Pct, 9);
            Assert.Equal("paused", later.Status);
        }

        [Fact]
        public void 暂停后倒计时也冻结()
        {
            var calc = new SalaryCalculator(Simple(), new PayCalendar());
            var pauseAt = new DateTime(2026, 11, 4, 10, 30, 0);

            var atPause = calc.Compute(pauseAt, Salary, Weekdays, 10, true, 0, pauseAt);
            var later = calc.Compute(pauseAt.AddHours(2), Salary, Weekdays, 10, true, 0, pauseAt);

            // 距下班的剩余时间不能因为墙上时钟走动而减少
            Assert.Equal(atPause.RemainSec, later.RemainSec, 6);
            // 09:00 上班、18:00 下班共 9 小时，暂停于 10:30（已工作 1.5h）→ 还剩 7.5h
            Assert.Equal(7.5 * 3600, atPause.RemainSec, 1);
        }

        [Fact]
        public void 未暂停时金额随时间正常增长()
        {
            var calc = new SalaryCalculator(Simple(), new PayCalendar());
            var day = new DateTime(2026, 11, 4);

            var at10 = calc.Compute(day.AddHours(10), Salary, Weekdays, 10, false, 0);
            var at11 = calc.Compute(day.AddHours(11), Salary, Weekdays, 10, false, 0);

            // 日薪 1000，9 小时工时 → 每小时约 111.11
            Assert.True(at11.Earned > at10.Earned);
            Assert.Equal(111.111, at11.Earned - at10.Earned, 2);
        }

        [Fact]
        public void 恢复瞬间数值不跳变()
        {
            var sched = Simple();
            var calc = new SalaryCalculator(sched, new PayCalendar());
            var day = new DateTime(2026, 11, 4);
            var pauseAt = day.AddHours(10.5);      // 10:30 暂停
            var resumeAt = day.AddHours(11.5);     // 11:30 恢复

            // 恢复时累计的暂停时长 = 暂停期间真正落在工作时段内的秒数
            double pausedWork = sched.GetElapsedWorkSeconds(day, resumeAt)
                              - sched.GetElapsedWorkSeconds(day, pauseAt);
            Assert.Equal(3600, pausedWork, 1);

            var during = calc.Compute(resumeAt, Salary, Weekdays, 10, true, 0, pauseAt);
            var after = calc.Compute(resumeAt, Salary, Weekdays, 10, false, pausedWork);

            Assert.Equal(during.Earned, after.Earned, 6);
            Assert.Equal(during.WorkedSec, after.WorkedSec, 6);
            Assert.Equal(during.Pct, after.Pct, 9);
        }

        [Fact]
        public void 暂停横跨午休不误扣工时()
        {
            var sched = Simple("09:00", "18:00", "12:00-13:00");
            var calc = new SalaryCalculator(sched, new PayCalendar());
            var day = new DateTime(2026, 11, 4);
            var pauseAt = day.AddHours(11.5);      // 11:30 暂停
            var resumeAt = day.AddHours(13.5);     // 13:30 恢复（跨过 12:00-13:00 午休）

            // 只应扣掉 11:30-12:00 与 13:00-13:30 这两段，午休那 1 小时不算
            double pausedWork = sched.GetElapsedWorkSeconds(day, resumeAt)
                              - sched.GetElapsedWorkSeconds(day, pauseAt);
            Assert.Equal(3600, pausedWork, 1);

            var during = calc.Compute(resumeAt, Salary, Weekdays, 10, true, 0, pauseAt);
            var after = calc.Compute(resumeAt, Salary, Weekdays, 10, false, pausedWork);
            Assert.Equal(during.WorkedSec, after.WorkedSec, 6);
            Assert.Equal(during.Earned, after.Earned, 6);
        }

        [Fact]
        public void 跨天暂停按暂停当天的作息计算()
        {
            // 夜班 22:00 → 次日 06:00
            var calc = new SalaryCalculator(Simple("22:00", "06:00"), new PayCalendar());
            var pauseAt = new DateTime(2026, 11, 4, 23, 0, 0);   // 4 号夜里暂停
            var now = new DateTime(2026, 11, 5, 2, 0, 0);        // 5 号凌晨

            var snap = calc.Compute(now, Salary, Weekdays, 10, true, 0, pauseAt);

            // 以暂停当天的作息算：22:00 上班，23:00 暂停 → 已工作 1 小时
            Assert.Equal(3600, snap.WorkedSec, 1);
            Assert.Equal(8 * 3600, snap.TotalSec, 1);
        }

        [Fact]
        public void 暂停期间累计的暂停秒数会被正确扣除()
        {
            var calc = new SalaryCalculator(Simple(), new PayCalendar());
            var day = new DateTime(2026, 11, 4);
            var pauseAt = day.AddHours(10);        // 10:00 暂停
            var resumeAt = day.AddHours(11);       // 11:00 恢复

            // 暂停 1 小时，恢复时把这 1 小时扣掉
            var snap = calc.Compute(resumeAt, Salary, Weekdays, 10, false, 3600);
            // 11 点本应工作 2 小时，扣掉暂停的 1 小时 → 1 小时
            Assert.Equal(3600, snap.WorkedSec, 1);
        }

        [Fact]
        public void 暂停时状态优先于上下班状态()
        {
            var calc = new SalaryCalculator(Simple(), new PayCalendar());
            var pauseAt = new DateTime(2026, 11, 4, 8, 0, 0);   // 上班前就暂停

            var snap = calc.Compute(pauseAt.AddHours(2), Salary, Weekdays, 10, true, 0, pauseAt);
            Assert.Equal("paused", snap.Status);
            Assert.Equal(0, snap.WorkedSec, 1);
        }
    }
}
