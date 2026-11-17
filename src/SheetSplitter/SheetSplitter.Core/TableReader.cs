using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ClosedXML.Excel;

namespace SheetSplitter.Core
{
    /// <summary>按扩展名分派到 Excel / CSV 读取器。所有错误都翻成人话。</summary>
    public static class TableReader
    {
        public static readonly string[] SupportedExtensions = { ".xlsx", ".xlsm", ".csv", ".tsv" };

        public static bool IsSupported(string path)
        {
            string ext = (Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();
            for (int i = 0; i < SupportedExtensions.Length; i++)
                if (SupportedExtensions[i] == ext) return true;
            return false;
        }

        /// <summary>读文件里所有 sheet。csv/tsv 只会有 1 个。</summary>
        public static TableFile Load(string path,
                                    Action<int, int> progress = null,
                                    CancellationToken token = default,
                                    Action<string, Exception> cellError = null)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new FriendlyException("请先选择要打开的文件。");
            if (!File.Exists(path)) throw new FriendlyException("找不到这个文件：" + path);

            string ext = (Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();
            if (ext == ".xls")
                throw new FriendlyException("这个格式读不了（.xls 是 Excel 97-2003 老格式）。请在 Excel 里打开后「另存为」xlsx，再回来比对。");
            if (!IsSupported(path))
                throw new FriendlyException("只支持 .xlsx / .xlsm / .csv / .tsv，这个文件类型不在支持范围内。");

            var file = new TableFile { Path = path };
            try
            {
                if (ext == ".csv" || ext == ".tsv")
                {
                    progress?.Invoke(0, 1);
                    file.Sheets.Add(CsvReader.ReadSheet(path));
                    progress?.Invoke(1, 1);
                }
                else
                {
                    ExcelReader.Progress = progress;
                    ExcelReader.CancelToken = token;
                    ExcelReader.CellErrorHandler = cellError;
                    try
                    {
                        file.Sheets.AddRange(ExcelReader.ReadAll(path));
                    }
                    finally
                    {
                        ExcelReader.Progress = null;
                        ExcelReader.CancelToken = default;
                        ExcelReader.CellErrorHandler = null;
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (FriendlyException) { throw; }
            catch (Exception ex)
            {
                throw new FriendlyException("读文件失败：" + ex.Message, ex);
            }

            if (file.Sheets.Count == 0) throw new FriendlyException("这个文件里没有可用的工作表。");
            return file;
        }

        /// <summary>只读表头（选主键列的下来框用），不读整表。</summary>
        public static List<string> ReadHeaders(string path, string sheetName)
        {
            string ext = (Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();
            if (ext == ".csv" || ext == ".tsv")
                return CsvReader.ReadSheet(path).Columns;

            if (ext == ".xls") throw new FriendlyException("这个格式读不了（.xls 是 Excel 97-2003 老格式）。请在 Excel 里另存为 xlsx。");
            if (!IsSupported(path)) throw new FriendlyException("只支持 .xlsx / .xlsm / .csv / .tsv。");

            using (var wb = ExcelReader.Open(path))
            {
                IXLWorksheet ws = null;
                if (!string.IsNullOrEmpty(sheetName))
                {
                    try { wb.Worksheets.TryGetWorksheet(sheetName, out ws); } catch { ws = null; }
                }
                if (ws == null) ws = wb.Worksheet(1);

                var sheet = new SheetData { Name = ws.Name };
                var lastCol = ws.LastColumnUsed();
                int maxCol = lastCol == null ? 0 : lastCol.ColumnNumber();
                if (maxCol > 0)
                {
                    var headers = new string[maxCol];
                    for (int c = 1; c <= maxCol; c++) headers[c - 1] = ExcelReader.ReadCell(ws.Cell(1, c));
                    sheet.SetColumns(headers, maxCol);
                }
                return sheet.Columns;
            }
        }

        /// <summary>只要 sheet 名字（选 sheet 的下拉框用），不读单元格内容。</summary>
        public static List<string> ListSheetNames(string path)
        {
            string ext = (Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();
            if (ext == ".csv" || ext == ".tsv")
                return new List<string> { Path.GetFileNameWithoutExtension(path) };
            if (ext == ".xls")
                throw new FriendlyException("这个格式读不了（.xls 是 Excel 97-2003 老格式）。请在 Excel 里另存为 xlsx。");
            if (!IsSupported(path)) throw new FriendlyException("只支持 .xlsx / .xlsm / .csv / .tsv。");
            return ExcelReader.ListSheets(path);
        }
    }
}
