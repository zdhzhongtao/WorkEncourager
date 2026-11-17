using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SheetSplitter.Core;

namespace SheetSplitter.Tests
{
    /// <summary>把规格书第 6.5 节的验收链路跑成自动化：拆 → 逐个打开 → 合回去 → 按行切。</summary>
    public class AcceptanceTests
    {
        [Fact]
        public void 五百行总表跑完四个模式()
        {
            string dir = Build.TempDir();
            string source = Path.Combine(dir, "总表.xlsx");
            TestDataGenerator.Write(source);

            var before = new FileInfo(source);
            long size = before.Length;
            DateTime stamp = before.LastWriteTimeUtc;

            var sheet = TableReader.Load(source).First;
            Assert.Equal(500, sheet.Rows.Count);

            // ---- 模式 1：按部门拆 ----
            string outSplit = Path.Combine(dir, "split");
            var plan1 = SplitPlanner.PlanByColumn(sheet, 1, "总表", true, false);
            Assert.Equal(13, plan1.Files.Count);        // 12 部门 + 空白

            var report1 = SplitExecutor.Execute(plan1, outSplit);
            Assert.Equal(13, report1.Written.Count);
            Assert.Empty(report1.Failed);
            Assert.Contains(report1.Written, n => n.Contains("空白"));
            Assert.Contains(report1.Written, n => n.Contains("研发_测试部"));

            // 每个文件都能重新打开，且带表头
            int rowsBack = 0;
            foreach (var name in report1.Written)
            {
                var part = TableReader.Load(Path.Combine(outSplit, name)).First;
                Assert.Equal(6, part.ColumnCount);
                Assert.Equal("单号", part.Columns[0]);
                rowsBack += part.Rows.Count;
            }
            Assert.Equal(500, rowsBack);

            // ---- 模式 2：把拆出来的 13 个文件合回去 ----
            string outMerge = Path.Combine(dir, "merge");
            var inputs = report1.Written
                .Select(name => new MergeInput
                {
                    Path = Path.Combine(outSplit, name),
                    Data = TableReader.Load(Path.Combine(outSplit, name)).First
                })
                .ToList();

            var plan2 = SplitPlanner.PlanMerge(inputs, HeaderAlignment.ByName, true, "合并.xlsx", "合并结果");
            Assert.Empty(plan2.MissingColumns);

            var report2 = SplitExecutor.Execute(plan2, outMerge);
            Assert.Single(report2.Written);
            var merged = TableReader.Load(Path.Combine(outMerge, report2.Written[0])).First;
            Assert.Equal(500, merged.Rows.Count);
            Assert.Equal(8, merged.ColumnCount);        // 6 列 + 来源文件 + 来源sheet

            var originalKeys = new HashSet<string>(sheet.Rows.Select(r => r.Get(0)));
            var mergedKeys = new HashSet<string>(merged.Rows.Select(r => r.Get(0)));
            Assert.Equal(originalKeys.Count, mergedKeys.Count);
            Assert.True(originalKeys.SetEquals(mergedKeys));

            // ---- 模式 3：单文件多 sheet 合并（这里用一张表模拟）----
            string outSheets = Path.Combine(dir, "sheets");
            var sameFile = new List<MergeInput>
            {
                new MergeInput { Path = source, Data = sheet },
                new MergeInput { Path = source, Data = sheet }
            };
            var plan3 = SplitPlanner.PlanMerge(sameFile, HeaderAlignment.ByName, true, "sheet合并.xlsx", "合并结果");
            var report3 = SplitExecutor.Execute(plan3, outSheets);
            var doubled = TableReader.Load(Path.Combine(outSheets, report3.Written[0])).First;
            Assert.Equal(1000, doubled.Rows.Count);

            // ---- 模式 4：按 100 行切 ----
            string outRows = Path.Combine(dir, "rows");
            var plan4 = SplitPlanner.PlanByRows(sheet, 100, "总表", true);
            Assert.Equal(5, plan4.Files.Count);

            var report4 = SplitExecutor.Execute(plan4, outRows);
            Assert.Equal(5, report4.Written.Count);
            int sum = report4.Written.Sum(n => TableReader.Load(Path.Combine(outRows, n)).First.Rows.Count);
            Assert.Equal(500, sum);

            // ---- 源文件一个字节都没动 ----
            var after = new FileInfo(source);
            Assert.Equal(size, after.Length);
            Assert.Equal(stamp, after.LastWriteTimeUtc);
        }
    }
}
