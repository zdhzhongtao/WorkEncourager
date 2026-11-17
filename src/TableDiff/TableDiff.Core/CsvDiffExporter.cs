using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TableDiff.Core
{
    /// <summary>
    /// 差异清单导出为 CSV。编码 UTF-8 带 BOM（否则 Excel 打开中文乱码）。
    /// 列固定：序号,类型,主键,列名,表A值,表B值,说明
    /// </summary>
    public static class CsvDiffExporter
    {
        public static readonly string[] Header = { "序号", "类型", "主键", "列名", "表A值", "表B值", "说明" };

        public static void Export(DiffResult result, string path)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (string.IsNullOrWhiteSpace(path)) throw new FriendlyException("请选择要保存的位置。");

            var sb = new StringBuilder();
            sb.Append(string.Join(",", Header)).Append("\r\n");

            int seq = 1;
            foreach (var line in EnumerateLines(result))
            {
                sb.Append(seq++).Append(',');
                sb.Append(Escape(line.Type)).Append(',');
                sb.Append(Escape(line.Key)).Append(',');
                sb.Append(Escape(line.Column)).Append(',');
                sb.Append(Escape(line.ValueA)).Append(',');
                sb.Append(Escape(line.ValueB)).Append(',');
                sb.Append(Escape(line.Note)).Append("\r\n");
            }

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(true));
            File.Move(tmp, path, true);
        }

        /// <summary>一行差异。导出 CSV 和 xlsx 共用这一份"摊平"逻辑，保证两边结论一致。</summary>
        public sealed class DiffLine
        {
            public string Type { get; set; }
            public string Key { get; set; }
            public string Column { get; set; }
            public string ValueA { get; set; }
            public string ValueB { get; set; }
            public string Note { get; set; }
            /// <summary>true = 单元格级差异（导出 xlsx 时高亮）。</summary>
            public bool IsCellChange { get; set; }
        }

        public static List<DiffLine> EnumerateLines(DiffResult result)
        {
            var lines = new List<DiffLine>();
            foreach (var row in result.Rows)
            {
                if (row.Status == RowStatus.Same) continue;

                if (row.Status == RowStatus.OnlyA || row.Status == RowStatus.OnlyB)
                {
                    bool a = row.Status == RowStatus.OnlyA;
                    lines.Add(new DiffLine
                    {
                        Type = a ? "仅A有" : "仅B有",
                        Key = row.Key,
                        Column = "（整行）",
                        ValueA = a ? "A 第 " + row.SourceRowA + " 行" : string.Empty,
                        ValueB = a ? string.Empty : "B 第 " + row.SourceRowB + " 行",
                        Note = a ? "B 表里没有这个主键" : "A 表里没有这个主键",
                        IsCellChange = false
                    });
                    continue;
                }

                for (int c = 0; c < row.Cells.Length; c++)
                {
                    if (!row.Cells[c].Changed) continue;
                    lines.Add(new DiffLine
                    {
                        Type = "字段变更",
                        Key = row.Key,
                        Column = c < result.Columns.Count ? result.Columns[c] : "列" + (c + 1),
                        ValueA = row.Cells[c].ValueA,
                        ValueB = row.Cells[c].ValueB,
                        Note = "A 第 " + row.SourceRowA + " 行 / B 第 " + row.SourceRowB + " 行",
                        IsCellChange = true
                    });
                }
            }
            return lines;
        }

        public static string Escape(string value)
        {
            if (value == null) return string.Empty;
            string text = Shield(value);

            bool needQuote = text.IndexOf(',') >= 0 || text.IndexOf('"') >= 0
                             || text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0;
            if (!needQuote) return text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// 防 CSV 注入：以 = @ 开头的单元格被 Excel 打开时会被当成公式执行（=HYPERLINK(...) 这类）。
        /// 前面加一个单引号让它老实当文本，看到的人也知道这里被处理过。
        /// 纯负数的金额（-100）不加，那是正常数据，Excel 也不会当公式。
        /// </summary>
        public static string Shield(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
            char first = value[0];

            if (first == '=' || first == '@' || first == '\t') return "'" + value;

            if ((first == '+' || first == '-') && !IsPlainNumber(value)) return "'" + value;

            return value;
        }

        private static bool IsPlainNumber(string value)
        {
            return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }

        public static string DefaultFileName(DateTime now)
        {
            return "差异清单_" + now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture) + ".csv";
        }
    }
}
