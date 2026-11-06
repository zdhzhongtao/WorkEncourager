using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WorkEnc.Core
{
    /// <summary>从外部文件读进来的一天工时。</summary>
    public class ImportedDay
    {
        /// <summary>日期，统一 yyyy-MM-dd。</summary>
        public string Date = "";
        /// <summary>首次上班 HH:mm（可为空）。</summary>
        public string FirstClockIn = "";
        /// <summary>最后下班 HH:mm（可为空）。</summary>
        public string LastClockOut = "";
        /// <summary>实际工时（小时），null 表示文件里没有。</summary>
        public double? WorkedHours;
        /// <summary>加班工时（小时），null 表示文件里没有。</summary>
        public double? OvertimeHours;
        /// <summary>备注。</summary>
        public string Note = "";
    }

    /// <summary>
    /// 外部工时文件的解析器（纯逻辑，可单测）。
    ///
    /// 约定：CSV 或 TSV，UTF-8。第一行可以是表头（中英文都认），也可以直接是数据。
    /// 最少只要一列「日期 + 工时」，其余列有就带上、没有就算了。
    ///
    /// 典型文件：
    /// <code>
    /// 日期,首次上班,最后下班,实际工时(小时),加班(小时),备注
    /// 2026-11-05,09:02,18:30,8.50,0.50,
    /// </code>
    /// </summary>
    public static class WorkLogImporter
    {
        private static readonly string[] DateKeys = { "日期", "date", "日", "打卡日期", "workdate" };
        private static readonly string[] InKeys = { "上班", "首次上班", "上班时间", "签到", "in", "clockin", "checkin", "首次打卡" };
        private static readonly string[] OutKeys = { "下班", "最后下班", "下班时间", "签退", "out", "clockout", "checkout", "最后打卡" };
        private static readonly string[] WorkedKeys = { "工时", "实际工时", "小时", "hours", "worked", "workhours", "时长" };
        private static readonly string[] OvertimeKeys = { "加班", "加班工时", "overtime", "ot" };
        private static readonly string[] NoteKeys = { "备注", "note", "remark", "memo", "说明" };

        /// <summary>
        /// 解析文本。无法识别的行会写进 errors（最多 20 条），返回成功解析出来的行。
        /// </summary>
        public static List<ImportedDay> Parse(string text, IList<string> errors)
        {
            var list = new List<ImportedDay>();
            if (string.IsNullOrWhiteSpace(text)) return list;

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
                            .Split('\n')
                            .Where(l => !string.IsNullOrWhiteSpace(l))
                            .ToList();
            if (lines.Count == 0) return list;

            char sep = DetectSeparator(lines[0]);
            var header = SplitLine(lines[0], sep);
            int firstDataLine = 1;
            int[] map;

            // 第一行含"日期"之类的词就当成表头，否则认为直接是数据
            bool hasHeader = header.Any(c => Match(c, DateKeys));
            if (hasHeader)
            {
                map = BuildMap(header);
            }
            else
            {
                // 无表头：拿第一行数据猜列含义（"日期,工时" 这种两列最常见）
                map = InferMap(header);
                firstDataLine = 0;
            }

            for (int i = firstDataLine; i < lines.Count; i++)
            {
                var cells = SplitLine(lines[i], sep);
                try
                {
                    string dateText = Cell(cells, map[0]);
                    if (!TryParseDate(dateText, out var date)) { AddError(errors, i + 1, dateText); continue; }

                    var d = new ImportedDay { Date = date.ToString("yyyy-MM-dd") };

                    string inText = Cell(cells, map[1]);
                    if (WorkSchedule.TryParseMinutes(inText, out int inMin))
                        d.FirstClockIn = WorkSchedule.FormatMinutes(inMin);

                    string outText = Cell(cells, map[2]);
                    if (WorkSchedule.TryParseMinutes(outText, out int outMin))
                        d.LastClockOut = WorkSchedule.FormatMinutes(outMin);

                    string workedText = Cell(cells, map[3]);
                    d.WorkedHours = ParseHours(workedText);

                    // 只给了上下班时间没给工时：自己算，跨天（夜班）也能算对
                    if (!d.WorkedHours.HasValue &&
                        WorkSchedule.TryParseMinutes(inText, out int a) &&
                        WorkSchedule.TryParseMinutes(outText, out int b))
                    {
                        if (b <= a) b += 24 * 60;
                        d.WorkedHours = (b - a) / 60.0;
                    }

                    d.OvertimeHours = ParseHours(Cell(cells, map[4]));
                    d.Note = (Cell(cells, map[5]) ?? "").Trim();

                    if (!d.WorkedHours.HasValue && string.IsNullOrEmpty(d.FirstClockIn) && string.IsNullOrEmpty(d.LastClockOut))
                    {
                        AddError(errors, i + 1, "这一行既没有工时也没有上下班时间");
                        continue;
                    }
                    list.Add(d);
                }
                catch (Exception ex)
                {
                    AddError(errors, i + 1, ex.Message);
                }
            }
            return list;
        }

        private static void AddError(IList<string> errors, int line, string why)
        {
            if (errors == null || errors.Count >= 20) return;
            errors.Add("第 " + line + " 行：" + why);
        }

        /// <summary>逗号或制表符，看第一行哪个多。</summary>
        private static char DetectSeparator(string line)
        {
            int comma = line.Count(c => c == ',' || c == '，');
            int tab = line.Count(c => c == '\t');
            return tab > comma ? '\t' : ',';
        }

        /// <summary>切分一行，支持双引号包裹（备注里有逗号时不会切错）。</summary>
        private static List<string> SplitLine(string line, char sep)
        {
            var cells = new List<string>();
            if (line == null) { cells.Add(""); return cells; }

            var sb = new System.Text.StringBuilder();
            bool quoted = false;
            foreach (char ch in line)
            {
                if (ch == '"') { quoted = !quoted; continue; }
                bool isSep = (ch == sep) || (sep == ',' && ch == '，');
                if (isSep && !quoted) { cells.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(ch);
            }
            cells.Add(sb.ToString());
            return cells;
        }

        /// <summary>
        /// 没有表头时按第一行数据推断列含义：
        /// 2 列 → 日期,工时
        /// 3 列且后两列都是时间 → 日期,上班,下班
        /// 3 列其他情况 → 日期,工时,备注
        /// 4 列以上 → 日期,上班,下班,工时,加班,备注
        /// </summary>
        private static int[] InferMap(List<string> sample)
        {
            var map = new[] { 0, -1, -1, -1, -1, -1 };
            if (sample == null) return map;

            if (sample.Count == 2)
            {
                if (WorkSchedule.TryParseMinutes(sample[1], out _)) map[1] = 1;  // 只有上班时间
                else map[3] = 1;                                                  // 当工时
            }
            else if (sample.Count == 3)
            {
                bool c1 = WorkSchedule.TryParseMinutes(sample[1], out _);
                bool c2 = WorkSchedule.TryParseMinutes(sample[2], out _);
                if (c1 && c2) { map[1] = 1; map[2] = 2; }   // 日期,上班,下班
                else { map[3] = 1; map[5] = 2; }            // 日期,工时,备注
            }
            else if (sample.Count >= 4)
            {
                map = new[] { 0, 1, 2, 3, 4, 5 };
            }
            return map;
        }

        private static bool Match(string cell, string[] keys)
        {
            if (string.IsNullOrWhiteSpace(cell)) return false;
            string c = cell.Trim().ToLowerInvariant().Replace(" ", "");
            return keys.Any(k => c.Contains(k));
        }

        /// <summary>表头 → 列下标；没有的列给 -1。</summary>
        private static int[] BuildMap(List<string> header)
        {
            int Find(string[] keys)
            {
                for (int i = 0; i < header.Count; i++)
                    if (Match(header[i], keys)) return i;
                return -1;
            }
            return new[]
            {
                Find(DateKeys), Find(InKeys), Find(OutKeys),
                Find(WorkedKeys), Find(OvertimeKeys), Find(NoteKeys)
            };
        }

        private static string Cell(List<string> cells, int index)
        {
            if (index < 0 || index >= cells.Count) return "";
            return (cells[index] ?? "").Trim();
        }

        /// <summary>支持 8.5 / 8.5h / 8:30 三种写法。</summary>
        public static double? ParseHours(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim().TrimEnd('h', 'H', '时', '小', '时');

            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v) &&
                v >= 0 && v < 48)
                return v;

            // 8:30 = 8.5 小时
            if (WorkSchedule.TryParseMinutes(s, out int minutes))
                return minutes / 60.0;

            return null;
        }

        /// <summary>支持 2026-11-05 / 2026/11/5 / 20261105 / 11-05（按今年补全）。</summary>
        public static bool TryParseDate(string s, out DateTime date)
        {
            date = default;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();

            string[] formats =
            {
                "yyyy-MM-dd", "yyyy/M/d", "yyyy-M-d", "yyyy.MM.dd", "yyyyMMdd",
                "M/d/yyyy", "M-d-yyyy", "yyyy年M月d日"
            };
            if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out date))
                return date.Year > 2000;

            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return date.Year > 2000;

            // 只给了"11-05"这种，按今年补
            if (DateTime.TryParseExact(s, new[] { "M-d", "M/d" }, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var md))
            {
                date = new DateTime(DateTime.Now.Year, md.Month, md.Day);
                return true;
            }
            return false;
        }
    }
}
