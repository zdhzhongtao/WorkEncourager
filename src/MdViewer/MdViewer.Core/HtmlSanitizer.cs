using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MdViewer.Core
{
    /// <summary>
    /// 清洗 md 里带进来的内联 HTML：剥掉 script / iframe / on* 事件 / javascript: 链接。
    /// 用正则而不是 HTML 解析器，是因为这里只防"明显有害"的几种，不是通用 sanitizer。
    /// </summary>
    public static class HtmlSanitizer
    {
        private static readonly Regex ScriptBlock = new Regex(@"<script\b[^>]*>.*?</script\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex ScriptSelfClosing = new Regex(@"<script\b[^>]*/\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ScriptOpenOnly = new Regex(@"<script\b[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex IframeBlock = new Regex(@"<iframe\b[^>]*>.*?</iframe\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex IframeSingle = new Regex(@"<iframe\b[^>]*/?>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ObjectEmbed = new Regex(@"<(object|embed|frame|frameset)\b[^>]*>.*?</\1\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>没有闭合标签的写法，比如 &lt;embed src="x"&gt;。</summary>
        private static readonly Regex ObjectEmbedSingle = new Regex(@"<(object|embed|frame|frameset)\b[^>]*/?>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // ── 文档级标签 ──
        // md 里写一句 <style>body{display:none}</style> 就能让整页空白，
        // <meta http-equiv="refresh"> 还能触发跳转。EPUB 那边早就单独剥过，
        // 普通 md 走的是这里，所以必须在这层处理掉。

        private static readonly Regex StyleBlock = new Regex(@"<style\b[^>]*>.*?</style\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex StyleSingle = new Regex(@"<style\b[^>]*/?>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TitleBlock = new Regex(@"<title\b[^>]*>.*?</title\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex TitleSingle = new Regex(@"<title\b[^>]*/?>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LinkTag = new Regex(@"<link\b[^>]*/?>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex BaseTag = new Regex(@"<base\b[^>]*/?>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MetaTag = new Regex(@"<meta\b[^>]*/?>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EventAttrDouble = new Regex(@"\son[a-z]+\s*=\s*""[^""]*""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EventAttrSingle = new Regex(@"\son[a-z]+\s*=\s*'[^']*'",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EventAttrBare = new Regex(@"\son[a-z]+\s*=\s*[^\s>]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex JsHrefDouble = new Regex(@"(href|src|xlink:href)\s*=\s*""\s*javascript:[^""]*""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex JsHrefSingle = new Regex(@"(href|src|xlink:href)\s*=\s*'\s*javascript:[^']*'",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// 所有可能带 URL 的属性。按「先解码再判断」处理，因为 jav&amp;#x61;script: 这种
        /// 写法浏览器照样执行，光看原始字符串拦不住。
        /// </summary>
        private static readonly Regex UrlAttr = new Regex(
            @"\b(href|src|xlink:href|action|formaction|poster)\s*=\s*(?:""([^""]*)""|'([^']*)')",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>不带引号的写法：&lt;a href=javascript:alert(1)&gt;。</summary>
        private static readonly Regex UrlAttrBare = new Regex(
            @"\b(href|src|xlink:href|action|formaction|poster)\s*=\s*([^\s""'>]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] DangerousSchemes =
        {
            "javascript:", "vbscript:", "data:text/html", "data:application/xhtml", "data:image/svg"
        };

        public static string Clean(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;

            string text = html;

            text = ScriptBlock.Replace(text, string.Empty);
            text = ScriptSelfClosing.Replace(text, string.Empty);
            text = ScriptOpenOnly.Replace(text, string.Empty);

            text = IframeBlock.Replace(text, string.Empty);
            text = IframeSingle.Replace(text, string.Empty);
            text = ObjectEmbed.Replace(text, string.Empty);
            text = ObjectEmbedSingle.Replace(text, string.Empty);

            // 文档级标签：<style> 会改整页排版，<link> 会引入外部样式，<meta refresh> 会触发跳转。
            // 版面配色由宿主统一提供，这些一律不该由文档自己决定。
            text = StyleBlock.Replace(text, string.Empty);
            text = StyleSingle.Replace(text, string.Empty);
            text = TitleBlock.Replace(text, string.Empty);
            text = TitleSingle.Replace(text, string.Empty);
            text = LinkTag.Replace(text, string.Empty);
            text = BaseTag.Replace(text, string.Empty);
            text = MetaTag.Replace(text, string.Empty);

            text = EventAttrDouble.Replace(text, string.Empty);
            text = EventAttrSingle.Replace(text, string.Empty);
            text = EventAttrBare.Replace(text, string.Empty);

            text = JsHrefDouble.Replace(text, "$1=\"#\"");
            text = JsHrefSingle.Replace(text, "$1='#'");

            // 带引号的：取属性值 → 还原实体 → 去掉空白与控制符 → 看协议
            text = UrlAttr.Replace(text, match =>
            {
                string name = match.Groups[1].Value;
                string value = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
                return IsDangerousUrl(value) ? name + "=\"#\"" : match.Value;
            });

            text = UrlAttrBare.Replace(text, match =>
            {
                string name = match.Groups[1].Value;
                string value = match.Groups[2].Value;
                return IsDangerousUrl(value) ? name + "=\"#\"" : match.Value;
            });

            return text;
        }

        /// <summary>
        /// .NET 的 HtmlDecode 只认 HTML4 那套实体，`&amp;colon;` 这类 HTML5 的它不认，
        /// 但浏览器在属性值里照样会解码。这里把几个能被用来拼伪协议的补上。
        /// </summary>
        private static readonly Regex Html5Punctuation = new Regex(
            @"&(colon|tab|newline|sol|bsol|semi|comma|period|excl|quest|num);",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>这个 URL 是不是会被当成脚本执行。</summary>
        public static bool IsDangerousUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            // 先还原实体，再掐掉空白与不可见字符：jav&#x61;script: / java\tscript: 都要现原形
            string raw = System.Net.WebUtility.HtmlDecode(value);
            raw = Html5Punctuation.Replace(raw, match =>
            {
                switch (match.Groups[1].Value.ToLowerInvariant())
                {
                    case "colon": return ":";
                    case "tab": return "\t";
                    case "newline": return "\n";
                    case "sol": return "/";
                    case "bsol": return "\\";
                    case "semi": return ";";
                    case "comma": return ",";
                    case "period": return ".";
                    case "excl": return "!";
                    case "quest": return "?";
                    case "num": return "#";
                    default: return match.Value;
                }
            });
            var sb = new System.Text.StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char ch = raw[i];
                if (ch <= 0x20 || ch == 0x7F || ch == '\u200B' || ch == '\uFEFF') continue;
                sb.Append(ch);
            }

            string text = sb.ToString().ToLowerInvariant();
            for (int i = 0; i < DangerousSchemes.Length; i++)
            {
                if (text.StartsWith(DangerousSchemes[i], StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>把 HTML 里的实体还原成纯文本（目录、标题用）。</summary>
        public static string ToPlainText(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            string text = Regex.Replace(html, "<[^>]+>", string.Empty);
            return System.Net.WebUtility.HtmlDecode(text).Trim();
        }
    }
}
