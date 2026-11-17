using System;
using System.Collections.Generic;
using System.Linq;
using WorkEnc.Core;
using Xunit;

namespace WorkEnc.Tests
{
    /// <summary>
    /// v2.2.2 修复回归测试。
    ///
    /// 每一条对应一个被查出来的缺陷：
    /// P0-1 跨天班次工时整段丢失 / P0-2 整晚挂机凭空记加班 /
    /// P0-3 窗口隐藏时当天金额不写回 / P0-4 导入器表头子串误命中，
    /// 以及 P1 的夜班凌晨不计薪、负数分钟格式化、假日/调休冲突、CSV 口径、午休覆盖全天、深色模式配色。
    /// </summary>
    public class RegressionTests
    {
        private static readonly int[] Workdays = { 1, 2, 3, 4, 5 };

        private static Func<DateTime, (List<TimeSegment>, DateTime)> Provider(WorkSchedule sch)
        {
            return day => (sch.GetWorkSegments(day), sch.GetEndTime(day));
        }

        // ==================== P0-1 跨天班次工时整段丢失 ====================

        [Fact]
        public void NightShift_CrossMidnight_KeepsWholeShift()
        {
            // 22:00 → 06:00 的夜班，在场 5 小时（22:00 → 03:00）必须记满 5 小时。
            // v2.2.1 按自然日切分，零点以后那 3 小时无处安放，只记了 2 小时。
            var sch = new WorkSchedule { Start = "22:00", End = "06:00" };
            var tracker = new WorkdayTracker();
            var p = Provider(sch);

            var day = new DateTime(2026, 11, 4);
            tracker.Advance(day.AddHours(22), p, false);
            tracker.Advance(day.AddDays(1).AddHours(3), p, false);

            var rec = tracker.Records.Single();
            Assert.Equal("2026-11-04", rec.Date);          // 整段都归到班次开始那天
            Assert.Equal(5 * 3600, rec.WorkedSec, 1);
            Assert.Equal(8 * 3600, rec.ScheduledSec, 1);
            Assert.Equal("22:00", rec.FirstClockIn);
            Assert.Equal("03:00", rec.LastClockOut);
        }

        [Fact]
        public void NightShift_Overtime_NotHalvedAcrossMidnight()
        {
            // 下班 06:00，干到 08:00 → 2 小时加班。
            // v2.2.1 跨零点后换了"当天"的作息，加班被腰斩。
            var sch = new WorkSchedule { Start = "22:00", End = "06:00" };
            var tracker = new WorkdayTracker();
            var p = Provider(sch);

            var day = new DateTime(2026, 11, 4);
            tracker.Advance(day.AddHours(22), p, false);
            tracker.Advance(day.AddDays(1).AddHours(8), p, false);

            var rec = tracker.Records.Single();
            Assert.Equal("2026-11-04", rec.Date);
            Assert.Equal(8 * 3600, rec.WorkedSec, 1);
            Assert.Equal(2 * 3600, rec.OvertimeSec, 1);
            Assert.Equal("08:00", rec.LastClockOut);
        }

        [Fact]
        public void NormalShift_AcrossMidnight_StillBelongsToEachDay()
        {
            // 普通班次不受影响：11/4 记 8 小时，11/5 记 1 小时。
            var sch = new WorkSchedule { Start = "09:00", End = "18:00" };
            var tracker = new WorkdayTracker();
            var p = Provider(sch);

            var d1 = new DateTime(2026, 11, 4);
            tracker.Advance(d1.AddHours(10), p, false);
            tracker.Advance(d1.AddDays(1).AddHours(10), p, false);

            Assert.Equal(8 * 3600, tracker.Records.First(r => r.Date == "2026-11-04").WorkedSec, 1);
            Assert.Equal(1 * 3600, tracker.Records.First(r => r.Date == "2026-11-05").WorkedSec, 1);
        }

        // ==================== P0-2 整晚挂机凭空记加班 ====================

        [Fact]
        public void IdleGuard_StopsOvertimeFromGrowing()
        {
            // 下班 18:00 就走了，程序一直挂到次日 09:00。
            // v2.2.1 按墙钟凭空记了 6 小时加班。
            var sch = new WorkSchedule { Start = "09:00", End = "18:00" };
            var tracker = new WorkdayTracker
            {
                IdleThresholdSeconds = 300,
                // 一路挂着没人碰过键盘鼠标
                IdleSecondsProvider = () => 15 * 3600
            };
            var p = Provider(sch);

            var day = new DateTime(2026, 11, 4);
            tracker.Advance(day.AddHours(17), p, false);
            tracker.Advance(day.AddHours(19), p, false);
            tracker.Advance(day.AddDays(1).AddHours(9), p, false);

            var rec = tracker.Records.First(r => r.Date == "2026-11-04");
            Assert.True(rec.OvertimeSec < 5 * 60,
                "空闲保护生效后不该再冒出成片的加班，实际 " + rec.OvertimeSec + " 秒");
        }

