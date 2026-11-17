using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using SheetSplitter.Core;

namespace SheetSplitter.Tests
{
    internal static class Build
    {
        public static SheetData Sheet(string name, string[] headers, params string[][] rows)
        {
            var sheet = new SheetData { Name = name };
            sheet.SetColumns(headers, headers.Length);
            for (int i = 0; i < rows.Length; i++)
                sheet.Rows.Add(new TableRow(rows[i], i + 2));
            return sheet;
        }

        public static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "SheetSplitterTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public class FileNameSanitizerTests
    {
        [Fact]
        public void 非法字符换成下划线()
        {
            Assert.Equal("研发_测试部", FileNameSanitizer.Clean("研发/测试部"));
            Assert.Equal("a_b_c_d_e_f_g_h_i", FileNameSanitizer.Clean("a\\b:c*d?e\"f<g>h|i"));
        }

        [Fact]
        public void 空值用空白兜底()
        {
            Assert.Equal("空白", FileNameSanitizer.Clean(""));
            Assert.Equal("空白", FileNameSanitizer.Clean("   "));
            Assert.Equal("空白", FileNameSanitizer.Clean(null));
        }

        [Fact]
        public void 去掉首尾空格和点()
        {
            Assert.Equal("财务部", FileNameSanitizer.Clean("  财务部.  "));
            Assert.Equal("财务部", FileNameSanitizer.Clean(".财务部."));
        }

        [Fact]
        public void 超长截断到60()
        {
            string value = new string('甲', 100);
            Assert.Equal(60, FileNameSanitizer.Clean(value).Length);
        }

        [Fact]
        public void 纯中文完整保留()
        {
            Assert.Equal("上海鼎新贸易有限公司", FileNameSanitizer.Clean("上海鼎新贸易有限公司"));
        }

        [Fact]
        public void 拼文件名时整体也不超60()
        {
            string name = FileNameSanitizer.BuildFileName(new string('长', 40), new string('值', 40));
            Assert.True(name.Length <= 65, "实际长度 " + name.Length);
            Assert.EndsWith(".xlsx", name);
        }

        [Fact]
        public void 重名自动加序号()
        {
            string dir = Build.TempDir();
            string first = FileNameSanitizer.ResolveUnique(dir, "表_财务部.xlsx");
            File.WriteAllText(Path.Combine(dir, first), "");

            string second = FileNameSanitizer.ResolveUnique(dir, "表_财务部.xlsx");
            File.WriteAllText(Path.Combine(dir, second), "");
            string third = FileNameSanitizer.ResolveUnique(dir, "表_财务部.xlsx");

            Assert.Equal("表_财务部.xlsx", first);
            Assert.Equal("表_财务部 (1).xlsx", second);
            Assert.Equal("表_财务部 (2).xlsx", third);
        }

        [Fact]
        public void 同一批里重名也算占用()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string a = FileNameSanitizer.ResolveUnique("", "x.xlsx", used);
            string b = FileNameSanitizer.ResolveUnique("", "x.xlsx", used);

            Assert.Equal("x.xlsx", a);
            Assert.Equal("x (1).xlsx", b);
        }
    }

    public class GroupTests
    {
        private static SheetData Sample()
        {
            return Build.Sheet("总表", new[] { "单号", "部门", "金额" },
                new[] { "T1", "财务部", "100" },
                new[] { "T2", "采购部", "200" },
                new[] { "T3", "财务部", "300" },
                new[] { "T4", "", "400" },
                new[] { "T5", "销售部", "500" });
        }

        [Fact]
        public void 按列值分组_组数与成员正确()
        {
            var groups = SplitPlanner.GroupByColumn(Sample(), 1);

            Assert.Equal(4, groups.Count);   // 财务部 / 采购部 / 销售部 / 空白
            Assert.Equal(2, groups[0].Value.Count);
            Assert.Equal("财务部", groups[0].Key);
        }

        [Fact]
        public void 空值单独一组并排在最后()
        {
            var groups = SplitPlanner.GroupByColumn(Sample(), 1);

            Assert.Equal(string.Empty, groups[groups.Count - 1].Key);
            Assert.Single(groups[groups.Count - 1].Value);
        }

