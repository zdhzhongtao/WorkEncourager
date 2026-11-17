using System;
using System.Linq;
using TableDiff.Core;

namespace TableDiff.Tests
{
    public class DiffEngineTests
    {
        private static SheetData A() => Build.Sheet("A",
            new[] { "单号", "客户", "金额" },
            new[] { "P1", "甲公司", "100" },
            new[] { "P2", "乙公司", "200" },
            new[] { "P3", "丙公司", "300" });

        private static SheetData B() => Build.Sheet("B",
            new[] { "单号", "客户", "金额" },
            new[] { "P1", "甲公司", "100" },
            new[] { "P2", "乙公司", "250" },
            new[] { "P4", "丁公司", "400" });

        [Fact]
        public void 单主键_能分出相同_改动_仅A_仅B()
        {
            var r = Build.Run(A(), B(), Build.Keys(0));

            Assert.Equal(3, r.RowCountA);
            Assert.Equal(3, r.RowCountB);
            Assert.Equal(1, r.SameCount);
            Assert.Equal(1, r.OnlyACount);      // P3
            Assert.Equal(1, r.OnlyBCount);      // P4
            Assert.Equal(1, r.ChangedRowCount); // P2
            Assert.Equal(1, r.ChangedCellCount);
        }

        [Fact]
        public void 单主键_改动行的前后值都要保留()
        {
            var r = Build.Run(A(), B(), Build.Keys(0));
            var row = Build.Row(r, "P2");

            Assert.NotNull(row);
            Assert.Equal(RowStatus.Changed, row.Status);
            Assert.Equal("200", row.Cells[2].ValueA);
            Assert.Equal("250", row.Cells[2].ValueB);
            Assert.True(row.Cells[2].Changed);
            Assert.False(row.Cells[1].Changed);
        }

        [Fact]
        public void 复合主键_两列一起才算同一个()
        {
            var a = Build.Sheet("A", new[] { "单号", "行号", "金额" },
                new[] { "P1", "1", "100" },
                new[] { "P1", "2", "200" });
            var b = Build.Sheet("B", new[] { "单号", "行号", "金额" },
                new[] { "P1", "1", "100" },
                new[] { "P1", "2", "999" });

            var r = Build.Run(a, b, Build.Keys(0, 1));

            Assert.Equal(1, r.SameCount);
            Assert.Equal(1, r.ChangedRowCount);
            Assert.Equal(0, r.OnlyACount);
        }

        [Fact]
        public void 主键重复_必须告警且不静默吞掉()
        {
            var a = Build.Sheet("A", new[] { "单号", "金额" },
                new[] { "P1", "100" },
                new[] { "P1", "200" });
            var b = Build.Sheet("B", new[] { "单号", "金额" },
                new[] { "P1", "100" });

            var r = Build.Run(a, b, Build.Keys(0));

            Assert.Single(r.DuplicateKeys);
            Assert.Equal(2, r.DuplicateKeys[0].CountA);
            Assert.Contains(r.Notes, n => n.Contains("主键重复"));
        }

        [Fact]
        public void 主键重复_B侧也要告警()
        {
            var a = Build.Sheet("A", new[] { "单号", "金额" }, new[] { "P1", "100" });
            var b = Build.Sheet("B", new[] { "单号", "金额" },
                new[] { "P1", "100" },
                new[] { "P1", "100" });

            var r = Build.Run(a, b, Build.Keys(0));

            Assert.Single(r.DuplicateKeys);
            Assert.Equal(2, r.DuplicateKeys[0].CountB);
        }

        [Fact]
        public void 没有选主键_给中文错误而不是崩掉()
        {
            var ex = Assert.Throws<FriendlyException>(() => Build.Run(A(), B(), new DiffOptions { Mode = DiffMode.ByKey }));
            Assert.Contains("主键", ex.Message);
        }

        [Fact]
        public void 数值容差内_算相同()
        {
            var a = Build.Sheet("A", new[] { "单号", "金额" }, new[] { "P1", "1000.001" });
            var b = Build.Sheet("B", new[] { "单号", "金额" }, new[] { "P1", "1000.009" });

            var o = Build.Keys(0);
            o.NumberTolerance = 0.01;
            var r = Build.Run(a, b, o);

            Assert.Equal(1, r.SameCount);
            Assert.Equal(0, r.ChangedCellCount);
        }

