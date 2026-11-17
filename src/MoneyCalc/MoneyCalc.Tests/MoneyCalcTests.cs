using System;
using MoneyCalc.Core;

namespace MoneyCalc.Tests
{
    public class TaxTests
    {
        [Fact]
        public void 含税算不含税_13个点()
        {
            var r = TaxCalculator.FromGross(1130m, 13m);

            Assert.Equal(1000.00m, r.Net);
            Assert.Equal(130.00m, r.Tax);
            Assert.Equal(1130.00m, r.Gross);
        }

        [Fact]
        public void 不含税算含税_13个点()
        {
            var r = TaxCalculator.FromNet(1000m, 13m);

            Assert.Equal(1130.00m, r.Gross);
            Assert.Equal(130.00m, r.Tax);
            Assert.Equal(1000.00m, r.Net);
        }

        [Fact]
        public void 税额必须自洽_不含税加税额等于含税()
        {
            decimal[] amounts = { 0.01m, 1m, 99.99m, 100m, 1130m, 9999999.99m, 3333.33m, 7.77m };
            decimal[] rates = { 0m, 1m, 3m, 5m, 6m, 9m, 13m, 6.5m };

            foreach (var amount in amounts)
            {
                foreach (var rate in rates)
                {
                    var fromGross = TaxCalculator.FromGross(amount, rate);
                    Assert.True(fromGross.IsConsistent, amount + " 含→不含 " + rate + "% 不自洽");
                    Assert.Equal(fromGross.Gross, fromGross.Net + fromGross.Tax);

                    var fromNet = TaxCalculator.FromNet(amount, rate);
                    Assert.True(fromNet.IsConsistent, amount + " 不含→含 " + rate + "% 不自洽");
                    Assert.Equal(fromNet.Gross, fromNet.Net + fromNet.Tax);
                }
            }
        }

        [Fact]
        public void 零税率时税额为零()
        {
            var r = TaxCalculator.FromGross(500m, 0m);

            Assert.Equal(500m, r.Net);
            Assert.Equal(0m, r.Tax);
            Assert.Equal(500m, r.Gross);
        }

        [Fact]
        public void 自定义六点五个点()
        {
            var r = TaxCalculator.FromNet(100m, 6.5m);

            Assert.Equal(6.50m, r.Tax);
            Assert.Equal(106.50m, r.Gross);
        }

        [Fact]
        public void 大金额也准()
        {
            var r = TaxCalculator.FromGross(9999999.99m, 13m);

            Assert.Equal(8849557.51m, r.Net);
            Assert.Equal(1150442.48m, r.Tax);
            Assert.Equal(9999999.99m, r.Net + r.Tax);
        }

        [Fact]
        public void 负数金额按方向一致处理()
        {
            var r = TaxCalculator.FromGross(-1130m, 13m);

            Assert.Equal(-1000m, r.Net);
            Assert.Equal(-130m, r.Tax);
            Assert.Equal(-1130m, r.Net + r.Tax);
        }

        [Fact]
        public void 舍入临界_0点005()
        {
            // 100 * 5% = 5.00；61.5 * 13% = 7.995 → 8.00（四舍五入远离零）
            var r = TaxCalculator.FromNet(61.5m, 13m);

            Assert.Equal(8.00m, r.Tax);
            Assert.Equal(69.50m, r.Gross);
        }

        [Fact]
        public void 四舍五入到分不会差一分()
        {
            // 含税 100，13% → 不含税 88.495575... → 88.50，税 11.50
            var r = TaxCalculator.FromGross(100m, 13m);

            Assert.Equal(88.50m, r.Net);
            Assert.Equal(11.50m, r.Tax);
        }

        [Fact]
        public void 高精度值保留四位()
        {
            var r = TaxCalculator.FromGross(100m, 13m);

            Assert.Equal(88.4956m, r.NetExact);
        }

        [Fact]
        public void 非法税率报中文错误()
        {
            Assert.Throws<FriendlyException>(() => TaxCalculator.FromGross(100m, -1m));
            Assert.Throws<FriendlyException>(() => TaxCalculator.FromNet(100m, 100m));
        }

        [Fact]
        public void 常用税率列表()
        {
            Assert.Equal(new[] { 13m, 9m, 6m, 5m, 3m, 1m }, TaxCalculator.CommonRates);
        }
    }

    public class DateTests
    {
        [Fact]
        public void 一月三十一加一个月落到二月末()
        {
            var result = DateMath.AddMonths(new DateTime(2026, 1, 31), 1);
            Assert.Equal(new DateTime(2026, 2, 28), result);
        }

        [Fact]
        public void 闰年二月二十九加一年落到二月末()
        {
            var result = DateMath.AddYears(new DateTime(2028, 2, 29), 1);
            Assert.Equal(new DateTime(2029, 2, 28), result);
        }

        [Fact]
        public void 闰年加一年仍是二月二十八()
        {
            Assert.Equal(new DateTime(2025, 2, 28), DateMath.AddYears(new DateTime(2024, 2, 29), 1));
        }

        [Fact]
        public void 十二月三十一加一天跨年()
        {
            Assert.Equal(new DateTime(2027, 1, 1), DateMath.AddDays(new DateTime(2026, 12, 31), 1));
        }

        [Fact]
        public void 十二月加一个月跨年()
        {
            Assert.Equal(new DateTime(2027, 1, 31), DateMath.AddMonths(new DateTime(2026, 12, 31), 1));
        }

