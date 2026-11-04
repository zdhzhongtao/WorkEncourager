using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WorkEnc.Core;
using Xunit;

namespace WorkEnc.Tests
{
    /// <summary>
    /// 端到端测试：模拟"开着就在工作"的一天，验证 CSV 报表内容正确。
    /// </summary>
    public class EndToEndWorkReportTests
    {
        private static readonly int[] Workdays = { 1, 2, 3, 4, 5 };

        private static Func<DateTime, (List<TimeSegment>, DateTime)> Provider(WorkSchedule sch)
        {
            return day => (sch.GetWorkSegments(day), sch.GetEndTime(day));
        }

        [Fact]
        public void FullDay_ProducesCorrectCsvRow()
        {
            var sch = new WorkSchedule { Start = "09:00", End = "18:00" };
            var tracker = new WorkdayTracker();
            var p = Provider(sch);
            var cal = new PayCalendar();

            // 2026-11-04 周三：从 9:00 一直开到 18:30（加班 30 分钟）
            var day = new DateTime(2026, 11, 4);
            tracker.Advance(day.AddHours(9), p, false);
            tracker.Advance(day.AddHours(12), p, false);
            tracker.Advance(day.AddHours(18).AddMinutes(30), p, false);

            var rec = tracker.Records.Single();
            Assert.Equal("2026-11-04", rec.Date);
            Assert.Equal(9 * 3600, rec.WorkedSec, 1);          // 工作 9 小时
            Assert.Equal(30 * 60, rec.OvertimeSec, 1);         // 加班 0.5 小时
            Assert.Equal("09:00", rec.FirstClockIn);
            Assert.Equal("18:30", rec.LastClockOut);

            // 导出 CSV
            var csv = CsvExporter.BuildWorkReport(tracker.Records, cal, Workdays);
            var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            Assert.Contains("日期,星期,是否计薪日", lines[0]);
            Assert.Contains("2026-11-04,周三,是", lines[1]);
            Assert.Contains("09:00,18:30", lines[1]);
            Assert.Contains("9.00,9.00,0.50", lines[1]);   // 实际9h / 应出勤9h / 加班0.5h
        }

        [Fact]
        public void LunchBreak_ExcludedFromWorkedSeconds()
        {
            var sch = new WorkSchedule { Start = "09:00", End = "18:00", Breaks = new List<string> { "12:00-13:00" } };
            var tracker = new WorkdayTracker();
            var p = Provider(sch);
            var day = new DateTime(2026, 11, 4);

            tracker.Advance(day.AddHours(9), p, false);
            tracker.Advance(day.AddHours(18), p, false);

            var rec = tracker.Records.Single();
            // 09:00-12:00 (3h) + 13:00-18:00 (5h) = 8 小时，午休 1 小时不计
            Assert.Equal(8 * 3600, rec.WorkedSec, 1);
        }

        [Fact]
        public void PausedPeriod_NotCountedInReport()
        {
            var sch = new WorkSchedule { Start = "09:00", End = "18:00" };
            var tracker = new WorkdayTracker();
            var p = Provider(sch);
            var day = new DateTime(2026, 11, 4);

            tracker.Advance(day.AddHours(9), p, false);
            tracker.Advance(day.AddHours(12), p, false);   // 9:00-12:00 正常工作
            tracker.Advance(day.AddHours(13), p, true);    // 12:00-13:00 暂停，不计时
            tracker.Advance(day.AddHours(18), p, false);

            var rec = tracker.Records.Single();
            Assert.Equal(8 * 3600, rec.WorkedSec, 1);   // 9-12 + 13-18 = 8 小时
            Assert.Equal(9 * 3600, rec.ScheduledSec, 1); // 应出勤仍是 9 小时
        }

        [Fact]
        public void ThreeDayReport_HasOneRowPerDay()
        {
            var sch = new WorkSchedule { Start = "09:00", End = "18:00" };
            var tracker = new WorkdayTracker();
            var p = Provider(sch);

            // 11/3 周二、11/4 周三、11/5 周四
            for (int d = 3; d <= 5; d++)
            {
                var day = new DateTime(2026, 11, d);
                tracker.Advance(day.AddHours(9), p, false);
                tracker.Advance(day.AddHours(17), p, false);
            }

            var recs = tracker.Records.ToList();
            Assert.Equal(3, recs.Count);
            Assert.Equal("2026-11-03", recs[0].Date);
            Assert.Equal("2026-11-05", recs[2].Date);

            var csv = CsvExporter.BuildWorkReport(recs, new PayCalendar(), Workdays);
            var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(4, lines.Length);   // 表头 + 3 行
        }

        [Fact]
        public void WeekendDay_IsMarkedAsNonPayDay()
        {
            var sch = new WorkSchedule { Start = "09:00", End = "18:00" };
            var tracker = new WorkdayTracker();
            var p = Provider(sch);
            // 2026-11-07 是周六
            var day = new DateTime(2026, 11, 7);
            tracker.Advance(day.AddHours(9), p, false);
            tracker.Advance(day.AddHours(15), p, false);

            var csv = CsvExporter.BuildWorkReport(tracker.Records, new PayCalendar(), Workdays);
            Assert.Contains("2026-11-07,周六,否", csv);
        }

