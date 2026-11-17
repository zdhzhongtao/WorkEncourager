using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ClosedXML.Excel;

namespace SheetSplitter.Core
{
    /// <summary>用 ClosedXML 读 xlsx / xlsm。读不出的单元格降级成公式文本，不中断整表。</summary>
    public static class ExcelReader
    {
        /// <summary>遇到读不出的单元格时回调（接到日志上）。</summary>
        public static Action<string, Exception> CellErrorHandler;
        /// <summary>进度回调：已处理行数 / 总行数。</summary>
        public static Action<int, int> Progress;
        /// <summary>取消令牌（长任务可中断）。</summary>
        public static CancellationToken CancelToken;

        public static List<string> ListSheets(string path)
        {
            using (var wb = Open(path))
            {
                var names = new List<string>();
                foreach (var ws in wb.Worksheets) names.Add(ws.Name);
                return names;
            }
        }

        public static List<SheetData> ReadAll(string path)
        {
            var result = new List<SheetData>();
            using (var wb = Open(path))
            {
                foreach (var ws in wb.Worksheets)
                {
                    CancelToken.ThrowIfCancellationRequested();
                    result.Add(ReadSheet(ws));
                }
            }
            return result;
        }

        public static XLWorkbook Open(string path)
        {
            try
            {
                return new XLWorkbook(path);
            }
            catch (IOException ex)
            {
                throw new FriendlyException("文件被 Excel 占用了，请先关闭再试。", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new FriendlyException("没有权限读这个文件，请检查文件属性和所在目录。", ex);
            }
            catch (Exception ex)
            {
                throw new FriendlyException("这个文件读不出来，可能不是有效的 Excel 文件，或者文件已损坏。", ex);
            }
        }

        public static SheetData ReadSheet(IXLWorksheet ws)
        {
            var sheet = new SheetData { Name = ws.Name };

            var lastRow = ws.LastRowUsed();
            if (lastRow == null) return sheet;

            int maxRow = lastRow.RowNumber();
            var lastCol = ws.LastColumnUsed();
            int maxCol = lastCol == null ? 1 : lastCol.ColumnNumber();
            if (maxRow <= 0 || maxCol <= 0) return sheet;

            // 表格末尾常常有「只剩格式、没有内容」的空行空列（有人点过整列设置格式）。
            // 不剪掉的话会按 Excel 的物理尺寸读，白白吃掉几百 MB 内存。
            while (maxRow > 1 && ws.Row(maxRow).IsEmpty()) maxRow--;
            while (maxCol > 1 && ws.Column(maxCol).IsEmpty()) maxCol--;

            // 兜底：真遇到超大表，先给个能看懂的提示，而不是让程序被内存吃掉
            long totalCells = (long)maxRow * maxCol;
            if (totalCells > 40_000_000L)
                throw new FriendlyException("这张表有 " + maxRow.ToString("N0") + " 行 × " + maxCol.ToString("N0") +
                    " 列，超出本工具能处理的范围（约 4000 万个单元格）。建议先在 Excel 里删掉多余的行列再比对。");

            var raw = new List<string[]>(maxRow);
            for (int r = 1; r <= maxRow; r++)
            {
                if ((r & 0x3FF) == 0)
                {
                    CancelToken.ThrowIfCancellationRequested();
                    Progress?.Invoke(r, maxRow);
                }

                var row = ws.Row(r);
                var cells = new string[maxCol];
                for (int c = 1; c <= maxCol; c++)
                    cells[c - 1] = ReadCell(row.Cell(c));
                raw.Add(cells);
            }

            Progress?.Invoke(maxRow, maxRow);
            return SheetData.FromRows(ws.Name, raw, true);
        }

        /// <summary>单元格 → 用户能看的文本。</summary>
        public static string ReadCell(IXLCell cell)
        {
            try
            {
                if (cell == null) return string.Empty;
                if (!cell.HasFormula && cell.IsEmpty()) return string.Empty;

                switch (cell.DataType)
                {
                    case XLDataType.Blank:
                        return cell.HasFormula ? "=" + SafeFormula(cell) : string.Empty;
                    case XLDataType.DateTime:
                        return CellText.FormatDate(cell.GetDateTime());
                    case XLDataType.TimeSpan:
                        return CellText.FormatTimeSpan(cell.GetTimeSpan());
                    case XLDataType.Number:
                        return CellText.FormatNumber(cell.GetDouble());
                    case XLDataType.Boolean:
                        return cell.GetBoolean() ? "TRUE" : "FALSE";
                    case XLDataType.Text:
                        {
                            string s = cell.GetString();
                            if (s.Length == 0 && cell.HasFormula) return "=" + SafeFormula(cell);
                            return s;
                        }
                    case XLDataType.Error:
                        return cell.GetError().ToString();
                    default:
                        return cell.GetFormattedString();
                }
            }
            catch (Exception ex)
            {
                // 公式没有缓存值、自定义格式怪：降级成公式原文，不中断
                string fallback;
                try { fallback = cell.HasFormula ? "=" + SafeFormula(cell) : string.Empty; }
                catch { fallback = string.Empty; }
                try { CellErrorHandler?.Invoke("读单元格失败 " + SafeAddress(cell) + "，已降级显示", ex); } catch { }
                return fallback;
            }
        }

        private static string SafeFormula(IXLCell cell)
        {
            try { return cell.FormulaA1 ?? string.Empty; } catch { return string.Empty; }
        }

        private static string SafeAddress(IXLCell cell)
        {
            try { return cell.Address.ToString(); } catch { return "?"; }
        }
    }
}
