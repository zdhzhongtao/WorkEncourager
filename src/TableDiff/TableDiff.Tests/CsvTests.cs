using System;
using System.Text;
using TableDiff.Core;

namespace TableDiff.Tests
{
    public class CsvTests
    {
        static CsvTests()
        {
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
        }

        [Fact]
        public void 引号内的逗号不算分隔符()
        {
            var rows = CsvReader.Parse("单号,客户\r\nP1,\"上海, 鼎新\"\r\n", ',');

            Assert.Equal(2, rows.Count);
            Assert.Equal("上海, 鼎新", rows[1][1]);
        }

        [Fact]
        public void 引号内的换行不算换行()
        {
            var rows = CsvReader.Parse("a,b\r\n1,\"第一行\r\n第二行\"\r\n", ',');

            Assert.Equal(2, rows.Count);
            Assert.Equal("第一行\r\n第二行", rows[1][1]);
        }

        [Fact]
        public void 两个引号转义成一个()
        {
            var rows = CsvReader.Parse("a,\"他说\"\"你好\"\"\"\r\n", ',');

            Assert.Single(rows);
            Assert.Equal("他说\"你好\"", rows[0][1]);
        }

        [Fact]
        public void 自动识别分号分隔()
        {
            string text = "单号;客户;金额\r\nP1;甲;100\r\nP2;乙;200\r\n";
            Assert.Equal(';', CsvReader.DetectDelimiter(text, "x.csv"));
        }

        [Fact]
        public void 自动识别逗号分隔()
        {
            string text = "单号,客户,金额\r\nP1,甲,100\r\n";
            Assert.Equal(',', CsvReader.DetectDelimiter(text, "x.csv"));
        }

        [Fact]
        public void tsv扩展名优先按制表符()
        {
            Assert.Equal('\t', CsvReader.DetectDelimiter("a\tb\tc\r\n", "x.tsv"));
        }

        [Fact]
        public void 无BOM的UTF8能正确解码()
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes("单号,客户\r\nP1,甲公司\r\n");
            string text = CsvReader.DecodeText(bytes, out string enc);

            Assert.Equal("utf-8", enc);
            Assert.Contains("甲公司", text);
        }

        [Fact]
        public void 带BOM的UTF8能正确解码且BOM不进数据()
        {
            byte[] body = new UTF8Encoding(false).GetBytes("单号,客户\r\nP1,甲\r\n");
            byte[] bytes = new byte[body.Length + 3];
            bytes[0] = 0xEF; bytes[1] = 0xBB; bytes[2] = 0xBF;
            Array.Copy(body, 0, bytes, 3, body.Length);

            string text = CsvReader.DecodeText(bytes, out string enc);

            Assert.Equal("utf-8-bom", enc);
            Assert.Equal('单', text[0]);
        }

        [Fact]
        public void GB18030编码能自动兜底()
        {
            byte[] bytes = Encoding.GetEncoding("GB18030").GetBytes("单号,客户\r\nP1,上海鼎新贸易\r\n");
            string text = CsvReader.DecodeText(bytes, out string enc);

            Assert.Equal("gb18030", enc);
            Assert.Contains("上海鼎新贸易", text);
            Assert.Equal('单', text[0]);
        }

        [Fact]
        public void 中文表头读成表()
        {
            var sheet = SheetData.FromRows("S", CsvReader.Parse("单号,客户,金额\r\nP1,甲,100\r\n", ','), true);

            Assert.Equal(3, sheet.ColumnCount);
            Assert.Equal("单号", sheet.Columns[0]);
            Assert.Equal("客户", sheet.Columns[1]);
            Assert.Single(sheet.Rows);
            Assert.Equal("100", sheet.Rows[0].Get(2));
        }

        [Fact]
        public void 文件末尾空行被丢掉()
        {
            var rows = CsvReader.Parse("a,b\r\n1,2\r\n\r\n", ',');
            Assert.Equal(2, rows.Count);
        }

        [Fact]
        public void 短行自动补空单元格()
        {
            var sheet = SheetData.FromRows("S", CsvReader.Parse("a,b,c\r\n1,2\r\n", ','), true);

            Assert.Equal(string.Empty, sheet.Rows[0].Get(2));
        }
    }
}