        [Fact]
        public void NationalDay_Holiday_IsMarkedWithNote()
        {
            var sch = new WorkSchedule { Start = "09:00", End = "18:00" };
            var tracker = new WorkdayTracker();
            var p = Provider(sch);
            // 2026-10-01 法定节假日
            var day = new DateTime(2026, 10, 1);
            tracker.Advance(day.AddHours(9), p, false);
            tracker.Advance(day.AddHours(15), p, false);

            var csv = CsvExporter.BuildWorkReport(tracker.Records, new PayCalendar(), Workdays);
            Assert.Contains("2026-10-01,周四,否", csv);
            Assert.Contains("法定节假日", csv);
        }

        [Fact]
        public void MakeupWorkday_Weekend_IsMarkedAsPayDay()
        {
            var sch = new WorkSchedule { Start = "09:00", End = "18:00" };
            var tracker = new WorkdayTracker();
            var p = Provider(sch);
            // 2026-10-10 周六调休上班
            var day = new DateTime(2026, 10, 10);
            tracker.Advance(day.AddHours(9), p, false);
            tracker.Advance(day.AddHours(15), p, false);

            var csv = CsvExporter.BuildWorkReport(tracker.Records, new PayCalendar(), Workdays);
            Assert.Contains("2026-10-10,周六,是", csv);
            Assert.Contains("调休上班", csv);
        }

        [Fact]
        public void MonthlySummary_MatchesDailyRows()
        {
            var recs = new List<DayRecord>();
            for (int d = 2; d <= 6; d++)
            {
                recs.Add(new DayRecord
                {
                    Date = $"2026-11-{d:00}",
                    WorkedSec = 8 * 3600,
                    ScheduledSec = 9 * 3600,
                    Earned = 88.8889
                });
            }
            var csv = CsvExporter.BuildMonthlyReport(recs, new PayCalendar(), Workdays);
            var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            Assert.StartsWith("2026-11,21,5,40.00,0.00,444.4445,8.00", lines[1]);
        }

        [Fact]
        public void SaveHistory_ThenReload_AndExportAgain()
        {
            var path = Path.Combine(Path.GetTempPath(), "hist_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var sch = new WorkSchedule { Start = "09:00", End = "18:00" };
                var tracker = new WorkdayTracker();
            var p = Provider(sch);
                var day = new DateTime(2026, 11, 4);
                tracker.Advance(day.AddHours(9), p, false);
                tracker.Advance(day.AddHours(13), p, false);
                tracker.Save(path);

                // 重启后重新加载，历史仍在
                var reloaded = WorkdayTracker.Load(path);
                var csv = CsvExporter.BuildWorkReport(reloaded.Records, new PayCalendar(), Workdays);
                Assert.Contains("2026-11-04,周三,是", csv);
                Assert.Contains("4.00", csv);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }

    /// <summary>语录库语气行为测试。</summary>
    public class QuoteToneTests
    {
        [Fact]
        public void DifferentTones_DifferentTemplates()
        {
            var chill = new QuoteProvider(1).Get(Tone.Chill, QuoteScene.FirstItem, "T恤", "3小时", "100");
            var savage = new QuoteProvider(1).Get(Tone.Savage, QuoteScene.FirstItem, "T恤", "3小时", "100");
            var hype = new QuoteProvider(1).Get(Tone.Hype, QuoteScene.FirstItem, "T恤", "3小时", "100");
            Assert.NotEqual(chill, savage);
            Assert.NotEqual(savage, hype);
            Assert.NotEqual(chill, hype);
        }

        [Fact]
        public void AllScenesAndTones_ProduceValidText()
        {
            var q = new QuoteProvider(99);
            foreach (Tone tone in Enum.GetValues(typeof(Tone)))
            {
                foreach (QuoteScene scene in Enum.GetValues(typeof(QuoteScene)))
                {
                    for (int i = 0; i < 10; i++)
                    {
                        var s = q.Get(tone, scene, "T恤", "3小时25分", "123.45", 2, "0.75");
                        Assert.DoesNotContain("{", s);
                        Assert.DoesNotContain("}", s);
                        Assert.False(string.IsNullOrWhiteSpace(s));
                    }
                }
            }
        }

        [Fact]
        public void CountAndCanBuy_AreSubstituted()
        {
            var q = new QuoteProvider(5);
            string text = null;
            for (int i = 0; i < 200; i++)
            {
                text = q.Get(Tone.Hype, QuoteScene.RepeatItem, "T恤", "3小时", "123.45", 7, "0.5");
                if (text.Contains("7")) break;
            }
            Assert.Contains("7", text);
            Assert.DoesNotContain("{", text);
        }

        [Fact]
        public void Rotation_CoversAllTemplatesInPool()
        {
            var q = new QuoteProvider(11);
            var seen = new HashSet<string>();
            for (int i = 0; i < 200; i++)
            {
                var s = q.Get(Tone.Chill, QuoteScene.FirstItem, "T恤", "1小时", "100");
                seen.Add(s);
            }
            // 语录池该场景有 4 条模板，长期抽样应能覆盖到全部
            Assert.True(seen.Count >= 4, "实际覆盖到 " + seen.Count + " 条模板");
        }

        [Fact]
        public void Rotation_DoesNotRepeatImmediately()
        {
            var q = new QuoteProvider(11);
            string prev = null;
            for (int i = 0; i < 100; i++)
            {
                var s = q.Get(Tone.Chill, QuoteScene.FirstItem, "T恤", "1小时", "100");
                if (prev != null) Assert.NotEqual(prev, s);   // 相邻两次不应重复
                prev = s;
            }
        }
    }
}