        [Fact]
        public void 整月差_一月底到二月底算一个月()
        {
            Assert.Equal(1, DateMath.WholeMonthsBetween(new DateTime(2026, 1, 31), new DateTime(2026, 2, 28)));
        }

        [Fact]
        public void 整月差_不到一个月算零()
        {
            Assert.Equal(0, DateMath.WholeMonthsBetween(new DateTime(2026, 1, 31), new DateTime(2026, 2, 27)));
        }

        [Fact]
        public void 整月差_跨年()
        {
            Assert.Equal(13, DateMath.WholeMonthsBetween(new DateTime(2025, 11, 14), new DateTime(2026, 12, 14)));
        }

        [Fact]
        public void 整年差()
        {
            Assert.Equal(2, DateMath.WholeYearsBetween(new DateTime(2024, 3, 1), new DateTime(2026, 3, 1)));
            Assert.Equal(1, DateMath.WholeYearsBetween(new DateTime(2024, 3, 1), new DateTime(2026, 2, 28)));
        }

        [Fact]
        public void 整年差_闰日到二月末算一年()
        {
            Assert.Equal(1, DateMath.WholeYearsBetween(new DateTime(2024, 2, 29), new DateTime(2025, 2, 28)));
        }

        [Fact]
        public void 自然日差()
        {
            Assert.Equal(31, DateMath.DaysBetween(new DateTime(2026, 1, 1), new DateTime(2026, 2, 1)));
            Assert.Equal(0, DateMath.DaysBetween(new DateTime(2026, 1, 1), new DateTime(2026, 1, 1)));
        }

        [Fact]
        public void 年月日描述()
        {
            string text = DateMath.DescribeSpan(new DateTime(2024, 1, 31), new DateTime(2026, 3, 5));
            Assert.Contains("2 年", text);
            Assert.Contains("个月", text);
            Assert.Contains("天", text);
        }

        [Fact]
        public void 月末判断()
        {
            Assert.True(DateMath.IsLastDayOfMonth(new DateTime(2026, 2, 28)));
            Assert.False(DateMath.IsLastDayOfMonth(new DateTime(2028, 2, 28)));
            Assert.True(DateMath.IsLastDayOfMonth(new DateTime(2028, 2, 29)));
        }
    }

    public class RateTests
    {
        [Fact]
        public void 正反算互逆()
        {
            // 汇率 2 的倒数是 0.5，来回换不丢精度
            decimal direct = RateCalculator.Convert(1000m, 2m);
            decimal back = RateCalculator.Convert(direct, RateCalculator.Inverse(2m));

            Assert.Equal(2000m, direct);
            Assert.Equal(1000m, back);
        }

        [Fact]
        public void 倒数只保留四位会有一点点误差()
        {
            // 真实汇率 7.1234 的倒数 0.140382… 存成 0.1404，来回换差 0.12 元，属正常
            decimal direct = RateCalculator.Convert(1000m, 7.1234m);
            decimal back = RateCalculator.Convert(direct, RateCalculator.Inverse(7.1234m));

            Assert.Equal(7123.4m, direct);
            Assert.InRange(back, 999m, 1001m);
        }

        [Fact]
        public void 结果保留四位小数()
        {
            Assert.Equal(712.3456m, RateCalculator.Convert(100m, 7.123456m));
        }

        [Fact]
        public void 零汇率报中文错误()
        {
            var ex = Assert.Throws<FriendlyException>(() => RateCalculator.Convert(100m, 0m));
            Assert.Contains("汇率", ex.Message);
        }

        [Fact]
        public void 常用汇率去重且最新在前()
        {
            var list = new System.Collections.Generic.List<RateEntry>();
            list = RateCalculator.Add(list, new RateEntry { From = "USD", To = "CNY", Rate = 7.1m, SavedAt = new DateTime(2026, 11, 1) });
            list = RateCalculator.Add(list, new RateEntry { From = "EUR", To = "CNY", Rate = 7.8m, SavedAt = new DateTime(2026, 11, 2) });
            list = RateCalculator.Add(list, new RateEntry { From = "USD", To = "CNY", Rate = 7.2m, SavedAt = new DateTime(2026, 11, 3) });

            Assert.Equal(2, list.Count);
            Assert.Equal("USD", list[0].From);
            Assert.Equal(7.2m, list[0].Rate);
        }

        [Fact]
        public void 反方向也算同一对()
        {
            var list = new System.Collections.Generic.List<RateEntry>();
            list = RateCalculator.Add(list, new RateEntry { From = "USD", To = "CNY", Rate = 7.1m });
            list = RateCalculator.Add(list, new RateEntry { From = "CNY", To = "USD", Rate = 0.14m });

            Assert.Single(list);
        }

        [Fact]
        public void 最多存20条()
        {
            var list = new System.Collections.Generic.List<RateEntry>();
            for (int i = 0; i < 25; i++)
                list = RateCalculator.Add(list, new RateEntry { From = "C" + i, To = "CNY", Rate = i + 1 });

            Assert.Equal(20, list.Count);
            Assert.Equal("C24", list[0].From);
        }

        [Fact]
        public void 删除单条()
        {
            var list = new System.Collections.Generic.List<RateEntry>();
            var a = new RateEntry { From = "USD", To = "CNY", Rate = 7.1m };
            list = RateCalculator.Add(list, a);
            list = RateCalculator.Add(list, new RateEntry { From = "EUR", To = "CNY", Rate = 7.8m });

            list = RateCalculator.Remove(list, a);

            Assert.Single(list);
            Assert.Equal("EUR", list[0].From);
        }
    }
}
