using System;
using System.Collections.Generic;
using System.Globalization;

namespace MoneyCalc.Core
{
    /// <summary>一条常用汇率。</summary>
    public sealed class RateEntry
    {
        public string From { get; set; } = "USD";
        public string To { get; set; } = "CNY";
        public decimal Rate { get; set; }
        public DateTime SavedAt { get; set; } = DateTime.Now;

        public string Display =>
            From + " → " + To + "  " + Rate.ToString("0.####", CultureInfo.InvariantCulture)
            + "   " + SavedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 汇率换算。**不联网、不抓行情**，汇率全靠手输。
    /// </summary>
    public static class RateCalculator
    {
        public const int MaxSaved = 20;

        public static readonly string[] CommonCurrencies = { "CNY", "USD", "EUR", "HKD", "JPY", "GBP" };

        /// <summary>按汇率换算，4 位小数。</summary>
        public static decimal Convert(decimal amount, decimal rate)
        {
            if (rate <= 0) throw new FriendlyException("汇率要大于 0。");
            return decimal.Round(amount * rate, 4, MidpointRounding.AwayFromZero);
        }

        /// <summary>反向汇率（1 / rate），4 位小数。</summary>
        public static decimal Inverse(decimal rate)
        {
            if (rate <= 0) throw new FriendlyException("汇率要大于 0。");
            return decimal.Round(1m / rate, 4, MidpointRounding.AwayFromZero);
        }

        public static bool TryParseDecimal(string text, out decimal value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string clean = text.Trim().Replace(",", string.Empty);
            return decimal.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// 存一条常用汇率：同一币种对只留最新一条（提到最前），最多 20 条。
        /// </summary>
        public static List<RateEntry> Add(List<RateEntry> list, RateEntry entry)
        {
            var result = new List<RateEntry>();
            if (list != null)
            {
                foreach (var item in list)
                {
                    if (SamePair(item, entry)) continue;
                    result.Add(item);
                }
            }

            result.Insert(0, entry);
            if (result.Count > MaxSaved) result.RemoveRange(MaxSaved, result.Count - MaxSaved);
            return result;
        }

        /// <summary>反方向也算同一对（USD→CNY 与 CNY→USD 视为一对，只留最新）。</summary>
        private static bool SamePair(RateEntry a, RateEntry b)
        {
            if (a == null || b == null) return false;
            bool direct = string.Equals(a.From, b.From, StringComparison.OrdinalIgnoreCase)
                          && string.Equals(a.To, b.To, StringComparison.OrdinalIgnoreCase);
            bool reverse = string.Equals(a.From, b.To, StringComparison.OrdinalIgnoreCase)
                          && string.Equals(a.To, b.From, StringComparison.OrdinalIgnoreCase);
            return direct || reverse;
        }

        public static List<RateEntry> Remove(List<RateEntry> list, RateEntry entry)
        {
            var result = new List<RateEntry>();
            if (list == null) return result;
            foreach (var item in list)
                if (!ReferenceEquals(item, entry)) result.Add(item);
            return result;
        }
    }
}
