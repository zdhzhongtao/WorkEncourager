using System;
using System.Collections.Generic;
using System.Linq;
using TableDiff.Core;

namespace TableDiff.Tests
{
    internal static class Build
    {
        public static SheetData Sheet(string name, string[] headers, params string[][] rows)
        {
            var sheet = new SheetData { Name = name };
            sheet.SetColumns(headers, headers.Length);
            for (int i = 0; i < rows.Length; i++)
                sheet.Rows.Add(new TableRow(rows[i], i + 2));
            return sheet;
        }

        public static SheetData SheetRaw(string name, params string[][] raw)
        {
            return SheetData.FromRows(name, raw.ToList(), true);
        }

        public static DiffOptions Keys(params int[] indexes)
        {
            var o = new DiffOptions { Mode = DiffMode.ByKey };
            foreach (var i in indexes) o.KeyColumns.Add(i);
            return o;
        }

        public static DiffOptions WithColumns(this DiffOptions o, SheetData a, SheetData b)
        {
            foreach (var m in ColumnMapper.ByName(a, b)) o.Columns.Add(m);
            return o;
        }

        public static DiffResult Run(SheetData a, SheetData b, DiffOptions o)
        {
            if (o.Columns.Count == 0) o.WithColumns(a, b);
            return DiffEngine.Compare(a, b, o);
        }

        public static DiffRow Row(DiffResult r, string key)
        {
            return r.Rows.FirstOrDefault(x => x.Key == key);
        }
    }
}