        [Fact]
        public void 单值表只出一组()
        {
            var sheet = Build.Sheet("S", new[] { "部门" }, new[] { "财务部" }, new[] { "财务部" });
            var groups = SplitPlanner.GroupByColumn(sheet, 0);

            Assert.Single(groups);
            Assert.Equal(2, groups[0].Value.Count);
        }

        [Fact]
        public void 含空格与点的值会先清洗再命名()
        {
            var sheet = Build.Sheet("S", new[] { "部门" }, new[] { " 财务 部. " });
            var plan = SplitPlanner.PlanByColumn(sheet, 0, "总表", true, false);

            Assert.Single(plan.Files);
            Assert.Equal("总表_财务 部.xlsx", plan.Files[0].FileName);
        }

        [Fact]
        public void 拆分列下标越界给中文提示()
        {
            var ex = Assert.Throws<FriendlyException>(() => SplitPlanner.GroupByColumn(Sample(), 9));
            Assert.Contains("哪一列", ex.Message);
        }

        [Fact]
        public void 拆分文件保留表头与行数()
        {
            var plan = SplitPlanner.PlanByColumn(Sample(), 1, "总表", true, false);
            var finance = plan.Files.First(f => f.FileName.Contains("财务部"));

            Assert.Equal(3, finance.Sheets[0].Headers.Count);
            Assert.Equal(2, finance.Sheets[0].Rows.Count);
            Assert.Equal("总表", finance.Sheets[0].SheetName);
        }

        [Fact]
        public void 拆分可以不含表头()
        {
            var plan = SplitPlanner.PlanByColumn(Sample(), 1, "总表", false, false);

            Assert.Empty(plan.Files[0].Sheets[0].Headers);
        }

        [Fact]
        public void 拆成多个sheet时只有一个文件()
        {
            var plan = SplitPlanner.PlanByColumn(Sample(), 1, "总表", true, true);

            Assert.Single(plan.Files);
            Assert.Equal(4, plan.Files[0].Sheets.Count);
            Assert.Equal(5, plan.Files[0].RowCount);
        }

        [Fact]
        public void 空表拆分不生成文件()
        {
            var sheet = Build.Sheet("S", new[] { "部门" });
            var plan = SplitPlanner.PlanByColumn(sheet, 0, "总表", true, false);

            Assert.Empty(plan.Files);
            Assert.Contains(plan.Notes, n => n.Contains("没有数据行"));
        }
    }

    public class MergeTests
    {
        private static MergeInput Input(string file, string sheetName, string[] headers, params string[][] rows)
        {
            return new MergeInput { Path = file, Data = Build.Sheet(sheetName, headers, rows) };
        }

        [Fact]
        public void 按列名对齐_列序不同也能合()
        {
            var a = Input("a.xlsx", "Sheet1", new[] { "单号", "金额" }, new[] { "T1", "100" });
            var b = Input("b.xlsx", "Sheet1", new[] { "金额", "单号" }, new[] { "200", "T2" });

            var plan = SplitPlanner.PlanMerge(new[] { a, b }, HeaderAlignment.ByName, false, "合并.xlsx", "合并结果");
            var sheet = plan.Files[0].Sheets[0];

            Assert.Equal(new[] { "单号", "金额" }, sheet.Headers.ToArray());
            Assert.Equal(2, sheet.Rows.Count);
            Assert.Equal("T2", sheet.Rows[1].Get(0));
            Assert.Equal("200", sheet.Rows[1].Get(1));
        }

        [Fact]
        public void 缺列留空并记录缺了哪列()
        {
            var a = Input("a.xlsx", "Sheet1", new[] { "单号", "金额" }, new[] { "T1", "100" });
            var b = Input("b.xlsx", "Sheet1", new[] { "单号" }, new[] { "T2" });

            var plan = SplitPlanner.PlanMerge(new[] { a, b }, HeaderAlignment.ByName, false, "合并.xlsx", "合并结果");

            Assert.Single(plan.MissingColumns);
            Assert.Contains("b.xlsx", plan.MissingColumns[0]);
            Assert.Contains("金额", plan.MissingColumns[0]);
            Assert.Equal(string.Empty, plan.Files[0].Sheets[0].Rows[1].Get(1));
        }

