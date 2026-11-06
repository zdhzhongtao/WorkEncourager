using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WorkEnc.Core;
using Xunit;

namespace WorkEnc.Tests
{
    /// <summary>节假日日历测试。</summary>
    public class PayCalendarTests
    {
        private static readonly int[] Workdays = { 1, 2, 3, 4, 5 };

        [Fact]
        public void NewYearHoliday_Jan1To3()
        {
            var cal = new PayCalendar();
            Assert.True(cal.IsHoliday(new DateTime(2026, 1, 1)));
            Assert.True(cal.IsHoliday(new DateTime(2026, 1, 3)));
            Assert.False(cal.IsHoliday(new DateTime(2026, 1, 4)));
        }

        [Fact]
        public void SpringFestival_Feb15To23()
        {
            var cal = new PayCalendar();
            Assert.True(cal.IsHoliday(new DateTime(2026, 2, 15)));
            Assert.True(cal.IsHoliday(new DateTime(2026, 2, 23)));
            Assert.False(cal.IsHoliday(new DateTime(2026, 2, 24)));
        }

        [Fact]
        public void NationalDay_Oct1To7()
        {
            var cal = new PayCalendar();
            Assert.True(cal.IsHoliday(new DateTime(2026, 10, 1)));
            Assert.True(cal.IsHoliday(new DateTime(2026, 10, 7)));
            Assert.False(cal.IsHoliday(new DateTime(2026, 10, 8)));
        }

        [Fact]
        public void MakeupWorkdays_AreWeekendButWork()
        {
            var cal = new PayCalendar();
            // 2026-02-14 是周六，春节调休上班
            var d = new DateTime(2026, 2, 14);
            Assert.Equal(DayOfWeek.Saturday, d.DayOfWeek);
            Assert.True(cal.IsMakeupWorkday(d));
            Assert.True(cal.IsPayDay(d, Workdays));   // 周六但要上班
        }

        [Fact]
        public void MakeupWorkday_Jan4Sunday()
        {
            var cal = new PayCalendar();
            var d = new DateTime(2026, 1, 4);
            Assert.Equal(DayOfWeek.Sunday, d.DayOfWeek);
            Assert.True(cal.IsPayDay(d, Workdays));
        }

        [Fact]
        public void RegularHoliday_NotPayDay()
        {
            var cal = new PayCalendar();
            // 2026-10-01 周四，法定假日
            var d = new DateTime(2026, 10, 1);
            Assert.Equal(DayOfWeek.Thursday, d.DayOfWeek);
            Assert.False(cal.IsPayDay(d, Workdays));
        }

        [Fact]
        public void NormalWeekday_IsPayDay()
        {
            var cal = new PayCalendar();
            Assert.True(cal.IsPayDay(new DateTime(2026, 11, 4), Workdays));   // 周三
            Assert.False(cal.IsPayDay(new DateTime(2026, 11, 7), Workdays));  // 周六
        }

        [Fact]
        public void ExtraHoliday_OverridesWeekendWorkday()
        {
            var cal = new PayCalendar();
            cal.ExtraHolidays.Add("2026-02-14");
            Assert.False(cal.IsPayDay(new DateTime(2026, 2, 14), Workdays));
        }

        [Fact]
        public void ExtraWorkday_OverridesHoliday()
        {
            var cal = new PayCalendar();
            cal.ExtraWorkdays.Add("2026-10-01");
            Assert.True(cal.IsPayDay(new DateTime(2026, 10, 1), Workdays));
        }

        [Fact]
        public void CountPayDays_November2026()
        {
            var cal = new PayCalendar();
            // 2026年11月：30天，周一至周五共 21 天（无节假日、无调休）
            Assert.Equal(21, cal.CountPayDays(2026, 11, Workdays));
        }

