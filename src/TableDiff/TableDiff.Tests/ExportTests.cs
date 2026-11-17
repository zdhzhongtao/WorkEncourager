using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using TableDiff.Core;

namespace TableDiff.Tests
{
    public class ExportTests : IDisposable
    {
        private readonly string _dir;

        public ExportTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "TableDiffTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static DiffResult SampleResult()
        {
            var a = Build.Sheet("A", new[] { "单号", "客户", "金额" },
                new[] { "P1", "甲", "100" },
                new[] { "P2", "乙", "200" },
                new[] { "P3", "丙", "300" });
            var b = Build.Sheet("B", new[] { "单号", "客户", "金额" },
                new[] { "P1", "甲", "100" },
                new[] { "P2", "乙", "250" },
                new[] { "P4", "丁", "400" });

            return Build.Run(a, b, Build.Keys(0));
        }

        [Fact]
        public void CSV导出的列序与表头固定()
        {
            string path = Path.Combine(_dir, "差异清单.csv");
            CsvDiffExporter.Export(SampleResult(), path);

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            Assert.Equal("序号,类型,主键,列名,表A值,表B值,说明", lines[0]);
        }

        [Fact]
        public void CSV导出行数等于差异条数()
        {
            string path = Path.Combine(_dir, "差异清单.csv");
            var result = SampleResult();
            CsvDiffExporter.Export(result, path);

            var lines = CsvDiffExporter.EnumerateLines(result);
            string[] fileLines = File.ReadAllLines(path, Encoding.UTF8);

            Assert.Equal(3, lines.Count);          // 1 改动 + 1 仅A + 1 仅B
            Assert.Equal(lines.Count + 1, fileLines.Length);
        }

        [Fact]
        public void CSV必须带BOM否则Excel乱码()
        {
            string path = Path.Combine(_dir, "差异清单.csv");
            CsvDiffExporter.Export(SampleResult(), path);

            byte[] bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 3);
            Assert.Equal(0xEF, bytes[0]);
            Assert.Equal(0xBB, bytes[1]);
            Assert.Equal(0xBF, bytes[2]);
        }

        [Fact]
        public void CSV里的逗号和引号被正确转义()
        {
            Assert.Equal("\"上海, 鼎新\"", CsvDiffExporter.Escape("上海, 鼎新"));
            Assert.Equal("\"他说\"\"好\"\"\"", CsvDiffExporter.Escape("他说\"好\""));
            Assert.Equal("甲", CsvDiffExporter.Escape("甲"));
        }

        [Fact]
        public void 默认文件名带时间戳()
        {
            string name = CsvDiffExporter.DefaultFileName(new DateTime(2026, 11, 14, 9, 5, 0));
            Assert.Equal("差异清单_20261114_0905.csv", name);
        }

        [Fact]
        public void xlsx报告有汇总与差异明细两个sheet()
        {
            string path = Path.Combine(_dir, "差异报告.xlsx");
            XlsxDiffExporter.Export(SampleResult(), path, "a.xlsx", "b.xlsx");

            using (var wb = new XLWorkbook(path))
            {
                Assert.Equal(2, wb.Worksheets.Count);
                Assert.Equal("汇总", wb.Worksheet(1).Name);
                Assert.Equal("差异明细", wb.Worksheet(2).Name);
            }
        }

        [Fact]
        public void xlsx明细行数与CSV一致()
        {
            string path = Path.Combine(_dir, "差异报告.xlsx");
            var result = SampleResult();
            XlsxDiffExporter.Export(result, path, "a.xlsx", "b.xlsx");

            var expected = CsvDiffExporter.EnumerateLines(result).Count;
            using (var wb = new XLWorkbook(path))
            {
                var ws = wb.Worksheet("差异明细");
                int dataRows = ws.RowsUsed().Count() - 1;   // 去掉表头
                Assert.Equal(expected, dataRows);
                Assert.Equal("序号", ws.Cell(1, 1).GetString());
            }
        }

        [Fact]
        public void xlsx改动单元格有高亮填充()
        {
            string path = Path.Combine(_dir, "差异报告.xlsx");
            XlsxDiffExporter.Export(SampleResult(), path, "a.xlsx", "b.xlsx");

            using (var wb = new XLWorkbook(path))
            {
                var ws = wb.Worksheet("差异明细");
                var cell = ws.Cell(2, 5);
                Assert.Equal("200", cell.GetString());
                Assert.NotEqual(XLColor.NoColor, cell.Style.Fill.BackgroundColor);
            }
        }
    }
}
