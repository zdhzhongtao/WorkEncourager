using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;

namespace SheetSplitter.Core
{
    /// <summary>
    /// 生成一张 500 行的样例总表，专供验收：含「部门」列（12 个部门，其中 1 行空值、1 个部门名带 /）。
    /// 命令行：SheetSplitter.exe --maketestdata &lt;目录&gt;
    /// </summary>
    public static class TestDataGenerator
    {
        public const int Rows = 500;

        public static readonly string[] Departments =
        {
            "财务部", "采购部", "销售部", "生产部", "行政部", "人事部",
            "信息部", "法务部", "市场部", "物流部", "质检部", "研发/测试部"
        };

        public static string Generate(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new FriendlyException("请指定要生成到哪个目录。");
            Directory.CreateDirectory(directory);

            string path = Path.Combine(directory, "总表.xlsx");
            Write(path);

            return "已生成：" + Environment.NewLine + path + "（" + Rows + " 行）" + Environment.NewLine
                   + "预期：按「部门」拆分成 13 个文件（12 个部门 + 1 个「空白」），"
                   + "其中「研发/测试部」的文件名里 / 会变成 _。";
        }

        public static void Write(string path)
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("总表");
                string[] header = { "单号", "部门", "客户", "金额", "日期", "备注" };
                for (int c = 0; c < header.Length; c++)
                {
                    ws.Cell(1, c + 1).Value = header[c];
                    ws.Cell(1, c + 1).Style.Font.Bold = true;
                }

                var start = new DateTime(2026, 1, 1);
                for (int i = 1; i <= Rows; i++)
                {
                    ws.Cell(i + 1, 1).Value = "T" + i.ToString("0000", CultureInfo.InvariantCulture);

                    // 第 37 行故意留空部门，验「空值归到 _空白」
                    if (i != 37)
                        ws.Cell(i + 1, 2).Value = Departments[(i - 1) % Departments.Length];

                    ws.Cell(i + 1, 3).Value = "客户" + ((i % 50) + 1);
                    ws.Cell(i + 1, 4).Value = 100m + i;
                    ws.Cell(i + 1, 5).Value = start.AddDays(i % 300);
                    ws.Cell(i + 1, 6).Value = "第 " + i + " 行";
                }

                ws.SheetView.FreezeRows(1);
                ws.Columns().AdjustToContents(1, 1);

                string tmp = path + ".tmp.xlsx";
                wb.SaveAs(tmp);
                File.Move(tmp, path, true);
            }
        }
    }
}