        [Fact]
        public void CountPayDays_October2026_IncludesMakeup()
        {
            var cal = new PayCalendar();
            // 10月：工作日 22 + 调休上班 10-10(周六) = 23，但 10/1-10/7 放假(7天，其中4个工作日被扣)
            // 22 - 5(10/1,2,5,6,7 为工作日) + 1(10/10周六上班) = 18
            int n = cal.CountPayDays(2026, 10, Workdays);
            Assert.Equal(18, n);
        }

        [Fact]
        public void NextPayDay_FindsUpcoming()
        {
            var cal = new PayCalendar();
            var next = cal.NextPayDay(2026, 11, Workdays, new DateTime(2026, 11, 5));
            Assert.Equal(new DateTime(2026, 11, 5), next);
        }

        [Fact]
        public void PrevPayDay_SkipsWeekend()
        {
            var cal = new PayCalendar();
            // 11月8日是周日，往前找最近计薪日 → 11月6日(周五)
            var prev = cal.PrevPayDay(2026, 11, Workdays, new DateTime(2026, 11, 8));
            Assert.Equal(new DateTime(2026, 11, 6), prev);
        }
    }

    /// <summary>工时追踪器测试。</summary>
    public class WorkdayTrackerTests
    {
        private static Func<DateTime, (List<TimeSegment>, DateTime)> Provider(string s, string e)
        {
            return day =>
            {
                var sch = new WorkSchedule { Start = s, End = e };
                return (sch.GetWorkSegments(day), sch.GetEndTime(day));
            };
        }

        [Fact]
        public void Advance_AccumulatesWhileRunning()
        {
            var t = new WorkdayTracker();
            var day = new DateTime(2026, 11, 4);
            var p = Provider("09:00", "18:00");

            t.Advance(day.AddHours(9), p, false);
            double d = t.Advance(day.AddHours(10), p, false);

            Assert.Equal(3600, d, 1);
            Assert.Equal(3600, t.Today.WorkedSec, 1);
        }

        [Fact]
        public void Advance_NotPaused_ExcludesPausedTime()
        {
            var t = new WorkdayTracker();
            var day = new DateTime(2026, 11, 4);
            var p = Provider("09:00", "18:00");

            t.Advance(day.AddHours(9), p, false);
            double d = t.Advance(day.AddHours(10), p, true);

            Assert.Equal(0, d, 1);
            Assert.Equal(0, t.Today.WorkedSec, 1);
        }

        [Fact]
        public void Advance_OutsideWorkHours_NotCounted()
        {
            var t = new WorkdayTracker();
            var day = new DateTime(2026, 11, 4);
            var p = Provider("09:00", "18:00");

            t.Advance(day.AddHours(6), p, false);
            double d = t.Advance(day.AddHours(7), p, false);

            Assert.Equal(0, d, 1);
        }

        [Fact]
        public void Advance_LongGap_ClampsToMaxGap()
        {
            var t = new WorkdayTracker { MaxGapSeconds = 90 };
            var day = new DateTime(2026, 11, 4);
            var p = Provider("09:00", "18:00");

            t.Advance(day.AddHours(9), p, false);
            // 模拟休眠 3 小时：只应记最后 90 秒，而不是整段
            double d = t.Advance(day.AddHours(12), p, false);

            Assert.Equal(90, d, 1);
        }

        [Fact]
        public void Advance_OnlyCountsOverlapWithSegments()
        {
            var t = new WorkdayTracker();
            var day = new DateTime(2026, 11, 4);
            var p = Provider("09:00", "18:00");

            // 08:30 开始，09:30 结束 → 只有 09:00-09:30 计入
            t.Advance(day.AddHours(8).AddMinutes(30), p, false);
            double d = t.Advance(day.AddHours(9).AddMinutes(30), p, false);

            Assert.Equal(1800, d, 1);
        }

