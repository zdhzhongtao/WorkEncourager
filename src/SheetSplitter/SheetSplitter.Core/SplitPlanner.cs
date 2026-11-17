using System;
using System.Collections.Generic;
using System.IO;

namespace SheetSplitter.Core
{
    /// <summary>合并时的一张输入表（已经定位到具体 sheet）。</summary>
    public sealed class MergeInput
    {
        public string Path { get; set; }
        public SheetData Data { get; set; }

        public string FileName => string.IsNullOrEmpty(Path) ? "?" : System.IO.Path.GetFileName(Path);
        public string Display => FileName + " · " + (Data == null ? "?" : Data.Name);
    }

    /// <summary>要写出去的一张 sheet。</summary>
    public sealed class OutputSheet
    {
        public string SheetName { get; set; } = "Sheet1";
        public List<string> Headers { get; set; } = new List<string>();
        public List<TableRow> Rows { get; } = new List<TableRow>();
    }

    /// <summary>要写出去的一个文件（可能含多张 sheet）。</summary>
    public sealed class OutputFile
    {
        public string FileName { get; set; } = "输出.xlsx";
        public List<OutputSheet> Sheets { get; } = new List<OutputSheet>();

        public int RowCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Sheets.Count; i++) n += Sheets[i].Rows.Count;
                return n;
            }
        }
    }

    /// <summary>执行前的计划：生成哪些文件、每个多少行。先给用户看，确认后才落盘。</summary>
    public sealed class SplitPlan
    {
        public List<OutputFile> Files { get; } = new List<OutputFile>();
        public List<string> Notes { get; } = new List<string>();
        public List<string> MissingColumns { get; } = new List<string>();

        public int TotalRows
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Files.Count; i++) n += Files[i].RowCount;
                return n;
            }
        }

        public string Summary => "将生成 " + Files.Count + " 个文件，共 " + TotalRows + " 行。";
    }

    public enum HeaderAlignment
    {
        ByName,
        ByPosition
    }

    /// <summary>拆合计划：全是纯计算，不碰磁盘，方便单测。</summary>
    public static class SplitPlanner
    {
        /// <summary>按某列的值分组。空值归一组并排在最后；其余按首次出现顺序。</summary>
        public static List<KeyValuePair<string, List<TableRow>>> GroupByColumn(SheetData sheet, int columnIndex)
        {
            if (sheet == null) throw new FriendlyException("没有可用的表。");
            if (columnIndex < 0 || columnIndex >= sheet.ColumnCount)
                throw new FriendlyException("请先选择「按哪一列拆分」。");

            var order = new List<string>();
            var groups = new Dictionary<string, List<TableRow>>(StringComparer.Ordinal);
            List<TableRow> blankGroup = null;
            string blankKey = null;

            foreach (var row in sheet.Rows)
            {
                string raw = row.Get(columnIndex);
                bool isBlank = string.IsNullOrWhiteSpace(raw);
                string key = isBlank ? (blankKey ?? string.Empty) : raw.Trim();

                if (isBlank)
                {
                    if (blankGroup == null)
                    {
                        blankKey = key;
                        blankGroup = new List<TableRow>();
                    }
                    blankGroup.Add(row);
                    continue;
                }

                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<TableRow>();
                    groups[key] = list;
                    order.Add(key);
                }
                list.Add(row);
            }

            var result = new List<KeyValuePair<string, List<TableRow>>>(order.Count + 1);
            foreach (var key in order) result.Add(new KeyValuePair<string, List<TableRow>>(key, groups[key]));
            if (blankGroup != null) result.Add(new KeyValuePair<string, List<TableRow>>(string.Empty, blankGroup));
            return result;
        }

        /// <summary>模式 1：按列值拆成多个文件，或者一个文件里的多个 sheet。</summary>
        public static SplitPlan PlanByColumn(SheetData sheet, int columnIndex, string sourceBaseName,
                                            bool includeHeader, bool toMultipleSheets)
        {
            var groups = GroupByColumn(sheet, columnIndex);
            var plan = new SplitPlan();

            if (groups.Count == 0)
            {
                plan.Notes.Add("源表没有数据行，没有生成任何文件。");
                return plan;
            }

            if (toMultipleSheets)
            {
                var file = new OutputFile { FileName = FileNameSanitizer.Clean(sourceBaseName, "拆分") + "_按列拆分.xlsx" };
                foreach (var group in groups)
                {
                    var output = new OutputSheet { SheetName = group.Key.Length == 0 ? "空白" : group.Key };
                    if (includeHeader) output.Headers.AddRange(sheet.Columns);
                    output.Rows.AddRange(group.Value);
                    file.Sheets.Add(output);
                }
                plan.Files.Add(file);
                plan.Notes.Add("一个文件里放了 " + groups.Count + " 张 sheet。");
                return plan;
            }

            foreach (var group in groups)
            {
                string value = group.Key.Length == 0 ? "空白" : group.Key;
                var file = new OutputFile { FileName = FileNameSanitizer.BuildFileName(sourceBaseName, value) };
                var output = new OutputSheet { SheetName = string.IsNullOrEmpty(sheet.Name) ? "Sheet1" : sheet.Name };
                if (includeHeader) output.Headers.AddRange(sheet.Columns);
                output.Rows.AddRange(group.Value);
                file.Sheets.Add(output);
                plan.Files.Add(file);
            }
            return plan;
        }

        /// <summary>模式 2 / 3：多张表合成一张。列名对齐或按位置对齐。</summary>
        public static SplitPlan PlanMerge(IList<MergeInput> inputs, HeaderAlignment alignment,
                                          bool addSourceColumns, string outputFileName, string outputSheetName)
        {
            if (inputs == null || inputs.Count == 0) throw new FriendlyException("请先添加要合并的文件。");

            var plan = new SplitPlan();
            var headers = new List<string>();
            if (alignment == HeaderAlignment.ByPosition)
            {
                int width = 0;
                foreach (var input in inputs) width = Math.Max(width, input.Data.ColumnCount);
                for (int i = 0; i < width; i++) headers.Add("列" + (i + 1));
            }
            else
            {
                var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var input in inputs)
                {
                    for (int c = 0; c < input.Data.ColumnCount; c++)
                    {
                        string name = input.Data.Columns[c];
                        if (index.ContainsKey(name)) continue;
                        index[name] = headers.Count;
                        headers.Add(name);
                    }
                }
            }

            if (addSourceColumns)
            {
                headers.Add("来源文件");
                headers.Add("来源sheet");
            }

            var output = new OutputSheet { SheetName = outputSheetName ?? "合并结果" };
            output.Headers.AddRange(headers);

            int total = 0;
            foreach (var input in inputs)
            {
                var map = new int[headers.Count];
                for (int i = 0; i < map.Length; i++) map[i] = -1;

                if (alignment == HeaderAlignment.ByPosition)
                {
                    for (int i = 0; i < Math.Min(headers.Count, input.Data.ColumnCount); i++) map[i] = i;
                }
                else
                {
                    var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (int c = 0; c < input.Data.ColumnCount; c++)
                    {
                        if (!lookup.ContainsKey(input.Data.Columns[c])) lookup[input.Data.Columns[c]] = c;
                    }
                    for (int i = 0; i < headers.Count; i++)
                    {
                        if (lookup.TryGetValue(headers[i], out int source)) map[i] = source;
                    }

                    var missing = new List<string>();
                    for (int i = 0; i < headers.Count; i++)
                    {
                        if (map[i] >= 0) continue;
                        if (addSourceColumns && i >= headers.Count - 2) continue;
                        missing.Add(headers[i]);
                    }
                    if (missing.Count > 0)
                        plan.MissingColumns.Add(input.FileName + " - 缺列：" + string.Join("、", missing));
                }

                foreach (var row in input.Data.Rows)
                {
                    var cells = new string[headers.Count];
                    for (int i = 0; i < headers.Count; i++)
                        cells[i] = map[i] >= 0 ? row.Get(map[i]) : string.Empty;

                    if (addSourceColumns)
                    {
                        cells[headers.Count - 2] = input.FileName;
                        cells[headers.Count - 1] = input.Data.Name;
                    }
                    output.Rows.Add(new TableRow(cells, row.SourceRow));
                    total++;
                }
            }

            if (total > ExcelWriter.ExcelMaxRows - 1)
                throw new FriendlyException("合并后共 " + total + " 行，超过 Excel 单表上限（1,048,576 行）。请先去掉一些文件，或者拆成两批合并。");

            var file = new OutputFile { FileName = outputFileName };
            file.Sheets.Add(output);
            plan.Files.Add(file);

            plan.Notes.Add("合并了 " + inputs.Count + " 张表，共 " + total + " 行。");
            if (plan.MissingColumns.Count > 0)
                plan.Notes.Add("有 " + plan.MissingColumns.Count + " 张表缺列，缺的格子留空了。");
            return plan;
        }

        /// <summary>模式 4：按行数切开。</summary>
        public static SplitPlan PlanByRows(SheetData sheet, int rowsPerFile, string sourceBaseName, bool includeHeader)
        {
            var plan = new SplitPlan();

            if (rowsPerFile <= 0) throw new FriendlyException("每份行数要填一个大于 0 的整数。");
            if (rowsPerFile > ExcelWriter.ExcelMaxRows - 1)
                throw new FriendlyException("每份 " + rowsPerFile + " 行超过了 Excel 单表上限（1,048,575 行，还得留一行表头）。请把每份行数改小。");

            if (sheet.Rows.Count == 0)
            {
                plan.Notes.Add("源表没有数据行，没有生成任何文件。");
                return plan;
            }

            int index = 1;
            for (int start = 0; start < sheet.Rows.Count; start += rowsPerFile)
            {
                var file = new OutputFile
                {
                    FileName = FileNameSanitizer.BuildFileName(sourceBaseName, "第" + index + "份")
                };
                var output = new OutputSheet { SheetName = string.IsNullOrEmpty(sheet.Name) ? "Sheet1" : sheet.Name };
                if (includeHeader) output.Headers.AddRange(sheet.Columns);

                int end = Math.Min(start + rowsPerFile, sheet.Rows.Count);
                for (int i = start; i < end; i++) output.Rows.Add(sheet.Rows[i]);

                file.Sheets.Add(output);
                plan.Files.Add(file);
                index++;
            }

            return plan;
        }
    }
}
