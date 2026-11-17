using System;
using MoneyCalc.Core;

namespace MoneyCalc.Tests
{
    public class AmountInWordsTests
    {
        [Theory]
        [InlineData("0", "零元整")]
        [InlineData("0.01", "零元零壹分")]
        [InlineData("0.1", "零元壹角")]
        [InlineData("0.05", "零元零伍分")]
        [InlineData("0.5", "零元伍角")]
        [InlineData("1", "壹元整")]
        [InlineData("1.00", "壹元整")]
        [InlineData("1.05", "壹元零伍分")]
        [InlineData("10.1", "壹拾元壹角")]
        [InlineData("12.34", "壹拾贰元叁角肆分")]
        [InlineData("20.00", "贰拾元整")]
        [InlineData("100.01", "壹佰元零壹分")]
        [InlineData("1001", "壹仟零壹元整")]
        [InlineData("1010", "壹仟零壹拾元整")]
        [InlineData("1100", "壹仟壹佰元整")]
        [InlineData("10000", "壹万元整")]
        [InlineData("100000", "壹拾万元整")]
        [InlineData("1000000", "壹佰万元整")]
        [InlineData("1000000.5", "壹佰万元伍角")]
        [InlineData("100000000", "壹亿元整")]
        [InlineData("100000001", "壹亿零壹元整")]
        [InlineData("100010000", "壹亿零壹万元整")]
        [InlineData("1234567.89", "壹佰贰拾叁万肆仟伍佰陆拾柒元捌角玖分")]
        [InlineData("999999999999.99", "玖仟玖佰玖拾玖亿玖仟玖佰玖拾玖万玖仟玖佰玖拾玖元玖角玖分")]
        [InlineData("-1", "负壹元整")]
        [InlineData("-1234.56", "负壹仟贰佰叁拾肆元伍角陆分")]
        public void 金额转大写(string input, string expected)
        {
            decimal value = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(expected, AmountInWords.ToChinese(value));
        }

        [Fact]
        public void 超过上限拒绝并给中文提示()
        {
            Assert.False(AmountInWords.TryToChinese(1000000000000m, out _, out string error));
            Assert.Contains("过大", error);
        }

        [Fact]
        public void 负数超限也拒绝()
        {
            Assert.False(AmountInWords.TryToChinese(-1000000000000m, out _, out _));
        }

        [Theory]
        [InlineData("1,234.56", 1234.56)]
        [InlineData(" 88 ", 88)]
        [InlineData("0.005", 0.01)]      // 四舍五入到分
        [InlineData("-12.3", -12.3)]
        public void 解析带千分位与空格的输入(string input, double expected)
        {
            Assert.True(AmountInWords.TryParse(input, out decimal value, out _));
            Assert.Equal((decimal)expected, value);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("-")]
        [InlineData("abc")]
        [InlineData("1.2.3")]
        public void 非法输入解析失败(string input)
        {
            Assert.False(AmountInWords.TryParse(input, out _, out string error));
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void 连续零只读一个零()
        {
            // 100005 → 壹拾万零伍
            Assert.Equal("壹拾万零伍元整", AmountInWords.ToChinese(100005m));
            // 1000050 → 壹佰万零伍拾
            Assert.Equal("壹佰万零伍拾元整", AmountInWords.ToChinese(1000050m));
        }
    }
}
