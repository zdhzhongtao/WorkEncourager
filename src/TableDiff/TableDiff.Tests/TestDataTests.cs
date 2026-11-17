using System;
using System.IO;
using System.Linq;
using TableDiff.Core;

namespace TableDiff.Tests
{
    /// <summary>把 --maketestdata 的验收过程自动化：读回来、比对、数字必须对得上。</summary>
    public class TestDataTests : IDisposable
    {
        private readonly string _dir;

        public TestDataTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "TableDiffTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Fact]
        public void 生成两个文件()
        {
            TestDataGenerator.Generate(_dir);

            Assert.True(File.Exists(Path.Combine(_dir, "表A.xlsx")));
            Assert.True(File.Exists(Path.Combine(_dir, "表B.xlsx")));
        }

        [Fact]
        public void 读回来的行数与列名正确()
        {
            TestDataGenerator.Generate(_dir);
            var file = TableReader.Load(Path.Combine(_dir, "表A.xlsx"));

            Assert.Single(file.Sheets);
            var sheet = file.First;
            Assert.Equal(200, sheet.Rows.Count);
            Assert.Equal("单据号", sheet.Columns[0]);
            Assert.Equal(6, sheet.ColumnCount);
        }

        [Fact]
        public void Excel日期不会显示成45292()
        {
            TestDataGenerator.Generate(_dir);
            var sheet = TableReader.Load(Path.Combine(_dir, "表A.xlsx")).First;

            string date = sheet.Rows[0].Get(4);
            Assert.StartsWith("2026-01-01", date);
        }

        [Fact]
        public void 金额是干净数字没有浮点尾巴()
        {
            TestDataGenerator.Generate(_dir);
            var sheet = TableReader.Load(Path.Combine(_dir, "表A.xlsx")).First;

            string amount = sheet.Rows[0].Get(3);
            Assert.DoesNotContain("0000000", amount);
            Assert.Equal("1037.5", amount);
        }

        [Fact]
        public void 比对结果刚好是3改1删2增()
        {
            TestDataGenerator.Generate(_dir);
            var a = TableReader.Load(Path.Combine(_dir, "表A.xlsx")).First;
            var b = TableReader.Load(Path.Combine(_dir, "表B.xlsx")).First;

            var result = Build.Run(a, b, Build.Keys(0));

            Assert.Equal(3, result.ChangedRowCount);
            Assert.Equal(3, result.ChangedCellCount);
            Assert.Equal(1, result.OnlyACount);
            Assert.Equal(2, result.OnlyBCount);
            Assert.Equal(196, result.SameCount);   // 200 − 3 改 − 1 删
            Assert.Equal(200, result.RowCountA);
            Assert.Equal(201, result.RowCountB);
            Assert.Empty(result.DuplicateKeys);
        }

        [Fact]
        public void csv与xlsx的结论一致()
        {
            TestDataGenerator.Generate(_dir);
            string csvPath = Path.Combine(_dir, "导出的清单.csv");
            var a = TableReader.Load(Path.Combine(_dir, "表A.xlsx")).First;
            var b = TableReader.Load(Path.Combine(_dir, "表B.xlsx")).First;
            var result = Build.Run(a, b, Build.Keys(0));

            CsvDiffExporter.Export(result, csvPath);
            var lines = CsvDiffExporter.EnumerateLines(result);

            Assert.Equal(6, lines.Count);   // 3 改 + 1 仅A + 2 仅B
            Assert.Equal(7, File.ReadAllLines(csvPath).Length);
            Assert.Equal(3, lines.Count(l => l.Type == "字段变更"));
        }
    }
}
