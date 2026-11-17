using System;
using TableDiff.Core;

namespace TableDiff.Tests
{
    /// <summary>导出 CSV 时的转义与防注入。</summary>
    public class CsvExportTests
    {
        [Theory]
        [InlineData("=1+1", "'=1+1")]
        [InlineData("=HYPERLINK(\"http://x\",\"点我\")", "\"'=HYPERLINK(\"\"http://x\"\",\"\"点我\"\")\"")]
        [InlineData("@SUM(A1)", "'@SUM(A1)")]
        [InlineData("+cmd|' /C calc'!A0", "'+cmd|' /C calc'!A0")]
        [InlineData("-2+3", "'-2+3")]
        public void 公式样子的值前面加单引号(string raw, string expected)
        {
            Assert.Equal(expected, CsvDiffExporter.Escape(raw));
        }

        [Theory]
        [InlineData("-100", "-100")]
        [InlineData("-12.5", "-12.5")]
        [InlineData("100", "100")]
        [InlineData("张三", "张三")]
        [InlineData("", "")]
        public void 普通数据不受影响(string raw, string expected)
        {
            Assert.Equal(expected, CsvDiffExporter.Escape(raw));
        }

        [Fact]
        public void 逗号与引号仍然按CSV规范转义()
        {
            Assert.Equal("\"a,b\"", CsvDiffExporter.Escape("a,b"));
            Assert.Equal("\"他说\"\"你好\"\"\"", CsvDiffExporter.Escape("他说\"你好\""));
            Assert.Equal("\"第一行\r\n第二行\"", CsvDiffExporter.Escape("第一行\r\n第二行"));
        }

        [Fact]
        public void 导出的文件带BOM且能读回来()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "diff-" + Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                var a = Build.Sheet("A", new[] { "单号", "金额" }, new[] { "P1", "100" });
                var b = Build.Sheet("B", new[] { "单号", "金额" }, new[] { "P1", "200" });
                var result = Build.Run(a, b, Build.Keys(0));

                CsvDiffExporter.Export(result, path);

                byte[] bytes = System.IO.File.ReadAllBytes(path);
                Assert.True(bytes.Length > 3);
                Assert.Equal(0xEF, bytes[0]);
                Assert.Equal(0xBB, bytes[1]);
                Assert.Equal(0xBF, bytes[2]);

                string text = System.IO.File.ReadAllText(path);
                Assert.Contains("序号,类型,主键,列名,表A值,表B值,说明", text);
                Assert.Contains("字段变更,P1,金额,100,200", text);
            }
            finally
            {
                try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { }
            }
        }
    }
}
