using System;
using System.Collections.Generic;

namespace TableDiff.Core
{
    /// <summary>一张表（一个 sheet 或一个 csv 文件）的原始内容，单元格一律是规整后的文本。</summary>
    public sealed class SheetData
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>表头（第一行）；没有表头时是 列1 / 列2 ……</summary>
        public List<string> Columns { get; } = new List<string>();

        /// <summary>数据行（不含表头）。</summary>
        public List<TableRow> Rows { get; } = new List<TableRow>();

        public int ColumnCount => Columns.Count;

        public void SetColumns(IList<string> headers, int count)
        {
            Columns.Clear();
            for (int i = 0; i < count; i++)
            {
                string name = (headers != null && i < headers.Count) ? (headers[i] ?? string.Empty).Trim() : string.Empty;
                if (name.Length == 0) name = "列" + (i + 1);
                // 表头重名时加后缀，否则列映射会分不清
                string unique = name;
                int dup = 2;
                while (Columns.Contains(unique)) unique = name + "(" + dup++ + ")";
                Columns.Add(unique);
            }
        }

        public static SheetData FromRows(string name, IList<string[]> raw, bool firstRowIsHeader)
        {
            var sheet = new SheetData { Name = name };
            if (raw == null || raw.Count == 0)
            {
                sheet.SetColumns(null, 0);
                return sheet;
            }

            int width = 0;
            for (int i = 0; i < raw.Count; i++) width = Math.Max(width, raw[i] == null ? 0 : raw[i].Length);

            int start = 0;
            if (firstRowIsHeader && raw.Count > 0)
            {
                sheet.SetColumns(raw[0], width);
                start = 1;
            }
            else
            {
                sheet.SetColumns(null, width);
            }

            for (int i = start; i < raw.Count; i++)
            {
                var cells = new string[width];
                var src = raw[i];
                for (int c = 0; c < width; c++)
                    cells[c] = (src != null && c < src.Length) ? (src[c] ?? string.Empty) : string.Empty;
                sheet.Rows.Add(new TableRow(cells, i + 1));
            }
            return sheet;
        }
    }

    /// <summary>一行数据。SourceRow 是它在文件里的物理行号（从 1 开始，含表头行）。</summary>
    public sealed class TableRow
    {
        public TableRow(string[] cells, int sourceRow)
        {
            Cells = cells ?? new string[0];
            SourceRow = sourceRow;
        }

        public string[] Cells { get; }
        public int SourceRow { get; }

        public string Get(int index)
        {
            if (index < 0 || index >= Cells.Length) return string.Empty;
            return Cells[index] ?? string.Empty;
        }
    }

    /// <summary>一个文件读出来的全部 sheet。</summary>
    public sealed class TableFile
    {
        public string Path { get; set; } = string.Empty;
        public List<SheetData> Sheets { get; } = new List<SheetData>();

        public SheetData First => Sheets.Count > 0 ? Sheets[0] : null;

        public SheetData Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return First;
            for (int i = 0; i < Sheets.Count; i++)
                if (string.Equals(Sheets[i].Name, name, StringComparison.Ordinal)) return Sheets[i];
            return First;
        }
    }

    /// <summary>能直接显示给用户看的错误（"文件被 Excel 占用了，请先关闭再试"这种）。</summary>
    public sealed class FriendlyException : Exception
    {
        public FriendlyException(string message) : base(message) { }
        public FriendlyException(string message, Exception inner) : base(message, inner) { }
    }
}
