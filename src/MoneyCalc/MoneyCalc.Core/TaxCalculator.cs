using System;

namespace MoneyCalc.Core
{
    /// <summary>一次含税/不含税换算的结果。分位与 4 位小数两套都给。</summary>
    public sealed class TaxResult
    {
        /// <summary>不含税（2 位小数）。</summary>
        public decimal Net { get; set; }
        /// <summary>税额（2 位小数）。</summary>
        public decimal Tax { get; set; }
        /// <summary>含税（2 位小数）。</summary>
        public decimal Gross { get; set; }

        /// <summary>高精度（4 位小数）的不含税。</summary>
        public decimal NetExact { get; set; }
        public decimal TaxExact { get; set; }
        public decimal GrossExact { get; set; }

        /// <summary>自洽性检查：不含税 + 税额 必须正好等于含税。</summary>
        public bool IsConsistent => Net + Tax == Gross && NetExact + TaxExact == GrossExact;
    }

    /// <summary>
    /// 含税 / 不含税换算。
    ///
    /// 关键规则：**先算基准值，另一个用减法得出**，否则各自四舍五入会差一分钱。
    /// </summary>
    public static class TaxCalculator
    {
        /// <summary>由含税价反推（最常见的场景：发票金额是含税的）。</summary>
        public static TaxResult FromGross(decimal gross, decimal ratePercent)
        {
            ValidateRate(ratePercent);

            decimal grossExact = gross;
            decimal netExact = grossExact / (1m + ratePercent / 100m);

            decimal gross2 = Round2(grossExact);
            decimal net2 = Round2(netExact);
            decimal tax2 = gross2 - net2;              // 用减法，保证 net + tax == gross

            return new TaxResult
            {
                Gross = gross2,
                Net = net2,
                Tax = tax2,
                GrossExact = Round4(grossExact),
                NetExact = Round4(netExact),
                TaxExact = Round4(grossExact) - Round4(netExact)
            };
        }

        /// <summary>由不含税价正推（报价场景）。</summary>
        public static TaxResult FromNet(decimal net, decimal ratePercent)
        {
            ValidateRate(ratePercent);

            decimal netExact = net;
            decimal taxExact = netExact * ratePercent / 100m;

            decimal net2 = Round2(netExact);
            decimal tax2 = Round2(taxExact);
            decimal gross2 = net2 + tax2;              // 用加法，保证 net + tax == gross

            return new TaxResult
            {
                Net = net2,
                Tax = tax2,
                Gross = gross2,
                NetExact = Round4(netExact),
                TaxExact = Round4(taxExact),
                GrossExact = Round4(netExact) + Round4(taxExact)
            };
        }

        private static void ValidateRate(decimal ratePercent)
        {
            if (ratePercent < 0) throw new FriendlyException("税率不能是负数。");
            if (ratePercent >= 100) throw new FriendlyException("税率要小于 100%。");
        }

        public static decimal Round2(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        public static decimal Round4(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);

        /// <summary>常用税率，界面上做成快捷按钮。</summary>
        public static readonly decimal[] CommonRates = { 13m, 9m, 6m, 5m, 3m, 1m };
    }
}
