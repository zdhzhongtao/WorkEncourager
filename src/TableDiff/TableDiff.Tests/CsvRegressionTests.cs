using System;
using TableDiff.Core;

namespace TableDiff.Tests
{
    /// <summary>CSV 解析的容错边界：表里出现英寸符号、单个引号不能把后面的内容全吞掉。</summary>
    public class CsvRegressionTests
    {
        [Fact]
        public void 单元格中间的单个引号按普通字符处理_不能吞掉后续行()
        {
            string text = "名称,规格\n显示器,27\"\n键盘,104键\n";
            var rows = CsvReader.Parse(text, ',');

            Assert.Equal(3, rows.Count);
            Assert.Equal(new[] { "显示器", "27\"" }, rows[1]);
            Assert.Equal(new[] { "键盘", "104键" }, rows[2]);
        }

        [Fact]
        public void 字段开头的引号仍然按转义解析()
        {
            string text = "a,\"b,c\",d\n";
            var rows = CsvReader.Parse(text, ',');
            Assert.Single(rows);
            Assert.Equal(new[] { "a", "b,c", "d" }, rows[0]);
        }

        [Fact]
        public void 引号内的双写引号还原成一个()
        {
            var rows = CsvReader.Parse("x,\"他说\"\"你好\"\"\"\n", ',');
            Assert.Equal(new[] { "x", "他说\"你好\"" }, rows[0]);
        }

        [Fact]
        public void 引号内换行保留在同一格里()
        {
            var rows = CsvReader.Parse("a,\"第一行\n第二行\",c\n", ',');
            Assert.Single(rows);
            Assert.Equal("第一行\n第二行", rows[0][1]);
        }
    }
}
