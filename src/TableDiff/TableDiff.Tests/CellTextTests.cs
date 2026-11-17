using System;
using System.Globalization;
using TableDiff.Core;

namespace TableDiff.Tests
{
    public class CellTextTests
    {
        [Fact]
        public void 数字去掉浮点尾巴()
        {
            Assert.Equal("1.2", CellText.FormatNumber(1.2000000000000002));
        }

        [Fact]
        public void 整数不带小数点()
        {
            Assert.Equal("200", CellText.FormatNumber(200.0));
            Assert.Equal("0", CellText.FormatNumber(-0.0));
        }

        [Fact]
        public void 日期显示为yyyy_MM_dd()
        {
            var d = new DateTime(2026, 11, 14);
            Assert.Equal("2026-11-14", CellText.FormatDate(d));
        }

        [Fact]
        public void 带时间的日期保留到分钟()
        {
            var d = new DateTime(2026, 11, 14, 9, 30, 0);
            Assert.Equal("2026-11-14 09:30", CellText.FormatDate(d));
        }

        [Fact]
        public void 千分位显示只对整数生效()
        {
            Assert.Equal("1,200", CellText.WithThousands("1200"));
            Assert.Equal("1200.5", CellText.WithThousands("1200.5"));
            Assert.Equal("甲", CellText.WithThousands("甲"));
        }

        [Fact]
        public void 带千分位的输入能解析成数字()
        {
            Assert.True(CellText.TryParseNumber("1,234.56", out double v));
            Assert.Equal(1234.56, v, 6);
        }

        [Fact]
        public void 空白的数字文本不算数字()
        {
            Assert.False(CellText.TryParseNumber("", out _));
            Assert.False(CellText.TryParseNumber("   ", out _));
            Assert.False(CellText.TryParseNumber("-", out _));
            Assert.False(CellText.TryParseNumber("甲", out _));
        }

        [Fact]
        public void 全空行判断()
        {
            Assert.True(CellText.IsBlankRow(new[] { "", "  ", null }));
            Assert.False(CellText.IsBlankRow(new[] { "", "x" }));
        }
    }
}
