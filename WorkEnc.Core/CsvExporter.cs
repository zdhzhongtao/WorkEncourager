using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace WorkEnc.Core
{
    /// <summary>统计区间汇总。</summary>
    public class StatsSummary
    {
        /// <summary>出勤天数（WorkedSec/OvertimeSec 达到 <see cref="DayRecord.AttendThresholdSec"/> 的天数）。</summary>
        public int DayCount { get; set; }
        public double TotalWorkedSec { get; set; }
        public double TotalOvertimeSec { get; set; }
        public double TotalEarned { get; set; }
        public double AvgWorkedSec { get; set; }
        public string BestDayDate { get; set; } = "";
        public double BestDayEarned { get; set; }
        public double BestDayWorkedSec { get; set; }
    }

    /// <summary>CSV 报表生成器（UTF-8 BOM，Excel 直接打开不乱码）。</summary>
    public class CsvExporter
    {
        /// <summary>
        /// 生成工时报表 CSV。
        /// 列：日期、星期、是否计薪日、备注、首次上班、最后下班、实际工时(小时)、应出勤(小时)、加班(小时)、当日已赚(元)
        /// </summary>
        public static string BuildWorkReport(IReadOnlyList<DayRecord> records, PayCalendar calendar, int[] weekdays)
        {
            var sb = new StringBuilder();
            sb.AppendLine("日期,星期,是否计薪日,备注,首次上班,最后下班,实际工时(小时),应出勤(小时),加班(小时),当日已赚(元)");

            foreach (var r in records.OrderBy(x => x.Date, StringComparer.Ordinal))
            {
                string dow = "";
                string isPay = "";
                string note = r.DayNote ?? "";
                if (DateTime.TryParseExact(r.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var dt))
                {
                    dow = WeekName(dt.DayOfWeek);
                    isPay = calendar.IsPayDay(dt, weekdays) ? "是" : "否";
                    if (string.IsNullOrEmpty(note))
                    {
                        if (PayCalendar.IsBuiltInHoliday(dt)) note = "法定节假日";
                        else if (PayCalendar.IsBuiltInMakeupWorkday(dt)) note = "调休上班";
                    }
                }
                sb.AppendLine(string.Join(",", new[]
                {
                    Csv(r.Date),
                    dow,
                    isPay,
                    Csv(note),
                    Csv(r.FirstClockIn ?? ""),
                    Csv(r.LastClockOut ?? ""),
                    (r.WorkedSec / 3600.0).ToString("0.00", CultureInfo.InvariantCulture),
                    (r.ScheduledSec / 3600.0).ToString("0.00", CultureInfo.InvariantCulture),
                    (r.OvertimeSec / 3600.0).ToString("0.00", CultureInfo.InvariantCulture),
                    r.Earned.ToString("0.0000", CultureInfo.InvariantCulture),
                }));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 生成月度汇总 CSV（按月一行）。
        /// </summary>
        public static string BuildMonthlyReport(IReadOnlyList<DayRecord> records, PayCalendar calendar, int[] weekdays)
        {
            var sb = new StringBuilder();
            sb.AppendLine("月份,计薪日数,出勤日数,实际工时(小时),加班(小时),本月已赚(元),日均工时(小时)");

            var groups = records
                .Where(r => !string.IsNullOrEmpty(r.Date) && r.Date.Length >= 7)
                .GroupBy(r => r.Date.Substring(0, 7))
                .OrderBy(g => g.Key, StringComparer.Ordinal);

            foreach (var g in groups)
            {
                var first = g.OrderBy(r => r.Date, StringComparer.Ordinal).First();
                if (!DateTime.TryParseExact(first.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var dt)) continue;

                int payDays = calendar.CountPayDays(dt.Year, dt.Month, weekdays);
                // 与 HistoryAnalyzer 的出勤口径保持一致：达到阈值才算一天，别把"开过机"算成出勤
                int attendDays = g.Count(r => r.IsAttended);
                double totalWorked = g.Sum(r => r.WorkedSec);
                double totalOt = g.Sum(r => r.OvertimeSec);
                double totalEarned = g.Sum(r => r.Earned);
                double avg = attendDays > 0 ? totalWorked / attendDays / 3600.0 : 0;

                sb.AppendLine(string.Join(",", new[]
                {
                    g.Key,
                    payDays.ToString(CultureInfo.InvariantCulture),
                    attendDays.ToString(CultureInfo.InvariantCulture),
                    (totalWorked / 3600.0).ToString("0.00", CultureInfo.InvariantCulture),
                    (totalOt / 3600.0).ToString("0.00", CultureInfo.InvariantCulture),
                    totalEarned.ToString("0.0000", CultureInfo.InvariantCulture),
                    avg.ToString("0.00", CultureInfo.InvariantCulture),
                }));
            }
            return sb.ToString();
        }

        /// <summary>写入文件（UTF-8 BOM）。</summary>
        public static void WriteFile(string path, string content)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, content, new UTF8Encoding(true));
        }

        /// <summary>
        /// 按区间汇总统计。
        ///
        /// DayCount / AvgWorkedSec 都按"真的上过班的天"算（见 DayRecord.IsAttended）：
        /// 记录是程序运行时每天自动建的，只开过程序没干活的空记录不该算进"出勤 N 天"，
        /// 也不该拉低"日均工时"。金额与工时的合计仍然包含全部记录，不会漏掉零星时长。
        /// </summary>
        public static StatsSummary Summarize(IReadOnlyList<DayRecord> records, DateTime from, DateTime to)
        {
            var list = records.Where(r =>
                DateTime.TryParseExact(r.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt) && dt >= from.Date && dt <= to.Date).ToList();

            var attended = list.Where(r => r.IsAttended).ToList();

            var sum = new StatsSummary
            {
                DayCount = attended.Count,
                TotalWorkedSec = list.Sum(r => r.WorkedSec),
                TotalOvertimeSec = list.Sum(r => r.OvertimeSec),
                TotalEarned = list.Sum(r => r.Earned),
            };
            // 日均工时 = 出勤那些天的工时 / 出勤天数。
            // 不用 TotalWorkedSec 做分子：那里面还有"没到出勤门槛"的零星秒数，
            // 摊到日均里会让数字虚高一点点，口径也不干净。
            sum.AvgWorkedSec = attended.Count > 0 ? attended.Sum(r => r.WorkedSec) / attended.Count : 0;

            // 最赚的一天只在真正出勤的日子里挑；全都没出勤时退回全部记录，至少给个最大值
            var bestPool = attended.Count > 0 ? attended : list;
            var best = bestPool.OrderByDescending(r => r.Earned).FirstOrDefault();
            if (best != null)
            {
                sum.BestDayDate = best.Date;
                sum.BestDayEarned = best.Earned;
                sum.BestDayWorkedSec = best.WorkedSec;
            }
            return sum;
        }

        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static string WeekName(DayOfWeek d)
        {
            switch (d)
            {
                case DayOfWeek.Monday: return "周一";
                case DayOfWeek.Tuesday: return "周二";
                case DayOfWeek.Wednesday: return "周三";
                case DayOfWeek.Thursday: return "周四";
                case DayOfWeek.Friday: return "周五";
                case DayOfWeek.Saturday: return "周六";
                default: return "周日";
            }
        }
    }
}
