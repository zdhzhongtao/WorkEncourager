using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace TableDiff.Core
{
    /// <summary>列对齐：默认按表头名，也可按列位置。</summary>
    public static class ColumnMapper
    {
        /// <summary>按表头名匹配。同名（忽略大小写与首尾空格）优先；名字对不上的 A 列排在前面，B 独有列追加到末尾。</summary>
        public static List<ColumnMap> ByName(SheetData a, SheetData b)
        {
            return ByName(a.Columns, b.Columns);
        }

        public static List<ColumnMap> ByName(IList<string> columnsA, IList<string> columnsB)
        {
            var maps = new List<ColumnMap>();
            var usedB = new bool[columnsB.Count];

            for (int i = 0; i < columnsA.Count; i++)
            {
                string name = columnsA[i];
                int hit = -1;
                for (int j = 0; j < columnsB.Count; j++)
                {
                    if (usedB[j]) continue;
                    if (string.Equals((columnsB[j] ?? string.Empty).Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)) { hit = j; break; }
                }
                if (hit >= 0) usedB[hit] = true;
                maps.Add(new ColumnMap(name, i, hit));
            }

            for (int j = 0; j < columnsB.Count; j++)
            {
                if (usedB[j]) continue;
                maps.Add(new ColumnMap(columnsB[j] + "（仅B）", -1, j));
            }
            return maps;
        }

        /// <summary>按列位置对齐（第 1 列对第 1 列），列名取 A 的。</summary>
        public static List<ColumnMap> ByPosition(SheetData a, SheetData b)
        {
            var maps = new List<ColumnMap>();
            int max = Math.Max(a.ColumnCount, b.ColumnCount);
            for (int i = 0; i < max; i++)
            {
                string name = i < a.ColumnCount ? a.Columns[i] : "列" + (i + 1);
                int ia = i < a.ColumnCount ? i : -1;
                int ib = i < b.ColumnCount ? i : -1;
                maps.Add(new ColumnMap(name, ia, ib));
            }
            return maps;
        }
    }

    /// <summary>比对引擎。纯内存计算，不碰界面，可单测。</summary>
    public static class DiffEngine
    {
        public static DiffResult Compare(SheetData a, SheetData b, DiffOptions options,
                                         IProgress<int> progress = null,
                                         CancellationToken token = default)
        {
            if (a == null || b == null) throw new ArgumentNullException(nameof(a));
            if (options == null) options = new DiffOptions();

            var result = new DiffResult();
            var columns = options.EffectiveColumns();

            for (int i = 0; i < columns.Length; i++) result.Columns.Add(columns[i].Name);

            var rowsA = Filter(a.Rows, options);
            var rowsB = Filter(b.Rows, options);
            result.RowCountA = rowsA.Count;
            result.RowCountB = rowsB.Count;

            if (options.IgnoreBlankRows && (a.Rows.Count != rowsA.Count || b.Rows.Count != rowsB.Count))
                result.Notes.Add("已跳过全空行：A 侧 " + (a.Rows.Count - rowsA.Count) + " 行，B 侧 " + (b.Rows.Count - rowsB.Count) + " 行。");

            switch (options.Mode)
            {
                case DiffMode.ByPosition:
                    CompareByPosition(rowsA, rowsB, columns, options, result, progress, token);
                    break;
                case DiffMode.ByWholeRow:
                    CompareByWholeRow(rowsA, rowsB, columns, options, result, progress, token);
                    break;
                default:
                    CompareByKey(rowsA, rowsB, columns, options, result, progress, token);
                    break;
            }

            return result;
        }

        private static List<TableRow> Filter(List<TableRow> rows, DiffOptions options)
        {
            if (!options.IgnoreBlankRows) return rows;
            var list = new List<TableRow>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
                if (!CellText.IsBlankRow(rows[i].Cells)) list.Add(rows[i]);
            return list;
        }

        // ---------------- 按主键 ----------------

        private static void CompareByKey(List<TableRow> rowsA, List<TableRow> rowsB, ColumnMap[] columns,
                                         DiffOptions options, DiffResult result,
                                         IProgress<int> progress, CancellationToken token)
        {
            if (options.KeyColumns.Count == 0)
                throw new FriendlyException("请先选择主键列（1~3 列）。");

            var indexB = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int i = 0; i < rowsB.Count; i++)
            {
                string key = BuildKey(rowsB[i], columns, options, fromA: false);
                if (!indexB.TryGetValue(key, out var list))
                {
                    list = new List<int>(1);
                    indexB[key] = list;
                }
                list.Add(i);
            }

            var seenA = new Dictionary<string, int>(StringComparer.Ordinal);
            var matchedB = new bool[rowsB.Count];

            // 同主键有多条时逐条消耗 B 侧的候选，不能反复拿第一条：
            // 否则 A 的两条重复行会同时"匹配"B 的唯一一行，凭空少报一个「仅A」。
            var cursor = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int i = 0; i < rowsA.Count; i++)
            {
                if ((i & 0x3FF) == 0)
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Report(i * 100 / Math.Max(1, rowsA.Count));
                }

                var rowA = rowsA[i];
                string key = BuildKey(rowA, columns, options, fromA: true);

                if (seenA.TryGetValue(key, out int n)) seenA[key] = n + 1;
                else seenA[key] = 1;

                int used = cursor.TryGetValue(key, out int pos) ? pos : 0;
                if (indexB.TryGetValue(key, out var candidates) && used < candidates.Count)
                {
                    int bIndex = candidates[used];
                    cursor[key] = used + 1;
                    matchedB[bIndex] = true;
                    var rowB = rowsB[bIndex];

                    var cells = new CellDiff[columns.Length];
                    bool changed = false;
                    for (int c = 0; c < columns.Length; c++)
                    {
                        string va = ValueOf(rowA, columns[c].IndexA);
                        string vb = ValueOf(rowB, columns[c].IndexB);
                        bool diff = columns[c].Compare && !CellEquals(va, vb, options);
                        cells[c] = new CellDiff { ValueA = va, ValueB = vb, Changed = diff };
                        if (diff) changed = true;
                    }

                    var dr = new DiffRow
                    {
                        Status = changed ? RowStatus.Changed : RowStatus.Same,
                        Key = DisplayKey(rowA, columns, options, true),
                        Cells = cells,
                        SourceRowA = rowA.SourceRow,
                        SourceRowB = rowB.SourceRow
                    };
                    result.Rows.Add(dr);
                }
                else
                {
                    result.Rows.Add(BuildSingleSide(rowA, columns, RowStatus.OnlyA, key, options));
                }
            }

            for (int i = 0; i < rowsB.Count; i++)
            {
                if (matchedB[i]) continue;
                result.Rows.Add(BuildSingleSide(rowsB[i], columns, RowStatus.OnlyB, null, options));
            }

            // 主键重复告警：A 侧、B 侧合并统计，任一侧大于 1 就报
            var allKeys = new HashSet<string>(seenA.Keys, StringComparer.Ordinal);
            foreach (var k in indexB.Keys) allKeys.Add(k);
            foreach (var key in allKeys)
            {
                int ca = seenA.TryGetValue(key, out int na) ? na : 0;
                int cb = indexB.TryGetValue(key, out var cand) ? cand.Count : 0;
                if (ca > 1 || cb > 1)
                    result.DuplicateKeys.Add(new KeyConflict { Key = key, CountA = ca, CountB = cb });
            }

            Tally(result);
        }

        // ---------------- 按行位置 ----------------

        private static void CompareByPosition(List<TableRow> rowsA, List<TableRow> rowsB, ColumnMap[] columns,
                                              DiffOptions options, DiffResult result,
                                              IProgress<int> progress, CancellationToken token)
        {
            int max = Math.Max(rowsA.Count, rowsB.Count);
            for (int i = 0; i < max; i++)
            {
                if ((i & 0x3FF) == 0)
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Report(i * 100 / Math.Max(1, max));
                }

                bool hasA = i < rowsA.Count, hasB = i < rowsB.Count;
                if (hasA && hasB)
                {
                    var cells = new CellDiff[columns.Length];
                    bool changed = false;
                    for (int c = 0; c < columns.Length; c++)
                    {
                        string va = ValueOf(rowsA[i], columns[c].IndexA);
                        string vb = ValueOf(rowsB[i], columns[c].IndexB);
                        bool diff = columns[c].Compare && !CellEquals(va, vb, options);
                        cells[c] = new CellDiff { ValueA = va, ValueB = vb, Changed = diff };
                        if (diff) changed = true;
                    }
                    result.Rows.Add(new DiffRow
                    {
                        Status = changed ? RowStatus.Changed : RowStatus.Same,
                        Key = "第 " + (i + 1) + " 行",
                        Cells = cells,
                        SourceRowA = rowsA[i].SourceRow,
                        SourceRowB = rowsB[i].SourceRow
                    });
                }
                else if (hasA)
                {
                    result.Rows.Add(BuildSingleSide(rowsA[i], columns, RowStatus.OnlyA, "第 " + (i + 1) + " 行", options));
                }
                else
                {
                    result.Rows.Add(BuildSingleSide(rowsB[i], columns, RowStatus.OnlyB, "第 " + (i + 1) + " 行", options));
                }
            }
            Tally(result);
        }

        // ---------------- 按整行内容 ----------------

        private static void CompareByWholeRow(List<TableRow> rowsA, List<TableRow> rowsB, ColumnMap[] columns,
                                              DiffOptions options, DiffResult result,
                                              IProgress<int> progress, CancellationToken token)
        {
            var pool = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int i = 0; i < rowsB.Count; i++)
            {
                string sig = BuildRowSignature(rowsB[i], columns, options, false);
                if (!pool.TryGetValue(sig, out var list))
                {
                    list = new List<int>(1);
                    pool[sig] = list;
                }
                list.Add(i);
            }

            var usedB = new bool[rowsB.Count];

            for (int i = 0; i < rowsA.Count; i++)
            {
                if ((i & 0x3FF) == 0)
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Report(i * 100 / Math.Max(1, rowsA.Count));
                }

                string sig = BuildRowSignature(rowsA[i], columns, options, true);
                int hit = -1;
                if (pool.TryGetValue(sig, out var list))
                {
                    for (int k = 0; k < list.Count; k++)
                    {
                        if (usedB[list[k]]) continue;
                        hit = list[k];
                        break;
                    }
                }

                if (hit >= 0)
                {
                    usedB[hit] = true;
                    result.Rows.Add(new DiffRow
                    {
                        Status = RowStatus.Same,
                        Key = "第 " + rowsA[i].SourceRow + " 行",
                        // 按整行内容比对时两侧列顺序可能不同，这里必须读 A 自己的列下标。
                        Cells = BuildCells(rowsA[i], columns, true),
                        SourceRowA = rowsA[i].SourceRow
                    });
                }
                else
                {
                    result.Rows.Add(BuildSingleSide(rowsA[i], columns, RowStatus.OnlyA, "第 " + rowsA[i].SourceRow + " 行", options));
                }
            }

            for (int i = 0; i < rowsB.Count; i++)
            {
                if (usedB[i]) continue;
                result.Rows.Add(BuildSingleSide(rowsB[i], columns, RowStatus.OnlyB, "第 " + rowsB[i].SourceRow + " 行", options));
            }

            Tally(result);
        }

        // ---------------- 公共小工具 ----------------

        private static CellDiff[] BuildCells(TableRow row, ColumnMap[] columns, bool fromA)
        {
            var cells = new CellDiff[columns.Length];
            for (int c = 0; c < columns.Length; c++)
            {
                string v = ValueOf(row, fromA ? columns[c].IndexA : columns[c].IndexB);
                cells[c] = new CellDiff { ValueA = fromA ? v : string.Empty, ValueB = fromA ? string.Empty : v };
            }
            return cells;
        }

        private static DiffRow BuildSingleSide(TableRow row, ColumnMap[] columns, RowStatus status, string keyText, DiffOptions options)
        {
            bool fromA = status == RowStatus.OnlyA;
            var cells = new CellDiff[columns.Length];
            for (int c = 0; c < columns.Length; c++)
            {
                string v = ValueOf(row, fromA ? columns[c].IndexA : columns[c].IndexB);
                cells[c] = new CellDiff { ValueA = fromA ? v : string.Empty, ValueB = fromA ? string.Empty : v };
            }

            return new DiffRow
            {
                Status = status,
                Key = keyText ?? "第 " + row.SourceRow + " 行",
                Cells = cells,
                SourceRowA = fromA ? row.SourceRow : 0,
                SourceRowB = fromA ? 0 : row.SourceRow
            };
        }

        private static void Tally(DiffResult result)
        {
            for (int i = 0; i < result.Rows.Count; i++)
            {
                var row = result.Rows[i];
                switch (row.Status)
                {
                    case RowStatus.Same: result.SameCount++; break;
                    case RowStatus.OnlyA: result.OnlyACount++; break;
                    case RowStatus.OnlyB: result.OnlyBCount++; break;
                    case RowStatus.Changed:
                        result.ChangedRowCount++;
                        result.ChangedCellCount += row.ChangedCellCount;
                        break;
                }
            }

            if (result.DuplicateKeys.Count > 0)
                result.Notes.Add("有 " + result.DuplicateKeys.Count + " 个主键重复，已按第一条匹配，请核对（明细见「主键重复」提示）。");
        }

        public static string ValueOf(TableRow row, int index)
        {
            if (row == null || index < 0) return string.Empty;
            return row.Get(index);
        }

        private static string BuildKey(TableRow row, ColumnMap[] columns, DiffOptions options, bool fromA)
        {
            if (options.KeyColumns.Count == 0) return string.Empty;
            var parts = new List<string>(options.KeyColumns.Count);
            for (int i = 0; i < options.KeyColumns.Count; i++)
            {
                int idx = options.KeyColumns[i];
                if (idx < 0 || idx >= columns.Length) { parts.Add(string.Empty); continue; }
                string v = ValueOf(row, fromA ? columns[idx].IndexA : columns[idx].IndexB);
                parts.Add(CellText.Canonical(v, options.IgnoreCase, options.IgnoreTrim, options.EmptyEqualsMissing));
            }
            return CellText.JoinKey(parts);
        }

        private static string DisplayKey(TableRow row, ColumnMap[] columns, DiffOptions options, bool fromA)
        {
            if (options.KeyColumns.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < options.KeyColumns.Count; i++)
            {
                int idx = options.KeyColumns[i];
                if (idx < 0 || idx >= columns.Length) continue;
                string v = ValueOf(row, fromA ? columns[idx].IndexA : columns[idx].IndexB);
                if (sb.Length > 0) sb.Append(" + ");
                sb.Append(v);
            }
            return sb.ToString();
        }

        private static string BuildRowSignature(TableRow row, ColumnMap[] columns, DiffOptions options, bool fromA)
        {
            var sb = new StringBuilder();
            for (int c = 0; c < columns.Length; c++)
            {
                string v = ValueOf(row, fromA ? columns[c].IndexA : columns[c].IndexB);
                sb.Append(CellText.Canonical(v, options.IgnoreCase, options.IgnoreTrim, options.EmptyEqualsMissing));
                sb.Append('\u0001');
            }
            return sb.ToString();
        }

        /// <summary>单元格是否相等（带数值容差）。</summary>
        public static bool CellEquals(string a, string b, DiffOptions options)
        {
            if (options == null) options = new DiffOptions();

            bool blankA = string.IsNullOrWhiteSpace(a);
            bool blankB = string.IsNullOrWhiteSpace(b);
            if (options.EmptyEqualsMissing && blankA && blankB) return true;
            if (blankA != blankB && options.EmptyEqualsMissing) return false;

            string na = CellText.Canonical(a, options.IgnoreCase, options.IgnoreTrim, options.EmptyEqualsMissing);
            string nb = CellText.Canonical(b, options.IgnoreCase, options.IgnoreTrim, options.EmptyEqualsMissing);

            if (options.NumberTolerance > 0
                && CellText.TryParseNumber(na, out double da)
                && CellText.TryParseNumber(nb, out double db))
            {
                return Math.Abs(da - db) <= options.NumberTolerance;
            }

            return string.Equals(na, nb, StringComparison.Ordinal);
        }
    }
}
