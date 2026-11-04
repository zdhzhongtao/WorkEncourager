using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkEnc.Core
{
    /// <summary>单日工作记录。</summary>
    public class DayRecord
    {
        /// <summary>日期 yyyy-MM-dd。</summary>
        public string Date { get; set; } = "";

        /// <summary>实际在场工作秒数（程序运行且处于上班时段内累计）。</summary>
        public double WorkedSec { get; set; }

        /// <summary>当日应出勤秒数（按作息表，扣午休）。</summary>
        public double ScheduledSec { get; set; }

        /// <summary>当日加班秒数（超出下班时间仍在工作）。</summary>
        public double OvertimeSec { get; set; }

        /// <summary>当日已赚金额。</summary>
        public double Earned { get; set; }

        /// <summary>当日是计薪日。</summary>
        public bool IsPayDay { get; set; }

        /// <summary>是否为节假日/调休上班日描述。</summary>
        public string DayNote { get; set; } = "";

        /// <summary>首次上班时刻 HH:mm。</summary>
        public string FirstClockIn { get; set; } = "";

        /// <summary>最后下班时刻 HH:mm。</summary>
        public string LastClockOut { get; set; } = "";
    }

    /// <summary>
    /// 工作时长追踪器。
    /// 核心约定：**程序运行期间处于上班时段内，就认为在正常工作**，
    /// 因此可跨重启累计真实在场时长，用于生成 CSV 工时报表。
    /// </summary>
    public class WorkdayTracker
    {
        private readonly Dictionary<string, DayRecord> _records = new(StringComparer.Ordinal);

        /// <summary>最近一次 tick 的真实时刻（用于计算增量）。</summary>
        private DateTime _lastTick = DateTime.MinValue;

        /// <summary>当前正在累计的日期。</summary>
        public string CurrentDate { get; private set; } = "";

        /// <summary>所有记录（按日期升序）。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public IReadOnlyList<DayRecord> Records =>
            _records.Values.OrderBy(r => r.Date, StringComparer.Ordinal).ToList();

        /// <summary>原始记录字典，供序列化使用。</summary>
        [System.Text.Json.Serialization.JsonInclude]
        private Dictionary<string, DayRecord> RecordsForSerialization
        {
            get => _records;
            set { if (value != null) { _records.Clear(); foreach (var kv in value) _records[kv.Key] = kv.Value; } }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public DayRecord Today => GetOrCreate(CurrentDate);

        public DayRecord GetOrCreate(string date)
        {
            if (!_records.TryGetValue(date, out var rec))
            {
                rec = new DayRecord { Date = date };
                _records[date] = rec;
            }
            return rec;
        }

        /// <summary>
        /// 推进一次追踪。返回本次新增的工作秒数。
        /// 跨天时增量按自然日切分归属到各自日期，不会把凌晨的工时记到前一天。
        /// </summary>
        /// <param name="now">当前真实时刻</param>
        /// <param name="provider">按日期提供当日工作时段与下班时刻</param>
        /// <param name="paused">
        /// 是否跳过本次区间的计时。语义是"这一整段都没在上班"，
        /// 所以暂停时必须先用 paused=false 把到暂停时刻为止的工时结算掉，再切到暂停状态。
        /// </param>
        public double Advance(DateTime now, Func<DateTime, (List<TimeSegment> segs, DateTime endTs)> provider, bool paused)
        {
            CurrentDate = now.ToString("yyyy-MM-dd");

            if (_lastTick == DateTime.MinValue)
            {
                _lastTick = now;
                return 0;
            }

            DateTime from = _lastTick;
            if (now <= from)
            {
                _lastTick = now;
                return 0;
            }
            _lastTick = now;

            if (paused || provider == null) return 0;

            double totalWorked = 0;
            DateTime cursor = from;

            // 按自然日切分，逐日归属
            while (cursor < now)
            {
                var dayEnd = cursor.Date.AddDays(1);
                var chunkEnd = now < dayEnd ? now : dayEnd;
                var day = cursor.Date;

                var daySchedule = provider(day);
                var segs = daySchedule.segs;
                var endTs = daySchedule.endTs;
                if (segs != null && segs.Count > 0)
                {
                    // 应出勤工时由追踪器自己记录，历史日期也能保留（否则 CSV 应出勤列会是 0）
                    double scheduled = 0;
                    foreach (var seg in segs) scheduled += seg.Seconds;

                    double worked = 0;
                    foreach (var seg in segs)
                    {
                        var s = cursor > seg.Start ? cursor : seg.Start;
                        var e = chunkEnd < seg.End ? chunkEnd : seg.End;
                        if (e > s) worked += (e - s).TotalSeconds;
                    }

                    var rec = GetOrCreate(day.ToString("yyyy-MM-dd"));
                    rec.ScheduledSec = scheduled;

                    if (worked > 0)
                    {
                        rec.WorkedSec += worked;
                        totalWorked += worked;

                        string hm = chunkEnd.ToString("HH:mm");
                        if (string.IsNullOrEmpty(rec.FirstClockIn)) rec.FirstClockIn = cursor.ToString("HH:mm");
                        rec.LastClockOut = hm;
                    }

                    // 加班：超出下班时间的部分
                    DateTime otFrom = cursor > endTs ? cursor : endTs;
                    if (chunkEnd > otFrom)
                    {
                        rec.OvertimeSec += (chunkEnd - otFrom).TotalSeconds;
                    }
                }

                cursor = chunkEnd;
            }

            return totalWorked;
        }

        /// <summary>把当天记录补齐统计信息（应出勤、已赚、计薪日标记等）。</summary>
        public void FillDayStats(DayRecord rec, double scheduledSec, double earned, bool isPayDay, string note)
        {
            rec.ScheduledSec = scheduledSec;
            rec.Earned = earned;
            rec.IsPayDay = isPayDay;
            rec.DayNote = note;
        }

        // ---------------- 持久化 ----------------

        public void Save(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });
                File.WriteAllText(path, json);
            }
            catch { }
        }

        public static WorkdayTracker Load(string path)
        {
            var t = new WorkdayTracker();
            try
            {
                if (!File.Exists(path)) return t;
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<WorkdayTracker>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (loaded == null) return t;
                t._records.Clear();
                foreach (var kv in loaded._records) t._records[kv.Key] = kv.Value;
                // _lastTick 不恢复，重启后从"此刻"开始计时，不追补关机期间的空档
                t._lastTick = DateTime.MinValue;
            }
            catch { }
            return t;
        }
    }
}
