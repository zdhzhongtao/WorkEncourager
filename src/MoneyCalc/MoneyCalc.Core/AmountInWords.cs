using System;
using System.Globalization;
using System.Text;

namespace MoneyCalc.Core
{
    /// <summary>
    /// 人民币大写。按《正确填写票据和结算凭证的基本规定》：
    /// 壹贰叁肆伍陆柒捌玖、拾佰仟万亿、元角分整零。
    /// </summary>
    public static class AmountInWords
    {
        /// <summary>上限 999,999,999,999.99（不到一万亿）。</summary>
        public const decimal MaxAmount = 999999999999.99m;

        private static readonly string[] Digits = { "零", "壹", "贰", "叁", "肆", "伍", "陆", "柒", "捌", "玖" };
        private static readonly string[] PlaceUnits = { "仟", "佰", "拾", "" };
        private static readonly string[] GroupUnits = { "", "万", "亿" };

        /// <summary>转成大写。超范围抛 <see cref="FriendlyException"/>。</summary>
        public static string ToChinese(decimal amount)
        {
            if (!TryToChinese(amount, out string text, out string error)) throw new FriendlyException(error);
            return text;
        }

        public static bool TryToChinese(decimal amount, out string text, out string error)
        {
            text = string.Empty;
            error = string.Empty;

            if (amount > MaxAmount || amount < -MaxAmount)
            {
                error = "金额过大，最多 999,999,999,999.99";
                return false;
            }

            bool negative = amount < 0;
            decimal abs = Math.Abs(amount);

            // 先变成「分」，避免浮点误差（0.1 + 0.2 那类问题在 decimal 上不存在，但四舍五入要显式）
            long cents = (long)decimal.Round(abs * 100m, 0, MidpointRounding.AwayFromZero);
            long yuan = cents / 100;
            int jiao = (int)(cents / 10 % 10);
            int fen = (int)(cents % 10);

            var sb = new StringBuilder();
            if (negative) sb.Append('负');
            sb.Append(IntToChinese(yuan)).Append('元');

            if (jiao == 0 && fen == 0)
            {
                sb.Append('整');
            }
            else
            {
                if (jiao > 0) sb.Append(Digits[jiao]).Append('角');
                else if (fen > 0) sb.Append('零');   // 有分无角时补一个「零」，否则读不通

                if (fen > 0) sb.Append(Digits[fen]).Append('分');
            }

            text = sb.ToString();
            return true;
        }

        /// <summary>整数部分：按四位一组，从高到低拼「万」「亿」。</summary>
        public static string IntToChinese(long value)
        {
            if (value == 0) return "零";

            var groups = new System.Collections.Generic.List<int>(4);
            long rest = value;
            while (rest > 0)
            {
                groups.Add((int)(rest % 10000));
                rest /= 10000;
            }

            var sb = new StringBuilder();
            bool pendingZero = false;

            for (int i = groups.Count - 1; i >= 0; i--)
            {
                int group = groups[i];
                if (group == 0)
                {
                    if (sb.Length > 0) pendingZero = true;
                    continue;
                }

                // 不是最高位、且这一组不足四位（或中间隔了整组 0）时要补一个「零」
                if (sb.Length > 0 && (pendingZero || group < 1000)) sb.Append('零');
                sb.Append(GroupToChinese(group)).Append(GroupUnits[i]);
                pendingZero = false;
            }

            return sb.ToString();
        }

        /// <summary>一组四位（0 &lt; group &lt; 10000）。中间的 0 只读一个，末尾的 0 不读。</summary>
        private static string GroupToChinese(int group)
        {
            var sb = new StringBuilder();
            bool pendingZero = false;

            for (int i = 0; i < 4; i++)
            {
                int digit = group / (i == 0 ? 1000 : i == 1 ? 100 : i == 2 ? 10 : 1) % 10;
                if (digit == 0)
                {
                    if (sb.Length > 0) pendingZero = true;
                    continue;
                }

                if (pendingZero)
                {
                    sb.Append('零');
                    pendingZero = false;
                }
                sb.Append(Digits[digit]).Append(PlaceUnits[i]);
            }

            return sb.ToString();
        }

        /// <summary>把用户输入（允许千分位逗号、空格）解析成金额。</summary>
        public static bool TryParse(string input, out decimal value, out string error)
        {
            value = 0;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                error = "请输入金额";
                return false;
            }

            string text = input.Trim().Replace(",", string.Empty).Replace("，", string.Empty).Replace(" ", string.Empty);
            if (text.Length == 0 || text == "-" || text == "." || text == "-.")
            {
                error = "请输入金额";
                return false;
            }

            if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                error = "看不懂这个金额";
                return false;
            }

            if (value > MaxAmount || value < -MaxAmount)
            {
                error = "金额过大，最多 999,999,999,999.99";
                return false;
            }

            value = decimal.Round(value, 2, MidpointRounding.AwayFromZero);
            return true;
        }
    }
}
