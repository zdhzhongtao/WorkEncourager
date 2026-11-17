using System;
using System.Collections.Generic;
using System.Globalization;

namespace TableDiff.Core
{
    /// <summary>单元格取值的规整：数字去浮点尾巴、日期按 yyyy-MM-dd。</summary>
    public static class CellText
    {
        public const string InvariantNumberFormat = "0.###############";

        /// <summary>把 double 变成人能看的文本：1.2000000000000002 → 1.2，200.0 → 200。</summary>
        public static string FormatNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return string.Empty;
            string s = value.ToString(InvariantNumberFormat, CultureInfo.InvariantCulture);
            if (s == "-0") s = "0";
            return s;
        }

        /// <summary>日期单元格：有时间的保留到分钟，只有日期的按 yyyy-MM-dd。</summary>
        public static string FormatDate(DateTime value)
        {
            if (value.TimeOfDay == TimeSpan.Zero)
                return value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        public static string FormatTimeSpan(TimeSpan value)
        {
            if (value.TotalDays >= 1) return FormatNumber(value.TotalDays);
            return value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 尝试把文本当数字解析（允许千分位逗号、前后普通空格）。
        /// 以下情况一律不当数字，避免把编号认成数值：
        /// 1) NaN / Infinity —— double 会认，但它们跟空值一比较就全乱了；
        /// 2) 带前导 0 的（007、0755 这类是编号，和 7、755 不是一回事）；
        /// 3) 超过 15 位有效数字（Excel 只有 15 位精度，再比较就会误判、导出也会变形）。
        /// </summary>
        public static bool TryParseNumber(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string s = text.Trim().Replace(",", string.Empty).Replace("\u3000", string.Empty);
            if (s.Length == 0) return false;
            // 只有一个减号 / 只有小数点不算数字
            if (s == "-" || s == "." || s == "-." || s == "+") return false;

            if (s.Length > 1 && s[0] == '0' && s[1] >= '0' && s[1] <= '9') return false;
            if (s.Length > 1 && s[0] == '+' ) return false;

            int digits = 0;
            bool hasExp = false;
            bool hasDot = false;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch >= '0' && ch <= '9') digits++;
                else if (ch == 'e' || ch == 'E') hasExp = true;
                else if (ch == '.') hasDot = true;
            }
            if (digits == 0) return false;
            // 只有「纯整数的长串」按文本（订单号、身份证）；1.2000000000000002 这种浮点尾巴还要照常比
            if (!hasExp && !hasDot && digits > 15) return false;

            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
            if (double.IsNaN(value) || double.IsInfinity(value)) return false;
            return true;
        }

        /// <summary>
        /// 比较用的规整值：能当数字看的统一成数字文本，其余去掉首尾空白。
        /// 这样 Excel 里的 1000（数字）和 CSV 里的 "1000"（文本）被判为同一个值。
        /// </summary>
        public static string Canonical(string text, bool ignoreCase, bool trimSpace, bool emptyAsNull)
        {
            if (text == null) text = string.Empty;
            if (trimSpace) text = text.Trim();
            if (emptyAsNull && text.Length == 0) return string.Empty;

            if (TryParseNumber(text, out double n)) return FormatNumber(n);
            return ignoreCase ? text.ToUpperInvariant() : text;
        }

        /// <summary>千分位显示（差异单元格里 "1,200 → 1,500" 更好读）。</summary>
        public static string WithThousands(string text)
        {
            if (!TryParseNumber(text, out double n)) return text;
            if (text.IndexOf(',') >= 0) return text;              // 本来就有千分位
            if (Math.Abs(n) >= 1000 && n == Math.Floor(n) && Math.Abs(n) < 1e15)
                return n.ToString("#,##0", CultureInfo.InvariantCulture);
            return text;
        }

        /// <summary>判断一行是否全空（所有单元格都是空白）。</summary>
        public static bool IsBlankRow(string[] row)
        {
            if (row == null) return true;
            for (int i = 0; i < row.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(row[i])) return false;
            }
            return true;
        }

        public static string JoinKey(IEnumerable<string> parts)
        {
            return string.Join("\u0001", parts);
        }
    }
}