        [Fact]
        public void IdleGuard_Off_KeepsCountingOvertime()
        {
            // 不开空闲保护时行为不变：真正在加班就照记。
            var sch = new WorkSchedule { Start = "09:00", End = "18:00" };
            var tracker = new WorkdayTracker { IdleThresholdSeconds = 0 };
            var p = Provider(sch);

            var day = new DateTime(2026, 11, 4);
            tracker.Advance(day.AddHours(17), p, false);
            tracker.Advance(day.AddHours(19), p, false);

            Assert.Equal(3600, tracker.Records.First().OvertimeSec, 1);
        }

        [Fact]
        public void IdleGuard_ActiveUser_StillCountsOvertime()
        {
            // 人还在（空闲时长很小）时，加班必须照常累计。
            var sch = new WorkSchedule { Start = "09:00", End = "18:00" };
            var tracker = new WorkdayTracker
            {
                IdleThresholdSeconds = 300,
                IdleSecondsProvider = () => 2
            };
            var p = Provider(sch);

            var day = new DateTime(2026, 11, 4);
            tracker.Advance(day.AddHours(17), p, false);
            tracker.Advance(day.AddHours(19), p, false);

            Assert.Equal(3600, tracker.Records.First().OvertimeSec, 1);
        }

        // ==================== P0-3 窗口隐藏时当天金额不写回 ====================

        [Fact]
        public void SyncDayStats_WritesEarnedEvenWhenWindowHidden()
        {
            // 挂在托盘里时界面不刷新，但数据必须照样写回，
            // 否则导出 CSV 时当天"已赚"是 0。
            var tracker = new WorkdayTracker();
            var day = new DateTime(2026, 11, 4);

            var rec = tracker.SyncDayStats(day, 8 * 3600, 275.8621, true, "");

            Assert.Equal("2026-11-04", rec.Date);
            Assert.Equal(275.8621, rec.Earned, 4);
            Assert.Equal(8 * 3600, rec.ScheduledSec, 1);
            Assert.True(rec.IsPayDay);
        }

        // ==================== P0-4 导入器表头子串误命中 ====================

        [Fact]
        public void Import_NoteColumn_IsNotMistakenForOvertime()
        {
            // "note" 里含 "ot"：v2.2.1 会把备注列当成加班列，备注内容被当成加班小时数。
            string csv = "Date,Hours,Note\r\n2026-11-05,8,ok\r\n";
            var errors = new List<string>();
            var rows = WorkLogImporter.Parse(csv, errors);

            Assert.Single(rows);
            Assert.Equal("ok", rows[0].Note);
            Assert.Null(rows[0].OvertimeHours);
            Assert.Equal(8, rows[0].WorkedHours.Value, 3);
        }

        [Fact]
        public void Import_DailyPayColumn_IsNotMistakenForDate()
        {
            // "日薪" 里含 "日"：v2.2.1 会把它当成日期列。
            string csv = "日薪,工时\r\n300,8\r\n";
            var errors = new List<string>();
            var rows = WorkLogImporter.Parse(csv, errors);

            Assert.Empty(rows);   // 没有真正的日期列，一行都读不进来
            Assert.NotEmpty(errors);
        }

        [Fact]
        public void Import_OvertimeColumn_BeforeWorkedColumn()
        {
            // 表头顺序是"加班工时,工时"时，加班列不能被"工时"的包含匹配抢走。
            string csv = "日期,加班工时,工时\r\n2026-11-05,1.5,8\r\n";
            var errors = new List<string>();
            var rows = WorkLogImporter.Parse(csv, errors);

            Assert.Single(rows);
            Assert.Equal(1.5, rows[0].OvertimeHours.Value, 3);
            Assert.Equal(8, rows[0].WorkedHours.Value, 3);
        }

        [Fact]
        public void Import_ShortAbbreviation_OnlyMatchesWholeWord()
        {
            // 表头就叫 "OT" 时要认；"Note" 不行。
            string csv = "日期,工时,OT,备注\r\n2026-11-05,8,2,说明\r\n";
            var errors = new List<string>();
            var rows = WorkLogImporter.Parse(csv, errors);

            Assert.Single(rows);
            Assert.Equal(2, rows[0].OvertimeHours.Value, 3);
            Assert.Equal(8, rows[0].WorkedHours.Value, 3);
            Assert.Equal("说明", rows[0].Note);
        }

