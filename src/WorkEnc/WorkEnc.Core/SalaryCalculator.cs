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

        /// <summary>今天是否计薪日（周末/法定假日为 false）。</summary>
        public bool IsPayDay { get; set; } = true;

        /// <summary>
        /// 本次计算归属的"班次起始日"。
        /// 夜班（22:00 → 次日 06:00）在凌晨还没下班，这时它属于前一天开始的那个班次。
        /// 工时、金额、计薪日判定都要按这一天来算，否则凌晨会一分钱都不算。
        /// </summary>
        public DateTime BaseDate { get; set; }

        /// <summary>working / off / wait / paused / rest</summary>
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
        /// <param name="pausedAccumSec">此前若干次暂停期间累计的工作秒数（需在恢复时扣掉）</param>
        /// <param name="pausedAt">
        /// 暂停发生的时刻。暂停时所有派生数值（已赚/进度/已工作/剩余）都以该时刻为基准冻结，
        /// 否则暂停期间工时仍随墙上时钟增长，界面数字会一直跳动。传 null 则退化为按 now 计算。
        /// </param>
        public Snapshot Compute(DateTime now, double monthlySalary, int[] weekdays, int payDay,
                               bool paused, double pausedAccumSec, DateTime? pausedAt = null)
        {
            var snap = new Snapshot { Now = now };

            var baseDate = now.Date;

            // 暂停：把"有效当前时间"回退到暂停那一刻，使工时、金额、进度、剩余全部冻结。
            // 跨天暂停时改用暂停当天的作息表（例如夜班 22:00→06:00）。
            DateTime effNow = now;
            if (paused && pausedAt.HasValue)
            {
                effNow = pausedAt.Value;
                if (effNow.Date != baseDate) baseDate = effNow.Date;
            }

            // 夜班归属：凌晨那几小时属于"前一天开始的那个班次"。
            // 不做这一步，02:00 查的是当天的作息（22:00 才上班），
            // 工时与金额全变成 0，界面显示"今天已赚 ¥0.0000"。
            baseDate = ResolveShiftDate(baseDate, effNow);
            snap.BaseDate = baseDate;

            snap.StartTs = _schedule.GetStartTime(baseDate);
            snap.EndTs = _schedule.GetEndTime(baseDate);
            snap.TotalSec = _schedule.GetTotalWorkSeconds(baseDate);

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
            // 休息日（周末 / 法定假日）单独一个状态，否则周六开着程序会显示"上班中"、
            // 并且把今天的 Earned 重复加进本月累计，导致月薪进度虚高一天。
            snap.IsPayDay = _calendar.IsPayDay(baseDate, weekdays);
            if (paused) snap.Status = "paused";
            else if (!snap.IsPayDay) snap.Status = "rest";
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
            // 今天只有是计薪日才并入月累计；休息日加班赚的钱不进月薪进度（按加班处理）
            // 夜班跨月时（月初凌晨还在上月末的班），这笔钱属于上个月，不并进本月。
            if (snap.IsPayDay && baseDate.Year == now.Year && baseDate.Month == now.Month)
                monthSum += snap.Earned;
            snap.MonthTotal = monthSum;
            snap.MonthPct = monthlySalary > 0 ? Math.Min(100.0, snap.MonthTotal / monthlySalary * 100.0) : 0;

            // 发薪日
            var (nextPay, daysTo) = ComputeNextPayDate(now, payDay, weekdays);
            snap.NextPayDate = nextPay;
            snap.PayDayInDays = daysTo;

            return snap;
        }

        /// <summary>
        /// 找到"此刻正在进行的那一班"是从哪天开始的。
        ///
        /// 作息表允许跨天（22:00 → 次日 06:00），凌晨两点还属于前一天开始的那个班次。
        /// 返回值用来决定工时、金额、计薪日按哪一天算。
        /// </summary>
        private DateTime ResolveShiftDate(DateTime baseDate, DateTime at)
        {
            var prev = baseDate.AddDays(-1);
            var prevStart = _schedule.GetStartTime(prev);
            var prevEnd = _schedule.GetEndTime(prev);
            if (at >= prevStart && at < prevEnd) return prev;
            return baseDate;
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
