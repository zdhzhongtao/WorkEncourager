using System;
using System.Linq;
using TableDiff.Core;

namespace TableDiff.Tests
{
    public class ColumnMapperTests
    {
        [Fact]
        public void 按表头名匹配_列序不同也能对上()
        {
            var a = Build.Sheet("A", new[] { "单号", "客户", "金额" });
            var b = Build.Sheet("B", new[] { "金额", "单号", "客户" });

            var maps = ColumnMapper.ByName(a, b);

            Assert.Equal(3, maps.Count);
            Assert.Equal(0, maps[0].IndexA);
            Assert.Equal(1, maps[0].IndexB);   // 单号在 B 是第 2 列
            Assert.Equal(2, maps[2].IndexA);
            Assert.Equal(0, maps[2].IndexB);   // 金额在 B 是第 1 列
        }

        [Fact]
        public void 缺列_IndexB为负且照样显示()
        {
            var a = Build.Sheet("A", new[] { "单号", "客户", "金额" });
            var b = Build.Sheet("B", new[] { "单号", "金额" });

            var maps = ColumnMapper.ByName(a, b);

            var customer = maps.First(m => m.Name == "客户");
            Assert.Equal(-1, customer.IndexB);
            Assert.Equal(3, maps.Count);
        }

        [Fact]
        public void 多出来的列_追加到末尾并标注仅B()
        {
            var a = Build.Sheet("A", new[] { "单号" });
            var b = Build.Sheet("B", new[] { "单号", "新列" });

            var maps = ColumnMapper.ByName(a, b);

            Assert.Equal(2, maps.Count);
            Assert.Contains("新列", maps[1].Name);
            Assert.Contains("仅B", maps[1].Name);
            Assert.Equal(-1, maps[1].IndexA);
            Assert.Equal(1, maps[1].IndexB);
        }

        [Fact]
        public void 按位置匹配_列名不同也对齐()
        {
            var a = Build.Sheet("A", new[] { "单号", "客户" });
            var b = Build.Sheet("B", new[] { "编号", "客户名" });

            var maps = ColumnMapper.ByPosition(a, b);

            Assert.Equal(2, maps.Count);
            Assert.Equal("单号", maps[0].Name);
            Assert.Equal(0, maps[0].IndexA);
            Assert.Equal(0, maps[0].IndexB);
        }

        [Fact]
        public void 按位置匹配_B列更多时多出的列只有B侧()
        {
            var a = Build.Sheet("A", new[] { "单号" });
            var b = Build.Sheet("B", new[] { "单号", "金额" });

            var maps = ColumnMapper.ByPosition(a, b);

            Assert.Equal(2, maps.Count);
            Assert.Equal(-1, maps[1].IndexA);
            Assert.Equal(1, maps[1].IndexB);
        }

        [Fact]
        public void 表头重名_自动加后缀()
        {
            var s = Build.SheetRaw("S", new[] { "金额", "金额", "备注" }, new[] { "1", "2", "x" });

            Assert.Equal("金额", s.Columns[0]);
            Assert.Equal("金额(2)", s.Columns[1]);
        }

        [Fact]
        public void 表头为空_自动补列N()
        {
            var s = Build.SheetRaw("S", new[] { "", "客户" }, new[] { "1", "甲" });

            Assert.Equal("列1", s.Columns[0]);
            Assert.Equal("客户", s.Columns[1]);
        }
    }
}
