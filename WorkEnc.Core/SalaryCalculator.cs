using System;
using System.Collections.Generic;

namespace WorkEnc.Core
{
    /// <summary>计算快照：界面所需的全部派生数据。</summary>
    public class Snapshot
    {
        public DateTime Now { get; set; }
        public DateTime StartTs { get; set; }
        public DateTime EndTs { get; set; }

        /// <summary>计薪总秒数（已扣午休）。</summary>
        public double TotalSec { get; set; }
        public double WorkedSec { get; set; }
        public double RemainSec { get; set; }
        public double Pct { get; set; }

        public double DailyPay { get; set; }
        public double PerSec { get; set; }
        public double Hourly { get; set; }
        public double Earned { get; set; }

        /// <summary>本月累计（已过计薪日按满勤补算 + 今天实际已赚）。</summary>
        public double MonthTotal { get; set; }
        /// <summary>本月工资条进度（0-100）。</summary>
        public double MonthPct { get; set; }
        /// <summary>本月计薪日总数。</summary>
        public int MonthPayDays { get; set; }
        /// <summary>本月已过的计薪日数（不含今天）。</summary>
        public int MonthElapsedPayDays { get; set; }

        /// <summary>距下一个发薪日的天数。</summary>
        public int PayDayInDays { get; set; }
        /// <summary>下一次发薪日。</summary>
        public DateTime? NextPayDate { get; set; }

        /// <summary>working / off / wait / paused</summary>
        public string Status { get; set; } = "off";
    }

    /// <summary>薪资计算器：纯函数，无 UI 依赖，可单元测试。</summary>
    public class SalaryCalculator
    {
        /// <summary>法定月计薪天数：(365 - 104) / 12 = 21.75</summary>
        public const double PayDaysPerMonth = 21.75;

        private readonly WorkSchedule _schedule;
        private readonly PayCalendar _calendar;

        public SalaryCalculator(WorkSchedule schedule, PayCalendar calendar)
        {
            _schedule = schedule ?? new WorkSchedule();
            _calendar = calendar ?? new PayCalendar();
        }

        /// <summary>使用默认作息与空日历构造。</summary>
        public SalaryCalculator()
            : this(new WorkSchedule(), new PayCalendar())
        {
        }

        /// <summary>
        /// 计算指定时刻的快照。
        /// </summary>
        /// <param name="now">当前时刻</param>
        /// <param name="monthlySalary">月薪</param>
        /// <param name="weekdays">计薪星期（0=周日）</param>
        /// <param name="payDay">每月发薪日（1-31）</param>
        /// <param name="paused">是否已暂停计时</param>
        /// <param name="pausedAccumSec">累计暂停秒数</param>
        public Snapshot Compute(DateTime now, double monthlySalary, int[] weekdays, int payDay,
                               bool paused, double pausedAccumSec)
        {
            var snap = new Snapshot { Now = now };

            var baseDate = now.Date;
            snap.StartTs = _schedule.GetStartTime(baseDate);
            snap.EndTs = _schedule.GetEndTime(baseDate);
            snap.TotalSec = _schedule.GetTotalWorkSeconds(baseDate);

            // 暂停：把"有效当前时间"回退到暂停时刻，并扣除此前累计的暂停时长
            DateTime effNow = paused ? now : now;
            double workSec = _schedule.GetElapsedWorkSeconds(baseDate, effNow);

            // 扣除已累计的暂停时长（暂停期间不计入工作）
            double effectiveWorked = Math.Max(0, workSec - pausedAccumSec);
            if (effectiveWorked > snap.TotalSec) effectiveWorked = snap.TotalSec;

            snap.WorkedSec = effectiveWorked;
            snap.RemainSec = Math.Max(0, snap.TotalSec - snap.WorkedSec);
            snap.Pct = snap.TotalSec > 0 ? snap.WorkedSec / snap.TotalSec * 100.0 : 0;

            // 金额
            snap.DailyPay = monthlySalary / PayDaysPerMonth;
            snap.PerSec = snap.TotalSec > 0 ? snap.DailyPay / snap.TotalSec : 0;
            snap.Hourly = snap.PerSec * 3600.0;
            snap.Earned = snap.PerSec * snap.WorkedSec;

            // 状态
            if (paused) snap.Status = "paused";
            else if (now < snap.StartTs) snap.Status = "wait";
            else if (now >= snap.EndTs) snap.Status = "off";
            else snap.Status = "working";

            // 本月累计
            snap.MonthPayDays = _calendar.CountPayDays(now.Year, now.Month, weekdays);
            snap.MonthElapsedPayDays = 0;
            double monthSum = 0;
            int daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
            for (int d = 1; d < now.Day; d++)
            {
                var dt = new DateTime(now.Year, now.Month, d);
                if (!_calendar.IsPayDay(dt, weekdays)) continue;
                snap.MonthElapsedPayDays++;
                monthSum += snap.DailyPay;
            }
            monthSum += snap.Earned;   // 今天按实际已赚
            snap.MonthTotal = monthSum;
            snap.MonthPct = monthlySalary > 0 ? Math.Min(100.0, snap.MonthTotal / monthlySalary * 100.0) : 0;

            // 发薪日
            var (nextPay, daysTo) = ComputeNextPayDate(now, payDay, weekdays);
            snap.NextPayDate = nextPay;
            snap.PayDayInDays = daysTo;

            return snap;
        }

        /// <summary>计算下一次发薪日及距今天数。</summary>
        public (DateTime? next, int days) ComputeNextPayDate(DateTime now, int payDay, int[] weekdays)
        {
            if (payDay < 1 || payDay > 31) return (null, -1);

            // 先看本月
            int daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
            int effectivePayDay = Math.Min(payDay, daysInMonth);
            var thisMonth = new DateTime(now.Year, now.Month, effectivePayDay);

            if (thisMonth >= now.Date) return (thisMonth, (int)(thisMonth - now.Date).TotalDays);

            // 本月已过，看下个月
            var nextMonth = now.Month == 12 ? new DateTime(now.Year + 1, 1, 1) : new DateTime(now.Year, now.Month + 1, 1);
            int nd = DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month);
            var target = new DateTime(nextMonth.Year, nextMonth.Month, Math.Min(payDay, nd));
            return (target, (int)(target - now.Date).TotalDays);
        }

        /// <summary>
        /// 计算两个日期之间的计薪日数量（不含起始日，含结束日）。
        /// </summary>
        public int CountPayDaysBetween(DateTime from, DateTime to, int[] weekdays)
        {
            if (to < from) return 0;
            int count = 0;
            var cur = from.Date.AddDays(1);
            while (cur <= to.Date)
            {
                if (_calendar.IsPayDay(cur, weekdays)) count++;
                cur = cur.AddDays(1);
            }
            return count;
        }
    }
}
