using System;
using System.Linq;
using TableDiff.Core;

namespace TableDiff.Tests
{
    /// <summary>自检发现的边界问题：先写用例钉住，再修实现。</summary>
    public class RegressionTests
    {
        [Fact]
        public void 主键重复_A侧两条只配到一条B_另一条要算仅A()
        {
            var a = Build.Sheet("A", new[] { "单号", "金额" },
                new[] { "P1", "100" },
                new[] { "P1", "100" });
            var b = Build.Sheet("B", new[] { "单号", "金额" },
                new[] { "P1", "100" });

            var r = Build.Run(a, b, Build.Keys(0));

            // 一条匹配上，另一条无处可配 → 仅A；不能两条都算「相同」
            Assert.Equal(1, r.SameCount + r.ChangedRowCount);
            Assert.Equal(1, r.OnlyACount);
            Assert.Equal(0, r.OnlyBCount);
            Assert.Single(r.DuplicateKeys);
            Assert.Equal(2, r.DuplicateKeys[0].CountA);
            Assert.Equal(1, r.DuplicateKeys[0].CountB);
        }

        [Fact]
        public void 主键重复_A两条B两条_逐条配对不重复占用()
        {
            var a = Build.Sheet("A", new[] { "单号", "金额" },
                new[] { "P1", "100" },
                new[] { "P1", "200" });
            var b = Build.Sheet("B", new[] { "单号", "金额" },
                new[] { "P1", "100" },
                new[] { "P1", "300" });

            var r = Build.Run(a, b, Build.Keys(0));

            Assert.Equal(1, r.SameCount);       // 100 对 100
            Assert.Equal(1, r.ChangedRowCount); // 200 对 300
            Assert.Equal(0, r.OnlyACount);
            Assert.Equal(0, r.OnlyBCount);
        }

        [Fact]
        public void 按整行比对_两侧列顺序不同_相同行要显示A侧的值()
        {
            var a = Build.Sheet("A", new[] { "编号", "金额" },
                new[] { "P1", "100" });
            var b = Build.Sheet("B", new[] { "金额", "编号" },
                new[] { "100", "P1" });

            var options = new DiffOptions { Mode = DiffMode.ByWholeRow };
            var r = Build.Run(a, b, options);

            var row = Assert.Single(r.Rows);
            Assert.Equal(RowStatus.Same, row.Status);
            Assert.Equal("P1", row.Cells[0].ValueA);
            Assert.Equal("100", row.Cells[1].ValueA);
        }

        [Fact]
        public void 文本NaNInfinity不当数字_不能和空值算相等()
        {
            Assert.False(CellText.TryParseNumber("NaN", out _));
            Assert.False(CellText.TryParseNumber("Infinity", out _));
            Assert.False(CellText.TryParseNumber("-Infinity", out _));
            Assert.False(CellText.TryParseNumber("∞", out _));
            Assert.NotEqual(CellText.Canonical("NaN", false, true, false), CellText.Canonical("", false, true, false));
        }

        [Fact]
        public void 超长数字串不按数字规整_避免丢精度()
        {
            // 18 位订单号：按 double 转会被截成 1.23456789012346E+17
            const string id = "123456789012345678";
            Assert.NotEqual("1.23456789012346E+17", CellText.Canonical(id, false, true, false));
            Assert.Equal(id, CellText.Canonical(id, false, true, false));
        }
    }
}
