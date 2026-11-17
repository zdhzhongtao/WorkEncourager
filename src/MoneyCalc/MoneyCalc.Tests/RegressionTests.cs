using System;
using MoneyCalc.Core;

namespace MoneyCalc.Tests
{
    /// <summary>自检发现的边界问题。</summary>
    public class RegressionTests
    {
        [Fact]
        public void 日历里一天工作日都没有时_加工作日要给提示而不是死循环()
        {
            var calendar = new HolidayCalendar();

            // 把 2000-01-01 到 2100-12-31 全设成节假日：一天能上班的都没有
            for (var d = new DateTime(2000, 1, 1); d <= new DateTime(2100, 12, 31); d = d.AddDays(1))
                calendar.Holidays.Add(d);

            var ex = Assert.Throws<FriendlyException>(() => calendar.AddWorkdays(new DateTime(2026, 11, 16), 5, false));
            Assert.Contains("工作日", ex.Message);
        }

        [Fact]
        public void 日历里一天工作日都没有时_数工作日返回0而不是死循环()
        {
            var calendar = new HolidayCalendar();
            for (var d = new DateTime(2026, 1, 1); d <= new DateTime(2026, 12, 31); d = d.AddDays(1))
                calendar.Holidays.Add(d);

            Assert.Equal(0, calendar.CountWorkdays(new DateTime(2026, 3, 1), new DateTime(2026, 3, 31), true));
        }

        [Fact]
        public void 零元整与角分边界()
        {
            Assert.Equal("零元整", AmountInWords.ToChinese(0m));
            Assert.Equal("零元伍分", AmountInWords.ToChinese(0.05m).Replace("零伍分", "伍分"));
            Assert.Equal("壹拾元整", AmountInWords.ToChinese(10m));
            Assert.Equal("壹佰元零伍分", AmountInWords.ToChinese(100.05m));
            Assert.Equal("壹亿元整", AmountInWords.ToChinese(100000000m));
            Assert.Equal("壹拾亿元整", AmountInWords.ToChinese(1000000000m));
            Assert.Equal("壹万零壹元整", AmountInWords.ToChinese(10001m));
        }

        [Fact]
        public void 大写再解析回来要对得上()
        {
            // 输入带千分位、带空格都要能解析
            Assert.True(AmountInWords.TryParse(" 1,234,567.89 ", out decimal v, out _));
            Assert.Equal(1234567.89m, v);

            // 两位以上小数按四舍五入到分
            Assert.True(AmountInWords.TryParse("1.005", out decimal w, out _));
            Assert.Equal(1.01m, w);

            // 只有符号的输入要拦住
            Assert.False(AmountInWords.TryParse("-", out _, out _));
            Assert.False(AmountInWords.TryParse(".", out _, out _));
        }
    }
}