        [Fact]
        public void Advance_Overtime_Tracked()
        {
            var t = new WorkdayTracker();
            var day = new DateTime(2026, 11, 4);
            var p = Provider("09:00", "18:00");

            t.Advance(day.AddHours(17), p, false);
            t.Advance(day.AddHours(19), p, false);

            Assert.Equal(3600, t.Today.OvertimeSec, 1);
        }

        [Fact]
        public void Advance_CrossDay_SplitsByCalendarDay()
        {
            var t = new WorkdayTracker();
            var d1 = new DateTime(2026, 11, 4);
            var d2 = new DateTime(2026, 11, 5);
            var p = Provider("09:00", "18:00");

            t.Advance(d1.AddHours(10), p, false);
            t.Advance(d2.AddHours(10), p, false);

            // 11/4 记录：10:00 → 18:00 共 8 小时
            var r1 = t.Records.First(r => r.Date == "2026-11-04");
            Assert.Equal(8 * 3600, r1.WorkedSec, 1);
            // 11/5 记录：00:00 → 10:00，其中 09:00-10:00 属上班时段 → 1 小时
            var r2 = t.Records.First(r => r.Date == "2026-11-05");
            Assert.Equal(1 * 3600, r2.WorkedSec, 1);
        }

        [Fact]
        public void SaveAndLoad_RoundTrips()
        {
            var path = Path.Combine(Path.GetTempPath(), "wt_test_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var t = new WorkdayTracker();
                var day = new DateTime(2026, 11, 4);
                var p = Provider("09:00", "18:00");
                t.Advance(day.AddHours(9), p, false);
                t.Advance(day.AddHours(14), p, false);
                t.Today.Earned = 123.456;
                t.Today.FirstClockIn = "09:00";
                t.Save(path);

                var loaded = WorkdayTracker.Load(path);
                var rec = loaded.Records.First();
                Assert.Equal("2026-11-04", rec.Date);
                Assert.Equal(5 * 3600, rec.WorkedSec, 1);
                Assert.Equal(123.456, rec.Earned, 3);
                Assert.Equal("09:00", rec.FirstClockIn);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Load_NonExistent_ReturnsEmpty()
        {
            var t = WorkdayTracker.Load(Path.Combine(Path.GetTempPath(), "nope_" + Guid.NewGuid().ToString("N") + ".json"));
            Assert.Empty(t.Records);
        }
    }

    /// <summary>CSV 导出测试。</summary>
    public class CsvExporterTests
    {
        [Fact]
        public void BuildWorkReport_HasHeaderAndRows()
        {
            var recs = new List<DayRecord>
            {
                new DayRecord { Date = "2026-11-04", WorkedSec = 8 * 3600, ScheduledSec = 8 * 3600, Earned = 275.8621, FirstClockIn = "09:00", LastClockOut = "17:30" },
                new DayRecord { Date = "2026-11-05", WorkedSec = 7.5 * 3600, ScheduledSec = 8 * 3600, Earned = 258.62 },
            };
            var csv = CsvExporter.BuildWorkReport(recs, new PayCalendar(), new[] { 1, 2, 3, 4, 5 });
            var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            Assert.Contains("日期,星期,是否计薪日", lines[0]);
            Assert.Contains("2026-11-04,周三,是", lines[1]);
            Assert.Contains("8.00,8.00", lines[1]);
            Assert.Contains("275.8621", lines[1]);
            Assert.Equal(3, lines.Length);
        }

        [Fact]
        public void BuildWorkReport_QuotesFieldsWithComma()
        {
            var recs = new List<DayRecord>
            {
                new DayRecord { Date = "2026-11-04", DayNote = "调休上班, 备注" },
            };
            var csv = CsvExporter.BuildWorkReport(recs, new PayCalendar(), new[] { 1, 2, 3, 4, 5 });
            Assert.Contains("\"调休上班, 备注\"", csv);
        }

        [Fact]
        public void BuildMonthlyReport_AggregatesByMonth()
        {
            var recs = new List<DayRecord>
            {
                new DayRecord { Date = "2026-11-02", WorkedSec = 8 * 3600, Earned = 100 },
                new DayRecord { Date = "2026-11-03", WorkedSec = 8 * 3600, Earned = 100 },
                new DayRecord { Date = "2026-12-01", WorkedSec = 8 * 3600, Earned = 100 },
            };
            var csv = CsvExporter.BuildMonthlyReport(recs, new PayCalendar(), new[] { 1, 2, 3, 4, 5 });
            var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            Assert.Contains("月份,计薪日数", lines[0]);
            Assert.StartsWith("2026-11,", lines[1]);
            Assert.StartsWith("2026-12,", lines[2]);
            Assert.Contains("16.00", lines[1]);   // 两天共 16 小时
        }

        [Fact]
        public void Summarize_ComputesTotalsAndBestDay()
        {
            var recs = new List<DayRecord>
            {
                new DayRecord { Date = "2026-11-02", WorkedSec = 8 * 3600, Earned = 100, OvertimeSec = 3600 },
                new DayRecord { Date = "2026-11-03", WorkedSec = 8 * 3600, Earned = 300 },
            };
            var s = CsvExporter.Summarize(recs, new DateTime(2026, 11, 1), new DateTime(2026, 11, 30));

            Assert.Equal(2, s.DayCount);
            Assert.Equal(16 * 3600, s.TotalWorkedSec, 1);
            Assert.Equal(3600, s.TotalOvertimeSec, 1);
            Assert.Equal(400, s.TotalEarned, 3);
            Assert.Equal(8 * 3600, s.AvgWorkedSec, 1);
            Assert.Equal("2026-11-03", s.BestDayDate);
            Assert.Equal(300, s.BestDayEarned, 3);
        }

        [Fact]
        public void Summarize_ExcludesOutOfRange()
        {
            var recs = new List<DayRecord>
            {
                new DayRecord { Date = "2026-10-01", WorkedSec = 3600, Earned = 999 },
                new DayRecord { Date = "2026-11-02", WorkedSec = 8 * 3600, Earned = 100 },
            };
            var s = CsvExporter.Summarize(recs, new DateTime(2026, 11, 1), new DateTime(2026, 11, 30));
            Assert.Equal(1, s.DayCount);
            Assert.Equal(100, s.TotalEarned, 3);
        }

        [Fact]
        public void Summarize_EmptyRecordIsNotAnAttendanceDay()
        {
            // 只要程序开过，Render 就会给当天建一条空记录；它不能算成"出勤一天"，
            // 也不能进到"日均工时"的分母里
            var recs = new List<DayRecord>
            {
                new DayRecord { Date = "2026-11-02", WorkedSec = 8 * 3600, Earned = 100 },
                new DayRecord { Date = "2026-11-03", WorkedSec = 0, Earned = 0 },
                new DayRecord { Date = "2026-11-04", WorkedSec = 30, Earned = 0 },
            };
            var s = CsvExporter.Summarize(recs, new DateTime(2026, 11, 1), new DateTime(2026, 11, 30));

            Assert.Equal(1, s.DayCount);
            Assert.Equal(8 * 3600, s.AvgWorkedSec, 1);
        }

        [Fact]
        public void Summarize_RestDayOvertimeCountsAsAttended()
        {
            // 休息日加班时 WorkedSec 是 0（不在工作时段内），只有 OvertimeSec，这天也算出勤
            var recs = new List<DayRecord>
            {
                new DayRecord { Date = "2026-11-07", WorkedSec = 0, OvertimeSec = 3 * 3600, Earned = 50 },
            };
            var s = CsvExporter.Summarize(recs, new DateTime(2026, 11, 1), new DateTime(2026, 11, 30));

            Assert.Equal(1, s.DayCount);
            Assert.Equal("2026-11-07", s.BestDayDate);
        }

        [Fact]
        public void WriteFile_UsesUtf8Bom()
        {
            var path = Path.Combine(Path.GetTempPath(), "csv_" + Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                CsvExporter.WriteFile(path, "日期,金额\n2026-11-04,1.0\n");
                var bytes = File.ReadAllBytes(path);
                Assert.Equal(0xEF, bytes[0]);
                Assert.Equal(0xBB, bytes[1]);
                Assert.Equal(0xBF, bytes[2]);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }

    /// <summary>配置与迁移测试。</summary>
    public class ConfigStoreTests
    {
        [Fact]
        public void Migrate_LegacySalaryAndHours()
        {
            var cfg = new AppConfig { salary = 100, hours = 9, Start = "09:00" };
            ConfigStore.Migrate(cfg);
            Assert.Equal(2175.0, cfg.MonthlySalary.Value, 2);
            Assert.Equal("18:00", cfg.End);
        }

        [Fact]
        public void Migrate_NoLegacy_DefaultsTo6000()
        {
            var cfg = new AppConfig();
            ConfigStore.Migrate(cfg);
            Assert.Equal(6000.0, cfg.MonthlySalary.Value, 2);
        }

        [Fact]
        public void Migrate_InvalidValuesAreSanitized()
        {
            var cfg = new AppConfig { MonthlySalary = -5, Shirt = 0, Weekdays = new int[0], Start = "bad", End = "99:99", PayDay = 99 };
            ConfigStore.Migrate(cfg);
            Assert.Equal(0, cfg.MonthlySalary.Value);
            Assert.Equal(69, cfg.Shirt);
            Assert.Equal(5, cfg.Weekdays.Length);
            Assert.Equal("09:00", cfg.Start);
            Assert.Equal("18:00", cfg.End);
            Assert.Equal(10, cfg.PayDay);
        }

        [Fact]
        public void SaveAndLoad_RoundTrips()
        {
            var path = Path.Combine(Path.GetTempPath(), "cfg_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var cfg = new AppConfig
                {
                    MonthlySalary = 12000,
                    Start = "08:30",
                    End = "17:30",
                    Breaks = new[] { "12:00-13:00" },
                    Shirt = 25,
                    GoalItem = "奶茶",
                    PayDay = 15,
                    Weekdays = new[] { 1, 2, 3, 4, 5, 6 },
                    Tone = "hype",
                    Theme = "dark",
                    ClickThrough = true,
                    ExtraHolidays = new[] { "2026-12-24" }
                };
                ConfigStore.Save(path, cfg);
                var loaded = ConfigStore.Load(path);

                Assert.Equal(12000, loaded.MonthlySalary.Value);
                Assert.Equal("08:30", loaded.Start);
                Assert.Equal("奶茶", loaded.GoalItem);
                Assert.Equal(15, loaded.PayDay);
                Assert.Equal("hype", loaded.Tone);
                Assert.Equal("dark", loaded.Theme);
                Assert.True(loaded.ClickThrough);
                Assert.Equal("12:00-13:00", loaded.Breaks[0]);
                Assert.Equal("2026-12-24", loaded.ExtraHolidays[0]);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Load_NonExistent_ReturnsDefault()
        {
            var cfg = ConfigStore.Load(Path.Combine(Path.GetTempPath(), "nope_" + Guid.NewGuid().ToString("N") + ".json"));
            Assert.NotNull(cfg);
            Assert.Equal(6000, cfg.MonthlySalary.Value);
        }

        [Fact]
        public void ExportImport_RoundTrips()
        {
            var cfg = new AppConfig { MonthlySalary = 8000, GoalItem = "游戏皮肤", PayDay = 20, Tone = "savage" };
            var json = ConfigStore.ExportJson(cfg);
            var back = ConfigStore.ImportJson(json);

            Assert.NotNull(back);
            Assert.Equal(8000, back.MonthlySalary.Value);
            Assert.Equal("游戏皮肤", back.GoalItem);
            Assert.Equal(20, back.PayDay);
            Assert.Equal("savage", back.Tone);
        }

        [Fact]
        public void ImportJson_Invalid_ReturnsNull()
        {
            Assert.Null(ConfigStore.ImportJson("{ not json"));
            Assert.Null(ConfigStore.ImportJson(""));
        }

        [Fact]
        public void BuildCalendar_IncludesUserDates()
        {
            var cfg = new AppConfig { ExtraHolidays = new[] { "2026-11-20" }, ExtraWorkdays = new[] { "2026-11-21" } };
            var cal = ConfigStore.BuildCalendar(cfg);
            Assert.True(cal.IsHoliday(new DateTime(2026, 11, 20)));
            Assert.True(cal.IsMakeupWorkday(new DateTime(2026, 11, 21)));
        }
    }

    /// <summary>语录库测试。</summary>
    public class QuoteProviderTests
    {
        [Fact]
        public void ParseTone_ValidAndFallback()
        {
            Assert.Equal(Tone.Chill, QuoteProvider.ParseTone("chill"));
            Assert.Equal(Tone.Savage, QuoteProvider.ParseTone("SAVAGE"));
            Assert.Equal(Tone.Hype, QuoteProvider.ParseTone("hype"));
            Assert.Equal(Tone.Chill, QuoteProvider.ParseTone("unknown"));
            Assert.Equal(Tone.Chill, QuoteProvider.ParseTone(null));
        }

        [Fact]
        public void Get_ReplacesAllPlaceholders()
        {
            var q = new QuoteProvider(42);
            for (int i = 0; i < 50; i++)
            {
                var s = q.Get(Tone.Hype, "T恤", "3小时25分", "123.4567");
                Assert.DoesNotContain("{", s);
                Assert.DoesNotContain("}", s);
                Assert.DoesNotContain("T恤", s == null ? "null" : s.Replace("T恤", ""));  // 模板占位符已全部替换
            }
        }

        [Fact]
        public void Get_ItemSubstitutedWhenTemplateUsesIt()
        {
            var q = new QuoteProvider(3);
            // 遍历足够多次，确保至少命中一条含 {ITEM} 的模板
            bool found = false;
            for (int i = 0; i < 200 && !found; i++)
            {
                var s = q.Get(Tone.Savage, "奶茶", "1小时", "100");
                if (s.Contains("奶茶")) found = true;
                Assert.DoesNotContain("{", s);
            }
            Assert.True(found, "语录库中应存在包含目标物的模板");
        }

        [Fact]
        public void Get_DifferentTonesProduceDifferentPools()
        {
            var q = new QuoteProvider(7);
            var chill = q.Get(Tone.Chill, "T恤", "1小时", "100");
            var savage = q.Get(Tone.Savage, "T恤", "1小时", "100");
            Assert.NotEqual(chill, savage);
        }

        [Fact]
        public void Get_EmptyItemUsesFallback()
        {
            var q = new QuoteProvider(1);
            var s = q.Get(Tone.Chill, "", "1小时", "100");
            Assert.DoesNotContain("{", s);
        }

        [Fact]
        public void Get_SameSeed_ProducesSameSequence()
        {
            var a = new QuoteProvider(123);
            var b = new QuoteProvider(123);
            for (int i = 0; i < 10; i++)
                Assert.Equal(a.Get(Tone.Chill, "X", "T", "E"), b.Get(Tone.Chill, "X", "T", "E"));
        }
    }

    /// <summary>主题测试。</summary>
    public class ThemeTests
    {
        [Fact]
        public void Get_DarkAndLight()
        {
            var light = Theme.Get("light");
            var dark = Theme.Get("dark");
            Assert.NotEqual(light.ShellBg, dark.ShellBg);
            Assert.NotEqual(light.TextPrimary, dark.TextPrimary);
            Assert.Equal("light", Theme.Get(null).Name);
            Assert.Equal("dark", Theme.Get("DARK").Name);
        }
    }
}
