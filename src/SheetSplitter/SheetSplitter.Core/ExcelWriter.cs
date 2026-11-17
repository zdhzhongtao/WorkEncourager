using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;

namespace SheetSplitter.Core
{
    /// <summary>写 xlsx。先写 .tmp 再改名，中途失败不留半个坏文件。</summary>
    public static class ExcelWriter
    {
        public const int ExcelMaxRows = 1048576;

        /// <summary>写一个文件（可以含多个 sheet）。目标存在会被覆盖，调用方负责先做重名处理。</summary>
        public static void Write(string path, IList<OutputSheet> sheets)
        {
            if (sheets == null || sheets.Count == 0) throw new FriendlyException("没有要写的内容。");

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string tmp = path + ".tmp.xlsx";
            try
            {
                using (var wb = new XLWorkbook())
                {
                    var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var sheet in sheets)
                    {
                        string name = SheetName.Safe(sheet.SheetName, used);
                        var ws = wb.AddWorksheet(name);
                        int row = 1;

                        if (sheet.Headers != null && sheet.Headers.Count > 0)
                        {
                            for (int c = 0; c < sheet.Headers.Count; c++)
                                SetCell(ws, row, c + 1, sheet.Headers[c]);
                            row++;
                        }

                        for (int i = 0; i < sheet.Rows.Count; i++)
                        {
                            var cells = sheet.Rows[i].Cells;
                            for (int c = 0; c < cells.Length; c++)
                                SetCell(ws, row, c + 1, cells[c]);
                            row++;
                        }

                        if (sheet.Headers != null && sheet.Headers.Count > 0)
                        {
                            for (int c = 0; c < sheet.Headers.Count; c++)
                                ws.Cell(1, c + 1).Style.Font.Bold = true;
                            ws.SheetView.FreezeRows(1);
                        }
                    }

                    wb.SaveAs(tmp);
                }

                File.Move(tmp, path, true);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
        }

        /// <summary>能当数字就当数字写（否则 Excel 里会有一堆「文本格式的数字」告警）。</summary>
        private static void SetCell(IXLWorksheet ws, int row, int column, string text)
        {
            var cell = ws.Cell(row, column);
            if (string.IsNullOrEmpty(text)) return;

            string trimmed = text.Trim();
            if (LooksNumeric(trimmed) && double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                cell.Value = number;
            else
                cell.Value = text;
        }

        /// <summary>
        /// 判断一段文本能不能安全地写成数字。
        /// 带前导 0 的（007、0755）、超过 15 位的（订单号、身份证）、科学计数法、带正号的，
        /// 一律按文本写 —— 写成 double 会悄悄改变内容，比多一个绿色小三角严重得多。
        /// </summary>
        private static bool LooksNumeric(string s)
        {
            if (s.Length == 0) return false;
            if (s[0] == '+') return false;
            if (s.Length > 1 && s[0] == '0' && s[1] >= '0' && s[1] <= '9') return false;

            int digits = 0;
            bool dot = false;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch >= '0' && ch <= '9') { digits++; continue; }
                if (ch == '-') { if (i == 0) continue; return false; }
                if (ch == '.') { if (dot) return false; dot = true; continue; }
                return false;      // 逗号、空格、e/E 之类都不认，宁可当文本
            }

            return digits > 0 && digits <= 15;
        }
    }

    /// <summary>sheet 名清洗：不超过 31 字，去掉 Excel 不认的字符，重名自动加序号。</summary>
    public static class SheetName
    {
        private static readonly char[] Invalid = { '[', ']', ':', '*', '?', '/', '\\' };

        public static string Safe(string raw, ISet<string> used)
        {
            string name = (raw ?? string.Empty).Trim();
            foreach (var ch in Invalid) name = name.Replace(ch, '_');
            if (name.Length == 0) name = "Sheet";
            if (name.Length > 31) name = name.Substring(0, 31);

            string unique = name;
            int n = 2;
            while (used != null && used.Contains(unique))
            {
                string suffix = "(" + n++ + ")";
                unique = name.Length + suffix.Length > 31 ? name.Substring(0, 31 - suffix.Length) + suffix : name + suffix;
            }
            used?.Add(unique);
            return unique;
        }
    }
}