        // ==================== P1 夜班凌晨不计薪 ====================

        [Fact]
        public void NightShift_EarlyMorning_CountsAsWork()
        {
            // 凌晨 02:00 还属于前一天开始的夜班：工时、金额、状态都要算。
            // v2.2.1 用 now.Date 查"今天"的作息（22:00 才上班），凌晨一分钱都不算。
            var sch = new WorkSchedule { Start = "22:00", End = "06:00" };
            var calc = new SalaryCalculator(sch, new PayCalendar());

            var at = new DateTime(2026, 11, 5).AddHours(2);   // 11/5 02:00
            var snap = calc.Compute(at, 2175, Workdays, 10, false, 0);

            Assert.Equal(new DateTime(2026, 11, 4), snap.BaseDate);   // 归属 11/4 那一班
            Assert.Equal(4 * 3600, snap.WorkedSec, 1);               // 22:00 → 02:00
            Assert.True(snap.Earned > 0, "凌晨的工时也要算钱");
            Assert.Equal("working", snap.Status);
        }

        [Fact]
        public void NightShift_AfterShiftEnd_UsesTodayShift()
        {
            // 07:00 已经下班了：这时的班次是"今天"（22:00 才开始），状态是待上班。
            var sch = new WorkSchedule { Start = "22:00", End = "06:00" };
            var calc = new SalaryCalculator(sch, new PayCalendar());

            var at = new DateTime(2026, 11, 5).AddHours(7);
            var snap = calc.Compute(at, 2175, Workdays, 10, false, 0);

            Assert.Equal(new DateTime(2026, 11, 5), snap.BaseDate);
            Assert.Equal("wait", snap.Status);
            Assert.Equal(0, snap.WorkedSec, 1);
        }

        [Fact]
        public void NormalDay_EarlyMorning_IsNotCounted()
        {
            // 普通班次凌晨两点当然是"待上班"，不能被夜班规则带偏。
            var sch = new WorkSchedule { Start = "09:00", End = "18:00" };
            var calc = new SalaryCalculator(sch, new PayCalendar());

            var at = new DateTime(2026, 11, 5).AddHours(2);
            var snap = calc.Compute(at, 2175, Workdays, 10, false, 0);

            Assert.Equal(new DateTime(2026, 11, 5), snap.BaseDate);
            Assert.Equal("wait", snap.Status);
            Assert.Equal(0, snap.WorkedSec, 1);
        }

        // ==================== P1 FormatMinutes 负值 ====================

        [Fact]
        public void FormatMinutes_Negative_DoesNotProduceBrokenText()
        {
            // v2.2.1 只对小时做了归一，分钟没做：-30 会输出 "23:-30"。
            string s = WorkSchedule.FormatMinutes(-30);
            Assert.DoesNotContain("-", s);
            Assert.Equal("23:30", s);
            Assert.Equal("00:00", WorkSchedule.FormatMinutes(0));
            Assert.Equal("09:00", WorkSchedule.FormatMinutes(540));
            Assert.Equal("23:59", WorkSchedule.FormatMinutes(1439));
            Assert.Equal("00:00", WorkSchedule.FormatMinutes(1440));
        }

        // ==================== P1 假日 / 调休冲突 ====================

        [Fact]
        public void PayCalendar_FindConflicts_ReportsDatesInBothSets()
        {
            var cal = new PayCalendar();
            cal.ExtraHolidays.Add("2027-01-01");
            cal.ExtraWorkdays.Add("2027-01-01");
            cal.ExtraHolidays.Add("2027-02-01");

            var conflicts = cal.FindConflicts();
            Assert.Single(conflicts);
            Assert.Equal("2027-01-01", conflicts[0]);
        }

        [Fact]
        public void PayCalendar_NoConflict_ReturnsEmpty()
        {
            var cal = new PayCalendar();
            cal.ExtraHolidays.Add("2027-01-01");
            cal.ExtraWorkdays.Add("2027-01-04");
            Assert.Empty(cal.FindConflicts());
        }

        // ==================== P1 CSV 口径 ====================

        [Fact]
        public void Csv_Header_DistinguishesPresenceFromEarnings()
        {
            // "实际工时"是程序在场时长，"当日已赚"是按作息表推算的金额，
            // 两者口径不同，表头必须说清楚。
            var recs = new List<DayRecord>
            {
                new DayRecord { Date = "2026-11-04", WorkedSec = 8 * 3600, Earned = 100 }
            };
            var csv = CsvExporter.BuildWorkReport(recs, new PayCalendar(), Workdays);
            var header = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];

