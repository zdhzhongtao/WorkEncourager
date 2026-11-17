using System;
using System.Collections.Generic;
using System.Globalization;

namespace WorkEnc.Core
{
    /// <summary>
    /// 每日作息安排：支持午休等多段休息。
    /// 例：09:00-12:00, 13:00-18:00 → 实际计薪工时 8 小时。
    /// </summary>
    public class WorkSchedule
    {
        /// <summary>上班时间（HH:mm）。</summary>
        public string Start { get; set; } = "09:00";

        /// <summary>下班时间（HH:mm）。</summary>
        public string End { get; set; } = "18:00";

        /// <summary>休息时段（午休等），格式 "HH:mm-HH:mm"。</summary>
        public List<string> Breaks { get; set; } = new List<string>();

        /// <summary>
        /// 计算当天的有效工作时段（已扣除休息段）。
        /// 返回的每个时段都是绝对时间（以 baseDate 当天 00:00 为基准），跨天时结束时间会 &gt; 24 小时。
        /// </summary>
        public List<TimeSegment> GetWorkSegments(DateTime baseDate)
        {
            var result = new List<TimeSegment>();
            if (!TryParseMinutes(Start, out int sMin) || !TryParseMinutes(End, out int eMin)) return result;

            var day = baseDate.Date;
            int startMin = sMin;
            int endMin = eMin;
            if (endMin <= startMin) endMin += 24 * 60;   // 支持跨天，如 22:00 → 06:00

            // 解析休息段（相对起始时间的偏移分钟）
            var breaks = new List<(int From, int To)>();
            foreach (var b in Breaks)
            {
                if (string.IsNullOrWhiteSpace(b)) continue;
                var parts = b.Split('-');
                if (parts.Length != 2) continue;
                if (!TryParseMinutes(parts[0].Trim(), out int bf)) continue;
                if (!TryParseMinutes(parts[1].Trim(), out int bt)) continue;
                if (bt <= bf) bt += 24 * 60;
                if (bf < startMin) bf += 24 * 60;
                if (bt < startMin) bt += 24 * 60;
                breaks.Add((bf, bt));
            }

            // 扣除休息段，得到实际工作区间
            int cursor = startMin;
            var ordered = breaks;
            ordered.Sort((x, y) => x.From.CompareTo(y.From));
            foreach (var (bf, bt) in ordered)
            {
                if (bt <= cursor || bf >= endMin) continue;
                if (bf > cursor)
                    result.Add(new TimeSegment(day.AddMinutes(cursor), day.AddMinutes(Math.Min(bf, endMin))));
                cursor = Math.Max(cursor, bt);
            }
            if (cursor < endMin)
                result.Add(new TimeSegment(day.AddMinutes(cursor), day.AddMinutes(endMin)));

            return result;
        }

        /// <summary>当天的计薪总秒数（已扣除休息段）。</summary>
        public double GetTotalWorkSeconds(DateTime baseDate)
        {
            double total = 0;
            foreach (var seg in GetWorkSegments(baseDate))
                total += (seg.End - seg.Start).TotalSeconds;
            return total;
        }

        /// <summary>
        /// 截至 now 为止，已实际工作（或已过）的秒数。
        /// </summary>
        public double GetElapsedWorkSeconds(DateTime baseDate, DateTime now)
        {
            double total = 0;
            foreach (var seg in GetWorkSegments(baseDate))
            {
                var from = seg.Start;
                var to = now < seg.End ? now : seg.End;
                if (to > from) total += (to - from).TotalSeconds;
            }
            return total;
        }

        /// <summary>当日下班时刻（含跨天）。</summary>
        public DateTime GetEndTime(DateTime baseDate)
        {
            if (!TryParseMinutes(Start, out int sMin) || !TryParseMinutes(End, out int eMin))
                return baseDate.Date.AddHours(18);
            if (eMin <= sMin) eMin += 24 * 60;
            return baseDate.Date.AddMinutes(eMin);
        }

        /// <summary>当日上班时刻。</summary>
        public DateTime GetStartTime(DateTime baseDate)
        {
            if (!TryParseMinutes(Start, out int sMin)) return baseDate.Date.AddHours(9);
            return baseDate.Date.AddMinutes(sMin);
        }

        /// <summary>解析 "HH:mm" 为当天分钟数（0-1439）。</summary>
        public static bool TryParseMinutes(string s, out int minutes)
        {
            minutes = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            var parts = s.Trim().Replace('：', ':').Split(':');
            if (parts.Length != 2) return false;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)) return false;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int m)) return false;
            if (h < 0 || h > 23 || m < 0 || m > 59) return false;
            minutes = h * 60 + m;
            return true;
        }

        /// <summary>把分钟数格式化为 "HH:mm"。</summary>
        public static string FormatMinutes(int minutes)
        {
            // 先整体归一到 0-1439，再拆时分。
            // 以前只对小时做了归一，分钟没做：传 -30 会输出 "23:-30" 这种鬼话。
            int total = ((minutes % 1440) + 1440) % 1440;
            int h = total / 60;
            int m = total % 60;
            return h.ToString("00") + ":" + m.ToString("00");
        }

        /// <summary>
        /// 当天是否"有上班时段，却被休息段整个盖掉"。
        ///
        /// 这种情况很隐蔽：作息表看起来填得挺全，但计薪工时是 0，
        /// 日薪算不出来也不报错，界面上只显示"今日已赚 ¥0.0000"。
        /// 设置页要显式拦下来告诉用户。
        /// </summary>
        public bool IsFullyCoveredByBreaks(DateTime baseDate)
        {
            if (!TryParseMinutes(Start, out int sMin) || !TryParseMinutes(End, out int eMin)) return false;

            bool hasBreak = false;
            foreach (var b in Breaks)
            {
                if (!string.IsNullOrWhiteSpace(b)) { hasBreak = true; break; }
            }
            if (!hasBreak) return false;

            // 上下班时间一样（或者是反着的）时本来就没有工时，不算"被盖住"
            int endMin = eMin;
            if (endMin <= sMin) endMin += 24 * 60;
            if (endMin <= sMin) return false;

            return GetWorkSegments(baseDate).Count == 0;
        }
    }

    /// <summary>一个工作时段。</summary>
    public struct TimeSegment
    {
        public DateTime Start { get; }
        public DateTime End { get; }

        public TimeSegment(DateTime start, DateTime end)
        {
            Start = start;
            End = end;
        }

        public double Seconds => (End - Start).TotalSeconds;
    }
}
