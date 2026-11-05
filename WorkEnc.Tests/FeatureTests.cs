using System;
using System.Collections.Generic;
using System.Linq;
using WorkEnc.Core;
using Xunit;

namespace WorkEnc.Tests
{
    /// <summary>外部工时文件导入的解析测试。</summary>
    public class WorkLogImporterTests
    {
        private static List<ImportedDay> Parse(string text, out List<string> errors)
        {
            errors = new List<string>();
            return WorkLogImporter.Parse(text, errors);
        }

        [Fact]
        public void Parse_WithHeader_ReadsAllColumns()
        {
            string csv =
                "日期,首次上班,最后下班,实际工时(小时),加班(小时),备注\r\n" +
                "2026-11-05,09:02,18:30,8.50,0.50,正常\r\n" +
                "2026-11-06,09:10,18:00,8.00,0,\r\n";

            var rows = Parse(csv, out var errors);

            Assert.Empty(errors);
            Assert.Equal(2, rows.Count);
            Assert.Equal("2026-11-05", rows[0].Date);
            Assert.Equal("09:02", rows[0].FirstClockIn);
            Assert.Equal("18:30", rows[0].LastClockOut);
            Assert.Equal(8.5, rows[0].WorkedHours);
            Assert.Equal(0.5, rows[0].OvertimeHours);
            Assert.Equal("正常", rows[0].Note);
        }

        [Fact]
        public void Parse_WithoutHeader_UsesFixedOrder()
        {
            var rows = Parse("2026-11-05,09:00,18:00,9,0,\r\n", out _);

            Assert.Single(rows);
            Assert.Equal("2026-11-05", rows[0].Date);
            Assert.Equal(9, rows[0].WorkedHours);
        }

        [Fact]
        public void Parse_OnlyDateAndHours_Works()
        {
            // 最精简的输入：只有日期和工时
            var rows = Parse("2026-11-05,8.5\r\n2026-11-06,7\r\n", out var errors);

            Assert.Empty(errors);
            Assert.Equal(2, rows.Count);
            Assert.Equal(8.5, rows[0].WorkedHours);
            Assert.Equal(7, rows[1].WorkedHours);
        }

        [Fact]
        public void Parse_DerivesHoursFromClocks_WhenHoursMissing()
        {
            // 表头里没有工时刻，靠上下班时间推算
            string csv = "日期,上班,下班\r\n2026-11-05,09:00,17:30\r\n";
            var rows = Parse(csv, out _);

            Assert.Single(rows);
            Assert.Equal(8.5, rows[0].WorkedHours.Value, 2);
        }

        [Fact]
        public void Parse_NightShift_CrossMidnight()
        {
            // 22:00 → 06:00 应该是 8 小时，不是负数
            string csv = "日期,上班,下班\r\n2026-11-05,22:00,06:00\r\n";
            var rows = Parse(csv, out _);

            Assert.Single(rows);
            Assert.Equal(8.0, rows[0].WorkedHours.Value, 2);
        }

        [Fact]
        public void Parse_BadDate_ReportsErrorAndSkipsRow()
        {
            string csv = "日期,工时\r\n不是日期,8\r\n2026-11-05,8\r\n";
            var rows = Parse(csv, out var errors);

            Assert.Single(rows);
            Assert.NotEmpty(errors);
        }

        [Fact]
        public void Parse_TabSeparated_AlsoWorks()
        {
            // 从某些系统复制出来的就是制表符分隔
            var rows = Parse("2026-11-05\t9:00\t18:00\r\n", out _);

            Assert.Single(rows);
            Assert.Equal(9, rows[0].WorkedHours);
        }

        [Fact]
        public void Parse_HoursInClockFormat()
        {
            // "8:30" 这种写法当 8.5 小时
            Assert.Equal(8.5, WorkLogImporter.ParseHours("8:30"));
            Assert.Equal(8.5, WorkLogImporter.ParseHours("8.5"));
            Assert.Equal(8.0, WorkLogImporter.ParseHours("8h"));
            Assert.Null(WorkLogImporter.ParseHours(""));
        }