            Assert.Contains("在场工时(小时)", header);
            Assert.Contains("当日已赚(元)", header);
            Assert.DoesNotContain("实际工时", header);
        }

        // ==================== P1 午休覆盖全天 ====================

        [Fact]
        public void Schedule_FullBreakCoverage_IsDetected()
        {
            // 午休填成整个工作时段：计薪工时直接归零，日薪全丢且一点异常都看不出来。
            var sch = new WorkSchedule
            {
                Start = "09:00",
                End = "18:00",
                Breaks = new List<string> { "09:00-18:00" }
            };
            Assert.True(sch.IsFullyCoveredByBreaks(new DateTime(2026, 11, 4)));
            Assert.Equal(0, sch.GetTotalWorkSeconds(new DateTime(2026, 11, 4)), 1);
        }

        [Fact]
        public void Schedule_NormalBreak_IsNotFullCoverage()
        {
            var sch = new WorkSchedule
            {
                Start = "09:00",
                End = "18:00",
                Breaks = new List<string> { "12:00-13:00" }
            };
            Assert.False(sch.IsFullyCoveredByBreaks(new DateTime(2026, 11, 4)));
            Assert.Equal(8 * 3600, sch.GetTotalWorkSeconds(new DateTime(2026, 11, 4)), 1);
        }

        [Fact]
        public void Schedule_NoBreak_IsNotFullCoverage()
        {
            var sch = new WorkSchedule { Start = "09:00", End = "18:00" };
            Assert.False(sch.IsFullyCoveredByBreaks(new DateTime(2026, 11, 4)));
        }

        // ==================== P1 深色模式配色 ====================

        [Fact]
        public void Theme_ToResourceMap_CoversEveryStyleColor()
        {
            // XAML 里用到的每一个 DynamicResource 键都必须在主题里有一份值，
            // 少一个就是一处颜色在深色模式下直接"消失"。
            var map = Theme.Dark().ToResourceMap();

            string[] required =
            {
                "ThemeShellBg", "ThemeShellBorder", "ThemeCardBg", "ThemeCardBorder",
                "ThemeTextPrimary", "ThemeTextSecondary", "ThemeTextMuted", "ThemeTextFaint",
                "ThemeAccent", "ThemeAccentDark", "ThemeAccentSoft", "ThemeAccentSoftBorder",
                "ThemeEncBg", "ThemeEncBorder", "ThemeEncText",
                "ThemeBarBg", "ThemeDivider",
                "ThemeInputBg", "ThemeInputBorder", "ThemeInputFg", "ThemeInputFocusBg",
                "ThemeBorderHover", "ThemeGhostBg", "ThemeGhostBorder", "ThemeGhostFg",
                "ThemeGhostHoverBg", "ThemeHoverBg", "ThemePressedBg", "ThemeFieldLabelFg",
                "ThemeDanger", "ThemeWarning",
                "ThemeScrollThumb", "ThemeScrollThumbHover", "ThemeTrackBg", "ThemeThumbStroke",
                "ThemeTabBarBg", "ThemeTabActiveBg", "ThemeTabActiveBorder",
                "ThemeTabActiveFg", "ThemeTabIdleFg",
                "ThemeChipWorkingBg", "ThemeChipWorkingFg", "ThemeChipWorkingDot",
                "ThemeChipOffBg", "ThemeChipOffFg", "ThemeChipOffDot",
                "ThemeChipWaitBg", "ThemeChipWaitFg", "ThemeChipWaitDot",
                "ThemeChipPausedBg", "ThemeChipPausedFg", "ThemeChipPausedDot",
            };

            foreach (var key in required)
            {
                Assert.True(map.ContainsKey(key), "缺少主题键：" + key);
                Assert.False(string.IsNullOrWhiteSpace(map[key]), "主题键没有值：" + key);
            }
        }

        [Fact]
        public void Theme_Dark_AndLight_DifferOnEveryMappedKey()
        {
            // 浅色和深色必须真的不一样，否则"切换主题"只是换个名字。
            var light = Theme.Light().ToResourceMap();
            var dark = Theme.Dark().ToResourceMap();

            Assert.Equal(light.Count, dark.Count);
            foreach (var kv in light)
            {
                Assert.True(dark.ContainsKey(kv.Key), "深色主题缺少：" + kv.Key);
                Assert.True(kv.Value != dark[kv.Key], "深浅主题颜色相同：" + kv.Key);
            }
        }
    }
}