        [Fact]
        public void 超出容差_算不同()
        {
            var a = Build.Sheet("A", new[] { "单号", "金额" }, new[] { "P1", "1000.00" });
            var b = Build.Sheet("B", new[] { "单号", "金额" }, new[] { "P1", "1000.02" });

            var o = Build.Keys(0);
            o.NumberTolerance = 0.01;
            var r = Build.Run(a, b, o);

            Assert.Equal(1, r.ChangedRowCount);
        }

        [Fact]
        public void 字符串数字与真数字等价()
        {
            var a = Build.Sheet("A", new[] { "单号", "金额" }, new[] { "P1", "1000" });
            var b = Build.Sheet("B", new[] { "单号", "金额" }, new[] { "P1", "1000.00" });

            var r = Build.Run(a, b, Build.Keys(0));

            Assert.Equal(1, r.SameCount);
        }

        [Fact]
        public void 浮点尾巴不影响判断()
        {
            var a = Build.Sheet("A", new[] { "单号", "金额" }, new[] { "P1", "1.2" });
            var b = Build.Sheet("B", new[] { "单号", "金额" }, new[] { "P1", "1.2000000000000002" });

            var r = Build.Run(a, b, Build.Keys(0));

            Assert.Equal(1, r.SameCount);
        }

        [Fact]
        public void 忽略列_不参与比对()
        {
            var a = Build.Sheet("A", new[] { "单号", "备注" }, new[] { "P1", "旧" });
            var b = Build.Sheet("B", new[] { "单号", "备注" }, new[] { "P1", "新" });

            var o = Build.Keys(0);
            o.Columns.Add(new ColumnMap("单号", 0, 0, true));
            o.Columns.Add(new ColumnMap("备注", 1, 1, false));

            var r = DiffEngine.Compare(a, b, o);

            Assert.Equal(1, r.SameCount);
        }

        [Fact]
        public void 忽略大小写()
        {
            var a = Build.Sheet("A", new[] { "单号", "客户" }, new[] { "P1", "ACME" });
            var b = Build.Sheet("B", new[] { "单号", "客户" }, new[] { "P1", "acme" });

            var o = Build.Keys(0);
            o.IgnoreCase = true;
            var r = Build.Run(a, b, o);

            Assert.Equal(1, r.SameCount);
        }

        [Fact]
        public void 不忽略大小写时判为不同()
        {
            var a = Build.Sheet("A", new[] { "单号", "客户" }, new[] { "P1", "ACME" });
            var b = Build.Sheet("B", new[] { "单号", "客户" }, new[] { "P1", "acme" });

            var r = Build.Run(a, b, Build.Keys(0));

            Assert.Equal(1, r.ChangedRowCount);
        }

        [Fact]
        public void 忽略首尾空格()
        {
            var a = Build.Sheet("A", new[] { "单号", "客户" }, new[] { "P1", "  甲公司  " });
            var b = Build.Sheet("B", new[] { "单号", "客户" }, new[] { "P1", "甲公司" });

            var r = Build.Run(a, b, Build.Keys(0));

            Assert.Equal(1, r.SameCount);
        }

        [Fact]
        public void 空单元格与空串视为相同()
        {
            var a = Build.Sheet("A", new[] { "单号", "客户" }, new[] { "P1", "" });
            var b = Build.Sheet("B", new[] { "单号", "客户" }, new[] { "P1", "   " });

            var r = Build.Run(a, b, Build.Keys(0));

            Assert.Equal(1, r.SameCount);
        }

        [Fact]
        public void 空单元格与有值视为不同()
        {
            var a = Build.Sheet("A", new[] { "单号", "客户" }, new[] { "P1", "" });
            var b = Build.Sheet("B", new[] { "单号", "客户" }, new[] { "P1", "甲公司" });

            var r = Build.Run(a, b, Build.Keys(0));

            Assert.Equal(1, r.ChangedRowCount);
        }

