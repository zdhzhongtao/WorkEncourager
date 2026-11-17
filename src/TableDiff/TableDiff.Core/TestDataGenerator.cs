using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;

namespace TableDiff.Core
{
    /// <summary>
    /// 生成一对用于验收的样例表（各 200 行左右，含 3 处改值 / 2 行新增 / 1 行删除）。
    /// 命令行：TableDiff.exe --maketestdata &lt;目录&gt;
    /// </summary>
    public static class TestDataGenerator
    {
        public const int RowCount = 200;
        public const int ChangedRows = 3;
        public const int AddedRows = 2;
        public const int DeletedRows = 1;

        private static readonly string[] Customers =
        {
            "上海鼎新贸易", "北京华创科技", "广州南方物流", "成都锦江电子", "苏州恒信材料"
        };

        private static readonly string[] Departments =
        {
            "财务部", "采购部", "销售部", "生产部", "行政部"
        };

        /// <summary>生成 表A.xlsx 与 表B.xlsx，返回一句给用户看的说明。</summary>
        public static string Generate(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new FriendlyException("请指定要生成到哪个目录。");
            Directory.CreateDirectory(directory);

            string pathA = Path.Combine(directory, "表A.xlsx");
            string pathB = Path.Combine(directory, "表B.xlsx");

            var rowsA = BuildBaseRows();
            var rowsB = Clone(rowsA);

            // 3 处改值（不动主键）
            rowsB[4][3] = "1750.00";     // 金额
            rowsB[41][1] = "上海鼎新贸易（新）";  // 客户
            rowsB[119][4] = "2026-09-01";  // 日期

            // 1 行删除（A 有 B 没有）
            rowsB.RemoveAt(87);

            // 2 行新增（B 有 A 没有）
            rowsB.Add(new[] { "PO-2026-0201", "宁波远洋船务", "销售部", "8888.00", "2026-08-01", "新增行 1" });
            rowsB.Add(new[] { "PO-2026-0202", "天津港务集团", "采购部", "1234.56", "2026-08-02", "新增行 2" });

            WriteWorkbook(pathA, "对账单A", rowsA);
            WriteWorkbook(pathB, "对账单B", rowsB);

            return "已生成：" + Environment.NewLine
                   + pathA + "（" + rowsA.Count + " 行）" + Environment.NewLine
                   + pathB + "（" + rowsB.Count + " 行）" + Environment.NewLine
                   + "预期结果：字段变更 3 行（3 个单元格）/ 仅A有 1 行 / 仅B有 2 行。";
        }

        private static List<string[]> BuildBaseRows()
        {
            var rows = new List<string[]>(RowCount);
            for (int i = 1; i <= RowCount; i++)
            {
                var d = new DateTime(2026, 1, 1).AddDays(i - 1);
                rows.Add(new[]
                {
                    "PO-2026-" + i.ToString("0000", CultureInfo.InvariantCulture),
                    Customers[i % Customers.Length],
                    Departments[i % Departments.Length],
                    (1000 + i * 37.5).ToString("0.00", CultureInfo.InvariantCulture),
                    d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    "第 " + i + " 行"
                });
            }
            return rows;
        }

        private static List<string[]> Clone(List<string[]> src)
        {
            var list = new List<string[]>(src.Count);
            for (int i = 0; i < src.Count; i++)
            {
                var row = new string[src[i].Length];
                Array.Copy(src[i], row, row.Length);
                list.Add(row);
            }
            return list;
        }

        private static void WriteWorkbook(string path, string sheetName, List<string[]> rows)
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet(sheetName);
                string[] header = { "单据号", "客户", "部门", "金额", "日期", "备注" };
                for (int c = 0; c < header.Length; c++)
                {
                    ws.Cell(1, c + 1).Value = header[c];
                    ws.Cell(1, c + 1).Style.Font.Bold = true;
                }

                for (int r = 0; r < rows.Count; r++)
                {
                    for (int c = 0; c < rows[r].Length; c++)
                    {
                        string text = rows[r][c];
                        if (c == 3 && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double amount))
                            ws.Cell(r + 2, c + 1).Value = amount;      // 金额写成真数字，验证数字规整
                        else if (c == 4 && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                            ws.Cell(r + 2, c + 1).Value = date;        // 日期写成真日期，验证 45292 → yyyy-MM-dd
                        else
                            ws.Cell(r + 2, c + 1).Value = text;
                    }
                }

                ws.Columns().AdjustToContents(1, 1);
                ws.SheetView.FreezeRows(1);

                string tmp = path + ".tmp.xlsx";
                wb.SaveAs(tmp);
                File.Move(tmp, path, true);
            }
        }
    }
}