        [Fact]
        public void 多出的列追加到末尾()
        {
            var a = Input("a.xlsx", "Sheet1", new[] { "单号" }, new[] { "T1" });
            var b = Input("b.xlsx", "Sheet1", new[] { "单号", "新列" }, new[] { "T2", "x" });

            var plan = SplitPlanner.PlanMerge(new[] { a, b }, HeaderAlignment.ByName, false, "合并.xlsx", "合并结果");
            var headers = plan.Files[0].Sheets[0].Headers;

            Assert.Equal(2, headers.Count);
            Assert.Equal("新列", headers[1]);
        }

        [Fact]
        public void 按位置对齐_列名不同也对()
        {
            var a = Input("a.xlsx", "Sheet1", new[] { "单号", "金额" }, new[] { "T1", "100" });
            var b = Input("b.xlsx", "Sheet1", new[] { "编号", "金额" }, new[] { "T2", "200" });

            var plan = SplitPlanner.PlanMerge(new[] { a, b }, HeaderAlignment.ByPosition, false, "合并.xlsx", "合并结果");
            var sheet = plan.Files[0].Sheets[0];

            Assert.Equal("列1", sheet.Headers[0]);
            Assert.Equal("T2", sheet.Rows[1].Get(0));
            Assert.Equal("200", sheet.Rows[1].Get(1));
        }

        [Fact]
        public void 追加来源文件与来源sheet两列()
        {
            var a = Input("a.xlsx", "一月", new[] { "单号" }, new[] { "T1" });

            var plan = SplitPlanner.PlanMerge(new[] { a }, HeaderAlignment.ByName, true, "合并.xlsx", "合并结果");
            var sheet = plan.Files[0].Sheets[0];

            Assert.Equal("来源文件", sheet.Headers[1]);
            Assert.Equal("来源sheet", sheet.Headers[2]);
            Assert.Equal("a.xlsx", sheet.Rows[0].Get(1));
            Assert.Equal("一月", sheet.Rows[0].Get(2));
        }

        [Fact]
        public void 没有输入文件给中文提示()
        {
            var ex = Assert.Throws<FriendlyException>(() =>
                SplitPlanner.PlanMerge(new List<MergeInput>(), HeaderAlignment.ByName, false, "x.xlsx", "结果"));
            Assert.Contains("添加", ex.Message);
        }

        [Fact]
        public void 合并结果sheet名可指定()
        {
            var a = Input("a.xlsx", "Sheet1", new[] { "单号" }, new[] { "T1" });
            var plan = SplitPlanner.PlanMerge(new[] { a }, HeaderAlignment.ByName, false, "合并.xlsx", "合并结果");

            Assert.Equal("合并结果", plan.Files[0].Sheets[0].SheetName);
            Assert.Equal("合并.xlsx", plan.Files[0].FileName);
        }
    }

    public class RowSplitTests
    {
        private static SheetData Sheet(int rows)
        {
            var data = new SheetData { Name = "总表" };
            data.SetColumns(new[] { "单号" }, 1);
            for (int i = 1; i <= rows; i++) data.Rows.Add(new TableRow(new[] { "T" + i }, i + 1));
            return data;
        }

        [Fact]
        public void 正好整除()
        {
            var plan = SplitPlanner.PlanByRows(Sheet(300), 100, "总表", true);

            Assert.Equal(3, plan.Files.Count);
            Assert.All(plan.Files, f => Assert.Equal(100, f.RowCount));
        }

        [Fact]
        public void 余一行()
        {
            var plan = SplitPlanner.PlanByRows(Sheet(301), 100, "总表", true);

            Assert.Equal(4, plan.Files.Count);
            Assert.Equal(1, plan.Files[3].RowCount);
        }

        [Fact]
        public void 每份行数大于总数只出一份()
        {
            var plan = SplitPlanner.PlanByRows(Sheet(50), 500, "总表", true);

            Assert.Single(plan.Files);
            Assert.Equal(50, plan.Files[0].RowCount);
        }

        [Fact]
        public void 空表不生成文件()
        {
            var plan = SplitPlanner.PlanByRows(Sheet(0), 100, "总表", true);

            Assert.Empty(plan.Files);
            Assert.Contains(plan.Notes, n => n.Contains("没有数据行"));
        }

