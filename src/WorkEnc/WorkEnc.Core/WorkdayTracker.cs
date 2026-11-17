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

        /// <summary>
        /// 判定"这天真的上过班"的门槛（秒）。不足 1 分钟算没上班。
        ///
        /// 记录是 Render 每天 GetOrCreate 出来的，只要程序开过就会有一条空记录。
        /// 如果按"有记录的天数"统计出勤，光开机不干活也会被算成出勤，
        /// 「本月出勤 N 天」就会虚高。因此所有出勤口径统一用这个阈值。
        /// </summary>
        public const double AttendThresholdSec = 60;

        /// <summary>
        /// 当天是否算出勤：实际工时或加班达到阈值。
        /// 加班也要算——休息日来加班时 WorkedSec 是 0（不在工作时段内），只有 OvertimeSec。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsAttended => WorkedSec > AttendThresholdSec || OvertimeSec > AttendThresholdSec;
    }

    /// <summary>
    /// 工作时长追踪器。
    /// 核心约定：**程序运行期间处于上班时段内，就认为在正常工作**，
    /// 因此可跨重启累计真实在场时长，用于生成 CSV 工时报表。
    /// </summary>
    public class WorkdayTracker
    {
        private readonly Dictionary<string, DayRecord> _records = new(StringComparer.Ordinal);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// 两次 tick 之间允许的最大间隔（秒）。超过就认为机器睡过 / 时钟跳变，
        /// 只把最后这么多秒计入工作，而不是把整段休眠算成干活。
        ///
        /// 默认 0 = 关闭（保持纯计算语义）；应用层会把它设为 90 秒来开启保护。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double MaxGapSeconds { get; set; }

        /// <summary>最近一次 tick 的真实时刻（用于计算增量）。</summary>
        private DateTime _lastTick = DateTime.MinValue;

        /// <summary>
        /// 系统空闲秒数的提供者：返回"距最近一次键鼠输入过了多少秒"。
        /// null = 视为人一直都在岗（纯计算语义：单元测试、离线统计都走这条）。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public Func<double> IdleSecondsProvider { get; set; }

        /// <summary>
        /// 空闲超过多少秒就算"人已经离岗"（秒）。0 = 关闭这层保护。
        ///
        /// 程序一直挂着不等于人一直在岗。下班忘了关程序时，按墙钟会把一整晚
        /// 都算成加班（v2.2.1 的 bug：18:00 挂到次日 09:00，凭空记 6 小时）。
        /// 有了这层保护，超过阈值没有键鼠输入就不再累计加班。
        ///
        /// 注意：只挡"加班"，不挡正常工时——正常工时沿用"开着就在岗"的约定，
        /// 否则读代码时长时间不动鼠标会被误判成离岗。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double IdleThresholdSeconds { get; set; }

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

        /// <summary>该日期是否已有记录（导入时用来区分"新增"还是"更新"）。</summary>
        public bool HasDate(string date) => !string.IsNullOrEmpty(date) && _records.ContainsKey(date);

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
        ///
        /// 归属规则是**按班次起始日**，不是按自然日。
        /// 作息表允许跨天（22:00 → 次日 06:00），这种班次的工时必须整体记在
        /// 开始那天：v2.2.1 按自然日切分，零点以后的那几个小时无处安放，
        /// 夜班 22:00→06:00 在场 5 小时只记 2 小时。
        ///
        /// 加班算在"本班次下班之后、下一个班次开始之前"的这段空档里，
        /// 这样跨零点加班也不会再被腰斩（以前 22:00→02:00 只记 1 小时）。
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

            // 睡眠 / 休眠 / 系统时间跳变：不允许把整段空档算成工时。
            // 只认最后 MaxGapSeconds 秒，其余丢弃（与"关机不追补"的语义保持一致）。
            double gap = (now - from).TotalSeconds;
            if (MaxGapSeconds > 0 && gap > MaxGapSeconds)
            {
                from = now.AddSeconds(-MaxGapSeconds);
            }

            _lastTick = now;

            if (paused || provider == null) return 0;

            // 空闲保护：程序挂着不等于人还在岗。
            // 以最后一次键鼠输入为界，把加班的累计截止到那一刻（再加一点宽限）。
            DateTime presenceEnd = now;
            double idle = IdleSecondsProvider != null ? IdleSecondsProvider() : 0;
            if (IdleThresholdSeconds > 0 && idle >= IdleThresholdSeconds)
            {
                presenceEnd = now.AddSeconds(-idle + IdleThresholdSeconds);
                if (presenceEnd < from) presenceEnd = from;
            }

            double totalWorked = 0;

            // 班次可能跨天，所以要回看一天、一直看到当天，
            // 逐个"班次起始日"归属工时与加班。
            for (DateTime baseDate = from.Date.AddDays(-1); baseDate <= now.Date; baseDate = baseDate.AddDays(1))
            {
                var daySchedule = provider(baseDate);
                var segs = daySchedule.segs;
                var endTs = daySchedule.endTs;
                if (segs == null || segs.Count == 0) continue;

                // 应出勤工时由追踪器自己记录，历史日期也能保留（否则 CSV 应出勤列会是 0）
                double scheduled = 0;
                foreach (var seg in segs) scheduled += seg.Seconds;

                // 在场工时：与本班次工作时段的交集
                double worked = 0;
                DateTime firstAt = DateTime.MinValue, lastAt = DateTime.MinValue;
                foreach (var seg in segs)
                {
                    var s = from > seg.Start ? from : seg.Start;
                    var e = now < seg.End ? now : seg.End;
                    if (e > s)
                    {
                        worked += (e - s).TotalSeconds;
                        if (firstAt == DateTime.MinValue || s < firstAt) firstAt = s;
                        if (e > lastAt) lastAt = e;
                    }
                }

                // 加班：本班次下班之后、下一个班次开始之前、且人确实还在场的那段
                double overtime = 0;
                DateTime otEnd = DateTime.MinValue;
                if (presenceEnd > from)
                {
                    var otFrom = from > endTs ? from : endTs;
                    var otTo = presenceEnd < now ? presenceEnd : now;
                    var nextStart = NextShiftStart(provider, baseDate);
                    if (nextStart.HasValue && nextStart.Value < otTo) otTo = nextStart.Value;
                    if (otTo > otFrom)
                    {
                        overtime = (otTo - otFrom).TotalSeconds;
                        otEnd = otTo;
                    }
                }

                // 一点工时一点加班都没有，就别给这天凭空造记录
                if (worked <= 0 && overtime <= 0) continue;

                var rec = GetOrCreate(baseDate.ToString("yyyy-MM-dd"));
                rec.ScheduledSec = scheduled;

                if (worked > 0)
                {
                    rec.WorkedSec += worked;
                    totalWorked += worked;

                    if (string.IsNullOrEmpty(rec.FirstClockIn)) rec.FirstClockIn = firstAt.ToString("HH:mm");
                    rec.LastClockOut = lastAt.ToString("HH:mm");
                }

                if (overtime > 0)
                {
                    rec.OvertimeSec += overtime;
                    // 下班后还在干，最后下班时间要跟到加班结束的那一刻
                    if (otEnd > lastAt) rec.LastClockOut = otEnd.ToString("HH:mm");
                }
            }

            return totalWorked;
        }

        /// <summary>
        /// 下一个班次的开始时刻（起始日 + 1 天那班）。没有则返回 null。
        /// 用来把本班次的加班截断在下一次上班之前。
        /// </summary>
        private static DateTime? NextShiftStart(Func<DateTime, (List<TimeSegment> segs, DateTime endTs)> provider, DateTime baseDate)
        {
            var next = provider(baseDate.AddDays(1));
            if (next.segs == null || next.segs.Count == 0) return null;

            DateTime min = DateTime.MaxValue;
            foreach (var seg in next.segs)
                if (seg.Start < min) min = seg.Start;

            return min == DateTime.MaxValue ? (DateTime?)null : min;
        }

        /// <summary>把当天记录补齐统计信息（应出勤、已赚、计薪日标记等）。</summary>
        public void FillDayStats(DayRecord rec, double scheduledSec, double earned, bool isPayDay, string note)
        {
            rec.ScheduledSec = scheduledSec;
            rec.Earned = earned;
            rec.IsPayDay = isPayDay;
            rec.DayNote = note;
        }

        /// <summary>
        /// 把某个班次日的统计信息写回记录，并返回该记录。
        ///
        /// 窗口隐藏在托盘里的时候也必须调用：
        /// 不然挂一整天再导出 CSV，当天"已赚"永远是 0（v2.2.1 的 bug）。
        /// </summary>
        /// <param name="shiftDate">班次归属日（夜班凌晨属于前一天开始的那一班）。</param>
        public DayRecord SyncDayStats(DateTime shiftDate, double scheduledSec, double earned, bool isPayDay, string note)
        {
            var rec = GetOrCreate(shiftDate.ToString("yyyy-MM-dd"));
            rec.ScheduledSec = scheduledSec;
            rec.Earned = earned;
            rec.IsPayDay = isPayDay;
            rec.DayNote = note ?? "";
            return rec;
        }

        // ---------------- 持久化 ----------------

        public void Save(string path)
        {
            try
            {
                AtomicFile.WriteJson(path, this, JsonOptions);
            }
            catch (Exception ex)
            {
                AtomicFile.ErrorHandler?.Invoke("工时记录保存失败: " + path, ex);
            }
        }

        public static WorkdayTracker Load(string path)
        {
            var t = AtomicFile.ReadJson<WorkdayTracker>(path, JsonOptions);
            if (t == null) return new WorkdayTracker();
            // _lastTick 不恢复，重启后从"此刻"开始计时，不追补关机期间的空档
            t._lastTick = DateTime.MinValue;
            return t;
        }
    }
}
