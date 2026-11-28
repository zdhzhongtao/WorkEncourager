using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MdViewer.Core
{
    /// <summary>
    /// 清洗 md / EPUB 里带进来的内联 HTML。
    ///
    /// 只做「明显有害」的几种：脚本、外来框架、事件属性、伪协议、能盖住界面的行内样式。
    /// 实现分两步：
    ///   1) 先成对删掉 script / style / iframe 这类整块元素（未闭合的开标签在第二步丢弃）；
    ///   2) 逐标签扫描，**只改标签内部**，正文文本原样保留。
    ///
    /// 为什么不用一条大正则跑全文：0.5.x 的写法会把正文/代码里的 `onerror="…"` 当成属性删掉
    /// （实测 `<p>把 onclick= 去掉</p>` 会变成 `<p>把>`），而且漏掉 `src="x"onerror=…` 这种
    /// 属性值后没空格的写法。逐标签处理既不会误伤正文，也不依赖「属性前必须有空格」。
    /// </summary>
    public static class HtmlSanitizer
    {
        /// <summary>整块删掉（连同内容）的元素。</summary>
        private static readonly string[] BlockElements =
        {
            "script", "style", "iframe", "object", "frame", "frameset", "title", "noscript"
        };

        /// <summary>只删标签本身的元素（没有闭合标签，或由浏览器自己配对）。</summary>
        private static readonly HashSet<string> VoidBlocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "link", "base", "meta", "embed"
        };

        /// <summary>连同内容一起丢弃的标签（表单控件是钓鱼页的入口，也不属于 Markdown 语法）。</summary>
        private static readonly HashSet<string> FormBlocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "form", "input", "textarea", "select", "option"
        };

        /// <summary>即使没配成对（自闭合 / 未闭合），标签本身也必须丢掉。</summary>
        private static readonly HashSet<string> TagBlocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "script", "style", "iframe", "object", "frame", "frameset", "title", "noscript", "embed", "link", "base", "meta"
        };

        private static readonly Regex TagName = new Regex(@"^<\s*/?\s*([a-zA-Z][a-zA-Z0-9:-]*)", RegexOptions.Compiled);

        /// <summary>事件属性。属性名前面不能是单词字符、冒号或连字符（`data-onerror` 不算）。</summary>
        private static readonly Regex EventAttr = new Regex(
            @"(?<![\w:-])on[a-zA-Z]+\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)",
            RegexOptions.Compiled);

        private static readonly Regex SrcdocAttr = new Regex(
            @"(?<![\w:-])srcdoc\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)",
            RegexOptions.Compiled);

        /// <summary>带 URL 的属性：值可能是双引号、单引号或不带引号。</summary>
        private static readonly Regex UrlAttr = new Regex(
            @"(?<![\w:-])(href|src|xlink:href|action|formaction|poster|srcset)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex StyleAttr = new Regex(
            @"(?<![\w:-])style\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] DangerousSchemes =
        {
            "javascript:", "vbscript:", "data:text/html", "data:application/xhtml", "data:image/svg"
        };

        /// <summary>允许出现在 style="" 里的属性。刻意不含 position / inset / z-index / overflow / transform。</summary>
        private static readonly HashSet<string> SafeStyleProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "text-align", "font-weight", "font-style", "font-size", "font-family",
            "text-decoration", "text-indent", "line-height", "vertical-align",
            "color", "background-color", "white-space", "word-break", "overflow-wrap",
            "width", "height", "max-width", "border", "border-radius", "opacity"
        };

        public static string Clean(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;

            string text = RemoveComments(html);
            text = RemoveBlockElements(text);
            return ScanTags(text);
        }

        // ───────────────────────── 第一步：整块元素 ─────────────────────────

        private static string RemoveComments(string text)
        {
            // 成对注释整段删；未闭合的 `<!--` 也要删掉 —— 否则浏览器会把后面整篇当注释，
            // 表现为「后半篇文档凭空消失」。
            text = Regex.Replace(text, "<!--.*?-->", string.Empty, RegexOptions.Singleline);
            return text.Replace("<!--", string.Empty);
        }

        private static string RemoveBlockElements(string text)
        {
            foreach (string name in BlockElements)
                text = RemoveElement(text, name);
            return text;
        }

        /// <summary>
        /// 线性删掉一个元素：找到开标签，再找对应闭标签；没有闭合就只删开标签。
        /// 不用正则是因为大量 `&lt;script` 会让 `.*?</script>` 回溯到超线性。
        /// </summary>
        private static string RemoveElement(string text, string name)
        {
            var sb = new StringBuilder(text.Length);
            int pos = 0;
            string open = "<" + name;

            while (pos < text.Length)
            {
                int start = text.IndexOf(open, pos, StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                {
                    sb.Append(text, pos, text.Length - pos);
                    break;
                }

                int after = start + open.Length;
                bool boundary = after >= text.Length || char.IsWhiteSpace(text[after]) || text[after] == '>' || text[after] == '/';
                if (!boundary)
                {
                    sb.Append(text, pos, after - pos);
                    pos = after;
                    continue;
                }

                int openEnd = text.IndexOf('>', after);
                if (openEnd < 0)
                {
                    // 开标签都没闭合：整段丢弃（浏览器也会当标签处理）
                    sb.Append(text, pos, start - pos);
                    pos = text.Length;
                    break;
                }

                sb.Append(text, pos, start - pos);

                // 自闭合 <style ... />
                if (text[openEnd - 1] == '/')
                {
                    pos = openEnd + 1;
                    continue;
                }

                string close = "</" + name;
                int closeStart = text.IndexOf(close, openEnd + 1, StringComparison.OrdinalIgnoreCase);
                if (closeStart < 0)
                {
                    // 未闭合：只删开标签，内容留给逐标签清洗（不会静默吞掉正文）
                    pos = openEnd + 1;
                    continue;
                }

                int closeEnd = text.IndexOf('>', closeStart);
                pos = closeEnd < 0 ? text.Length : closeEnd + 1;
            }

            return sb.ToString();
        }
        // ───────────────────────── 第二步：逐标签 ─────────────────────────

        private static string ScanTags(string input)
        {
            var sb = new StringBuilder(input.Length);
            int i = 0;
            while (i < input.Length)
            {
                char c = input[i];
                if (c != '<')
                {
                    sb.Append(c);
                    i++;
                    continue;
                }

                int end = FindTagEnd(input, i);
                if (end < 0)
                {
                    // 没有闭合的 `<`：看起来像标签就把剩下整段丢掉（浏览器也会这么干），
                    // 否则当普通文本（比如数学里的 a < b）。
                    if (i + 1 < input.Length && LooksLikeTagStart(input[i + 1]))
                        break;
                    sb.Append(c);
                    i++;
                    continue;
                }

                string tag = input.Substring(i, end - i + 1);
                sb.Append(CleanTag(tag));
                i = end + 1;
            }
            return sb.ToString();
        }

        private static bool LooksLikeTagStart(char ch)
        {
            return char.IsLetter(ch) || ch == '/' || ch == '!' || ch == '?';
        }

        /// <summary>找标签的 `>`，跳过引号里的内容。遇到嵌套的 `&lt;` 认为标签不合法。</summary>
        private static int FindTagEnd(string text, int start)
        {
            char quote = '\0';
            for (int i = start + 1; i < text.Length; i++)
            {
                char ch = text[i];
                if (quote != '\0')
                {
                    if (ch == quote) quote = '\0';
                    continue;
                }
                if (ch == '"' || ch == '\'')
                {
                    quote = ch;
                    continue;
                }
                if (ch == '>') return i;
                if (ch == '<') return -1;
            }
            return -1;
        }

        private static string CleanTag(string tag)
        {
            // 注释 / doctype / CDATA 之类：TagName 不命中，原样保留（注释已在第一步删掉）。
            Match nameMatch = TagName.Match(tag);
            if (!nameMatch.Success) return tag;

            string name = nameMatch.Groups[1].Value;
            if (TagBlocked.Contains(name) || VoidBlocked.Contains(name) || FormBlocked.Contains(name)) return string.Empty;

            string result = EventAttr.Replace(tag, string.Empty);
            result = SrcdocAttr.Replace(result, string.Empty);

            result = UrlAttr.Replace(result, match =>
            {
                string attr = match.Groups[1].Value;
                string value = match.Groups[2].Success ? match.Groups[2].Value
                    : match.Groups[3].Success ? match.Groups[3].Value
                    : match.Groups[4].Value;

                // srcset 里可以塞多个候选地址，统一丢掉（Markdown / EPUB 都很少用）
                if (string.Equals(attr, "srcset", StringComparison.OrdinalIgnoreCase)) return string.Empty;

                return IsDangerousUrl(value) ? attr + "=\"#\"" : match.Value;
            });

            result = StyleAttr.Replace(result, match =>
            {
                string value = match.Groups[1].Success ? match.Groups[1].Value
                    : match.Groups[2].Success ? match.Groups[2].Value
                    : match.Groups[3].Value;
                string safe = SanitizeStyle(value);
                return safe.Length == 0 ? string.Empty : "style=\"" + safe + "\"";
            });

            return result;
        }

        /// <summary>只保留排版类属性，去掉能整页覆盖/定位的属性。</summary>
        public static string SanitizeStyle(string style)
        {
            if (string.IsNullOrWhiteSpace(style)) return string.Empty;

            var kept = new List<string>();
            foreach (string part in style.Split(';'))
            {
                int colon = part.IndexOf(':');
                if (colon <= 0) continue;

                string property = part.Substring(0, colon).Trim();
                string value = part.Substring(colon + 1).Trim();
                if (property.Length == 0 || value.Length == 0) continue;
                if (!SafeStyleProperties.Contains(property)) continue;
                if (value.IndexOf("url(", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (value.IndexOf("expression", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                kept.Add(property + ": " + value);
            }
            return string.Join("; ", kept.ToArray());
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
            var sb = new StringBuilder(raw.Length);
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

        /// <summary>把 HTML 里的实体还原成纯文本（目录、标题用）。线性扫描，避免大量 `&lt;` 触发回溯。</summary>
        public static string ToPlainText(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;

            var sb = new StringBuilder(html.Length);
            int i = 0;
            while (i < html.Length)
            {
                char ch = html[i];
                if (ch != '<')
                {
                    sb.Append(ch);
                    i++;
                    continue;
                }

                int end = FindTagEnd(html, i);
                if (end < 0)
                {
                    if (i + 1 < html.Length && LooksLikeTagStart(html[i + 1])) break;
                    sb.Append(ch);
                    i++;
                    continue;
                }
                i = end + 1;   // 跳过标签，保留正文
            }
            return System.Net.WebUtility.HtmlDecode(sb.ToString()).Trim();
        }
    }
}
