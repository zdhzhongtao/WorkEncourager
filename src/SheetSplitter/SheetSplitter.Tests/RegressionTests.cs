using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using SheetSplitter.Core;

namespace SheetSplitter.Tests
{
    /// <summary>自检发现的边界问题。</summary>
    public class RegressionTests
    {
        [Fact]
        public void 以等号开头的文本按文本写_不能变成公式()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ss-eq-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "eq.xlsx");
            try
            {
                var sheet = new OutputSheet { SheetName = "S", Headers = { "备注" } };
                sheet.Rows.Add(new TableRow(new[] { "=1+1" }, 2));
                sheet.Rows.Add(new TableRow(new[] { "=HYPERLINK(\"http://x\",\"点我\")" }, 3));
                ExcelWriter.Write(path, new List<OutputSheet> { sheet });

                using (var wb = new XLWorkbook(path))
                {
                    var ws = wb.Worksheet(1);
                    Assert.False(ws.Cell(2, 1).HasFormula, "=1+1 变成了公式");
                    Assert.Equal("=1+1", ws.Cell(2, 1).GetText());
                    Assert.False(ws.Cell(3, 1).HasFormula, "超链接被当成公式了");
                }
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Theory]
        [InlineData("CON")]
        [InlineData("nul")]
        [InlineData("PRN")]
        [InlineData("COM1")]
        [InlineData("LPT9")]
        public void Windows保留设备名要避开_否则文件根本写不出来(string value)
        {
            string cleaned = FileNameSanitizer.Clean(value);

            Assert.False(IsReserved(cleaned), "「" + cleaned + "」仍然是设备名");

            string fileName = FileNameSanitizer.BuildFileName("表", value);
            Assert.False(IsReserved(Path.GetFileNameWithoutExtension(fileName)),
                "「" + fileName + "」会被当成设备");
        }

        private static bool IsReserved(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string bare = name.TrimEnd(' ').ToUpperInvariant();
            int dot = bare.IndexOf('.');
            if (dot >= 0) bare = bare.Substring(0, dot);
            if (bare == "CON" || bare == "PRN" || bare == "AUX" || bare == "NUL") return true;
            if (bare.Length == 4 && (bare.StartsWith("COM") || bare.StartsWith("LPT"))
                && bare[3] >= '0' && bare[3] <= '9') return true;
            return false;
        }

        [Fact]
        public void 保留名加了后缀能真的落盘()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ss-reg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string name = FileNameSanitizer.BuildFileName("表", "CON");
                string path = Path.Combine(dir, name);

                ExcelWriter.Write(path, new List<OutputSheet>
                {
                    new OutputSheet { SheetName = "S", Headers = { "A" }, Rows = { } }
                });

                Assert.True(File.Exists(path), "文件没写出来：" + path);
                Assert.True(new FileInfo(path).Length > 0);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void 零点五要写成数字而不是文本()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ss-num-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "n.xlsx");
            try
            {
                var sheet = new OutputSheet { SheetName = "S", Headers = { "值" } };
                sheet.Rows.Add(new TableRow(new[] { "0.5" }, 2));
                sheet.Rows.Add(new TableRow(new[] { "0.05" }, 3));
                sheet.Rows.Add(new TableRow(new[] { "007" }, 4));
                ExcelWriter.Write(path, new List<OutputSheet> { sheet });

                using (var wb = new XLWorkbook(path))
                {
                    var ws = wb.Worksheet(1);
                    Assert.Equal(XLDataType.Number, ws.Cell(2, 1).DataType);
                    Assert.Equal(0.5, ws.Cell(2, 1).GetDouble(), 6);
                    Assert.Equal(XLDataType.Number, ws.Cell(3, 1).DataType);
                    Assert.Equal(0.05, ws.Cell(3, 1).GetDouble(), 6);
                    Assert.Equal(XLDataType.Text, ws.Cell(4, 1).DataType);   // 007 是编号
                    Assert.Equal("007", ws.Cell(4, 1).GetText());
                }
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void 超过15位的数字按文本写_不丢精度()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ss-big-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "big.xlsx");
            try
            {
                const string id = "123456789012345678";
                var sheet = new OutputSheet { SheetName = "S", Headers = { "订单号" } };
                sheet.Rows.Add(new TableRow(new[] { id }, 2));
                ExcelWriter.Write(path, new List<OutputSheet> { sheet });

                using (var wb = new XLWorkbook(path))
                {
                    var cell = wb.Worksheet(1).Cell(2, 1);
                    Assert.Equal(XLDataType.Text, cell.DataType);
                    Assert.Equal(id, cell.GetText());
                }
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