        [Fact]
        public void TryParseDate_AcceptsCommonFormats()
        {
            Assert.True(WorkLogImporter.TryParseDate("2026-11-05", out var d1));
            Assert.Equal(new DateTime(2026, 11, 5), d1);

            Assert.True(WorkLogImporter.TryParseDate("2026/11/5", out var d2));
            Assert.Equal(new DateTime(2026, 11, 5), d2);

            Assert.True(WorkLogImporter.TryParseDate("20261105", out var d3));
            Assert.Equal(new DateTime(2026, 11, 5), d3);

            Assert.False(WorkLogImporter.TryParseDate("随便写的", out _));
        }

        [Fact]
        public void Parse_QuotedNoteWithComma_NotSplit()
        {
            string csv = "日期,工时,备注\r\n2026-11-05,8,\"迟到,但补上了\"\r\n";
            var rows = Parse(csv, out _);

            Assert.Single(rows);
            Assert.Equal("迟到,但补上了", rows[0].Note);
        }
    }

    /// <summary>语录 CSV 读写的往返测试。</summary>
    public class QuoteStoreTests
    {
        public QuoteStoreTests()
        {
            QuoteStore.SetCustom(null);   // 静态类，测试之间要清干净
        }

        [Fact]
        public void BuildCsv_ThenParse_RoundTrips()
        {
            string csv = QuoteStore.BuildCsv();
            QuoteStore.SetCustom(csv);

            // 导出的就是内置语录，装回去应该条数一致、内容一致
            Assert.True(QuoteStore.CustomCount > 0);
            var pool = QuoteStore.PoolFor(Tone.Chill, QuoteScene.FirstItem);
            Assert.Contains("再过 {TIME}，一件{ITEM}就赚回来了", pool);
        }

        [Fact]
        public void Custom_Overrides_BuiltIn()
        {
            QuoteStore.SetCustom("佛系,首件,我的自定义语录\r\n");

            var pool = QuoteStore.PoolFor(Tone.Chill, QuoteScene.FirstItem);
            Assert.Single(pool);
            Assert.Equal("我的自定义语录", pool[0]);

            // 没写过的组继续用内置
            var others = QuoteStore.PoolFor(Tone.Savage, QuoteScene.NotEnough);
            Assert.True(others.Length > 0);
            Assert.DoesNotContain("我的自定义语录", others);
        }

        [Fact]
        public void EnglishToneNames_AlsoAccepted()
        {
            QuoteStore.SetCustom("hype,repeatItem,英文语气名也能用\r\n");

            var pool = QuoteStore.PoolFor(Tone.Hype, QuoteScene.RepeatItem);
            Assert.Equal("英文语气名也能用", pool[0]);
        }

        [Fact]
        public void UnknownTone_IsSkipped()
        {
            // 表头那种行会被跳过，不该污染结果
            QuoteStore.SetCustom("语气,情境,语录\r\n瞎写,首件,不该进来\r\n");

            Assert.Equal(0, QuoteStore.CustomCount);
            Assert.True(QuoteStore.PoolFor(Tone.Chill, QuoteScene.FirstItem).Length > 1);
        }

        [Fact]
        public void NullOrEmpty_FallsBackToBuiltIn()
        {
            QuoteStore.SetCustom(null);
            Assert.Equal(0, QuoteStore.CustomCount);
            Assert.True(QuoteStore.PoolFor(Tone.Hype, QuoteScene.FirstItem).Length > 0);
        }

        [Fact]
        public void CsvWithCommaInQuote_IsEscaped()
        {
            QuoteStore.SetCustom("毒舌,买不够,\"今天只够买 1 件, 明天继续\"\r\n");

            var pool = QuoteStore.PoolFor(Tone.Savage, QuoteScene.NotEnough);
            Assert.Single(pool);
            Assert.Equal("今天只够买 1 件, 明天继续", pool[0]);
        }
    }
}