        [Fact]
        public void 每份行数非法给中文提示()
        {
            Assert.Throws<FriendlyException>(() => SplitPlanner.PlanByRows(Sheet(10), 0, "总表", true));
            Assert.Throws<FriendlyException>(() => SplitPlanner.PlanByRows(Sheet(10), -5, "总表", true));
        }

        [Fact]
        public void 超过Excel单表上限要提前拦住()
        {
            var ex = Assert.Throws<FriendlyException>(() =>
                SplitPlanner.PlanByRows(Sheet(10), 2_000_000, "总表", true));
            Assert.Contains("上限", ex.Message);
        }

        [Fact]
        public void 文件名带第几份()
        {
            var plan = SplitPlanner.PlanByRows(Sheet(250), 100, "总表", true);

            Assert.Equal("总表_第1份.xlsx", plan.Files[0].FileName);
            Assert.Equal("总表_第3份.xlsx", plan.Files[2].FileName);
        }

        [Fact]
        public void 每份都带表头()
        {
            var plan = SplitPlanner.PlanByRows(Sheet(250), 100, "总表", true);

            Assert.All(plan.Files, f => Assert.Single(f.Sheets[0].Headers));
        }
    }

    public class ExecutorTests
    {
        [Fact]
        public void 写出文件后能重新读回来()
        {
            string dir = Build.TempDir();
            var sheet = Build.Sheet("总表", new[] { "单号", "部门" },
                new[] { "T1", "财务部" },
                new[] { "T2", "财务部" });

            var plan = SplitPlanner.PlanByColumn(sheet, 1, "总表", true, false);
            var report = SplitExecutor.Execute(plan, dir);

            Assert.Single(report.Written);
            string path = Path.Combine(dir, report.Written[0]);
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp.xlsx"), "临时文件必须清掉");

            var back = TableReader.Load(path).First;
            Assert.Equal(2, back.Rows.Count);
            Assert.Equal("财务部", back.Rows[0].Get(1));
        }

        [Fact]
        public void 目标已存在时不覆盖而是加序号()
        {
            string dir = Build.TempDir();
            var sheet = Build.Sheet("总表", new[] { "部门" }, new[] { "财务部" });
            var plan = SplitPlanner.PlanByColumn(sheet, 0, "总表", true, false);

            var first = SplitExecutor.Execute(plan, dir);
            var second = SplitExecutor.Execute(plan, dir);

            Assert.Equal("总表_财务部.xlsx", first.Written[0]);
            Assert.Equal("总表_财务部 (1).xlsx", second.Written[0]);
        }

        [Fact]
        public void 没选输出目录给中文提示()
        {
            var plan = SplitPlanner.PlanByColumn(Build.Sheet("S", new[] { "部门" }, new[] { "财务部" }), 0, "总表", true, false);
            var ex = Assert.Throws<FriendlyException>(() => SplitExecutor.Execute(plan, ""));
            Assert.Contains("输出目录", ex.Message);
        }

        [Fact]
        public void 执行前不产生任何文件()
        {
            string dir = Build.TempDir();
            SplitPlanner.PlanByColumn(Build.Sheet("S", new[] { "部门" }, new[] { "财务部" }), 0, "总表", true, false);

            Assert.Empty(Directory.GetFiles(dir));
        }

        [Fact]
        public void 源文件写完后没被动过()
        {
            string dir = Build.TempDir();
            string source = Path.Combine(dir, "总表.xlsx");
            TestDataGenerator.Write(source);
            var before = new FileInfo(source);
            long size = before.Length;
            var stamp = before.LastWriteTimeUtc;

            var file = TableReader.Load(source);
            var plan = SplitPlanner.PlanByColumn(file.First, 1, "总表", true, false);
            string outDir = Path.Combine(dir, "out");
            SplitExecutor.Execute(plan, outDir);

            var after = new FileInfo(source);
            Assert.Equal(size, after.Length);
            Assert.Equal(stamp, after.LastWriteTimeUtc);
            Assert.Equal(13, plan.Files.Count);   // 12 个部门 + 1 个空白
        }
    }
}
