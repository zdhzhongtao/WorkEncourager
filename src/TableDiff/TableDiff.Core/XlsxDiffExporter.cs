using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;

namespace TableDiff.Core
{
    /// <summary>
    /// 差异报告导出为 xlsx：两个 sheet「汇总」+「差异明细」，明细保留高亮填充。
    /// 先写 .tmp 再改名，避免中途失败留下半个坏文件。
    /// </summary>
    public static class XlsxDiffExporter
    {
        private static readonly XLColor HeadBg = XLColor.FromHtml("#E2E8F0");
        private static readonly XLColor ChangedFill = XLColor.FromHtml("#FEF3C7");
        private static readonly XLColor OnlyAFont = XLColor.FromHtml("#DC2626");
        private static readonly XLColor OnlyBFont = XLColor.FromHtml("#16A34A");

        public static void Export(DiffResult result, string path, string sourceA, string sourceB)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (string.IsNullOrWhiteSpace(path)) throw new FriendlyException("请选择要保存的位置。");

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using (var wb = new XLWorkbook())
            {
                var summary = wb.AddWorksheet("汇总");
                WriteSummary(summary, result, sourceA, sourceB);

                var detail = wb.AddWorksheet("差异明细");
                WriteDetail(detail, result);

                string tmp = path + ".tmp.xlsx";
                wb.SaveAs(tmp);
                File.Move(tmp, path, true);
            }
        }

        private static void WriteSummary(IXLWorksheet ws, DiffResult result, string sourceA, string sourceB)
        {
            ws.Cell(1, 1).Value = "指标";
            ws.Cell(1, 2).Value = "数值";
            ws.Range(1, 1, 1, 2).Style.Font.Bold = true;
            ws.Range(1, 1, 1, 2).Style.Fill.BackgroundColor = HeadBg;

            var rows = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("表 A 文件", sourceA ?? string.Empty),
                new KeyValuePair<string, object>("表 B 文件", sourceB ?? string.Empty),
                new KeyValuePair<string, object>("生成时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                new KeyValuePair<string, object>("A 行数", result.RowCountA),
                new KeyValuePair<string, object>("B 行数", result.RowCountB),
                new KeyValuePair<string, object>("相同行", result.SameCount),
                new KeyValuePair<string, object>("仅A有", result.OnlyACount),
                new KeyValuePair<string, object>("仅B有", result.OnlyBCount),
                new KeyValuePair<string, object>("有差异行数", result.ChangedRowCount),
                new KeyValuePair<string, object>("差异单元格数", result.ChangedCellCount),
                new KeyValuePair<string, object>("主键重复数", result.DuplicateKeys.Count)
            };

            int r = 2;
            foreach (var kv in rows)
            {
                ws.Cell(r, 1).Value = kv.Key;
                if (kv.Value is int i) ws.Cell(r, 2).Value = i;
                else ws.Cell(r, 2).Value = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
                r++;
            }

            if (result.DuplicateKeys.Count > 0)
            {
                r++;
                ws.Cell(r, 1).Value = "主键重复明细";
                ws.Cell(r, 1).Style.Font.Bold = true;
                r++;
                foreach (var dup in result.DuplicateKeys)
                {
                    ws.Cell(r, 1).Value = dup.ToString();
                    r++;
                }
            }

            ws.Column(1).Width = 20;
            ws.Column(2).Width = 46;
        }

        private static void WriteDetail(IXLWorksheet ws, DiffResult result)
        {
            var header = CsvDiffExporter.Header;
            for (int c = 0; c < header.Length; c++)
            {
                ws.Cell(1, c + 1).Value = header[c];
            }
            ws.Range(1, 1, 1, header.Length).Style.Font.Bold = true;
            ws.Range(1, 1, 1, header.Length).Style.Fill.BackgroundColor = HeadBg;

            var lines = CsvDiffExporter.EnumerateLines(result);
            int r = 2;
            int seq = 1;
            foreach (var line in lines)
            {
                ws.Cell(r, 1).Value = seq++;
                ws.Cell(r, 2).Value = line.Type ?? string.Empty;
                ws.Cell(r, 3).Value = line.Key ?? string.Empty;
                ws.Cell(r, 4).Value = line.Column ?? string.Empty;
                ws.Cell(r, 5).Value = line.ValueA ?? string.Empty;
                ws.Cell(r, 6).Value = line.ValueB ?? string.Empty;
                ws.Cell(r, 7).Value = line.Note ?? string.Empty;

                if (line.Type == "仅A有") ws.Cell(r, 2).Style.Font.FontColor = OnlyAFont;
                if (line.Type == "仅B有") ws.Cell(r, 2).Style.Font.FontColor = OnlyBFont;

                if (line.IsCellChange)
                {
                    ws.Cell(r, 5).Style.Fill.BackgroundColor = ChangedFill;
                    ws.Cell(r, 6).Style.Fill.BackgroundColor = ChangedFill;
                }
                r++;
            }

            ws.Column(1).Width = 6;
            ws.Column(2).Width = 10;
            ws.Column(3).Width = 26;
            ws.Column(4).Width = 18;
            ws.Column(5).Width = 28;
            ws.Column(6).Width = 28;
            ws.Column(7).Width = 26;
            ws.SheetView.FreezeRows(1);
            if (r > 2) ws.Range(2, 1, r - 1, 7).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }
    }
}