        [Fact]
        public void 忽略全空行_行数不计入()
        {
            var a = Build.Sheet("A", new[] { "单号", "客户" },
                new[] { "P1", "甲" },
                new[] { "", "" },
                new[] { "P2", "乙" });
            var b = Build.Sheet("B", new[] { "单号", "客户" }, new[] { "P1", "甲" }, new[] { "P2", "乙" });

            var r = Build.Run(a, b, Build.Keys(0));

            Assert.Equal(2, r.RowCountA);
            Assert.Equal(2, r.SameCount);
            Assert.Contains(r.Notes, n => n.Contains("全空行"));
        }

        [Fact]
        public void 按行位置_两边行数不等()
        {
            var a = Build.Sheet("A", new[] { "客户", "金额" },
                new[] { "甲", "100" },
                new[] { "乙", "200" });
            var b = Build.Sheet("B", new[] { "客户", "金额" },
                new[] { "甲", "100" },
                new[] { "乙", "200" },
                new[] { "丙", "300" });

            var o = new DiffOptions { Mode = DiffMode.ByPosition };
            var r = Build.Run(a, b, o);

            Assert.Equal(2, r.SameCount);
            Assert.Equal(1, r.OnlyBCount);
            Assert.Equal(0, r.OnlyACount);
        }

        [Fact]
        public void 按行位置_中间某行改动()
        {
            var a = Build.Sheet("A", new[] { "客户" }, new[] { "甲" }, new[] { "乙" });
            var b = Build.Sheet("B", new[] { "客户" }, new[] { "甲" }, new[] { "乙改" });

            var o = new DiffOptions { Mode = DiffMode.ByPosition };
            var r = Build.Run(a, b, o);

            Assert.Equal(1, r.SameCount);
            Assert.Equal(1, r.ChangedRowCount);
            Assert.Equal("第 2 行", r.Rows[1].Key);
        }

        [Fact]
        public void 按整行内容_位置变了但内容一样算相同()
        {
            var a = Build.Sheet("A", new[] { "客户", "金额" },
                new[] { "甲", "100" },
                new[] { "乙", "200" });
            var b = Build.Sheet("B", new[] { "客户", "金额" },
                new[] { "乙", "200" },
                new[] { "甲", "100" });

            var o = new DiffOptions { Mode = DiffMode.ByWholeRow };
            var r = Build.Run(a, b, o);

            Assert.Equal(2, r.SameCount);
            Assert.Equal(0, r.OnlyACount);
            Assert.Equal(0, r.OnlyBCount);
        }

        [Fact]
        public void 按整行内容_只多一行()
        {
            var a = Build.Sheet("A", new[] { "客户" }, new[] { "甲" });
            var b = Build.Sheet("B", new[] { "客户" }, new[] { "甲" }, new[] { "乙" });

            var o = new DiffOptions { Mode = DiffMode.ByWholeRow };
            var r = Build.Run(a, b, o);

            Assert.Equal(1, r.SameCount);
            Assert.Equal(1, r.OnlyBCount);
        }

        [Fact]
        public void 汇总数字与行状态一致()
        {
            var r = Build.Run(A(), B(), Build.Keys(0));

            int same = r.Rows.Count(x => x.Status == RowStatus.Same);
            int onlyA = r.Rows.Count(x => x.Status == RowStatus.OnlyA);
            int onlyB = r.Rows.Count(x => x.Status == RowStatus.OnlyB);
            int changed = r.Rows.Count(x => x.Status == RowStatus.Changed);

            Assert.Equal(same, r.SameCount);
            Assert.Equal(onlyA, r.OnlyACount);
            Assert.Equal(onlyB, r.OnlyBCount);
            Assert.Equal(changed, r.ChangedRowCount);
            Assert.Equal(r.Rows.Sum(x => x.ChangedCellCount), r.ChangedCellCount);
        }

        [Fact]
        public void 可选取消_能中断长任务()
        {
            var a = Build.Sheet("A", new[] { "单号" }, Enumerable.Range(0, 5000).Select(i => new[] { "P" + i }).ToArray());
            var b = Build.Sheet("B", new[] { "单号" }, Enumerable.Range(0, 5000).Select(i => new[] { "P" + i }).ToArray());

            using (var cts = new System.Threading.CancellationTokenSource())
            {
                cts.Cancel();
                Assert.Throws<OperationCanceledException>(() =>
                    DiffEngine.Compare(a, b, Build.Keys(0).WithColumns(a, b), null, cts.Token));
            }
        }
    }
}
