using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MdViewer.Core
{
    /// <summary>
    /// 文档头部的 YAML front matter（<c>---</c> 包起来那段）。
    ///
    /// 自己解析而不是交给 Markdig：一是 Markdig 的 YAML 扩展只负责「别渲染成正文」，
    /// 不会给我们键值；二是我们需要把它画成一张信息卡（标题 / 日期 / 作者 / 标签），
    /// 这属于阅读体验，不是 Markdown 语法。
    ///
    /// 只认最常见的两种写法，不追求完整 YAML：
    /// <code>
    /// title: 文档标题
    /// tags: [a, b, c]
    /// tags:
    ///   - a
    ///   - b
    /// </code>
    /// 复杂嵌套（对象、多行块）原样留在 Raw 里，不解析也不丢。
    /// </summary>
    public sealed class FrontMatter
    {
        public bool Found { get; private set; }

        /// <summary>原始文本（不含首尾的 --- 行）。</summary>
        public string Raw { get; private set; } = string.Empty;

        /// <summary>键值对，键保持原样大小写，值为去掉两端引号的一行文本。</summary>
        public Dictionary<string, string> Values { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>标签（tags / keywords 两种键都认）。</summary>
        public List<string> Tags { get; } = new List<string>();

        /// <summary>适合放在标题上的值（title / name），没有就是空。</summary>
        public string Title
        {
            get
            {
                string value;
                return Values.TryGetValue("title", out value) ? value : string.Empty;
            }
        }

        public string Get(string key)
        {
            string value;
            return Values.TryGetValue(key, out value) ? value : string.Empty;
        }

        private static readonly Regex Closing = new Regex(@"^(-{3}|\.{3})\s*$", RegexOptions.Compiled);

        /// <summary>
        /// 从 markdown 开头切出 front matter，正文部分通过 <paramref name="body"/> 返回。
        /// 开头不是 <c>---</c>（或没闭合）就整篇当正文。
        /// </summary>
        public static string Split(string markdown, out FrontMatter frontMatter)
        {
            frontMatter = new FrontMatter();
            if (string.IsNullOrEmpty(markdown)) return markdown ?? string.Empty;

            string text = markdown.Replace("\r\n", "\n").Replace('\r', '\n');

            // 允许开头有空行（有些生成的 md 会带）
            int start = 0;
            while (start < text.Length && (text[start] == '\n' || text[start] == ' ' || text[start] == '\t')) start++;
            if (start >= text.Length) return markdown;

            int firstLineEnd = text.IndexOf('\n', start);
            if (firstLineEnd < 0) return markdown;
            if (text.Substring(start, firstLineEnd - start).Trim() != "---") return markdown;

            int pos = firstLineEnd + 1;
            int end = -1;
            int endLineStart = -1;
            while (pos <= text.Length)
            {
                int lineEnd = text.IndexOf('\n', pos);
                if (lineEnd < 0) lineEnd = text.Length;
                string line = text.Substring(pos, lineEnd - pos);
                if (Closing.IsMatch(line)) { end = lineEnd; endLineStart = pos; break; }
                if (lineEnd >= text.Length) break;
                pos = lineEnd + 1;
            }

            if (end < 0) return markdown; // 没闭合，当普通正文（不能吞掉内容）

            frontMatter.Found = true;
            frontMatter.Raw = text.Substring(firstLineEnd + 1, Math.Max(0, endLineStart - firstLineEnd - 1));
            frontMatter.Parse(frontMatter.Raw);

            return end >= text.Length ? string.Empty : text.Substring(end + 1);
        }

        private void Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;

            string[] lines = raw.Split('\n');
            string pendingListKey = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#') continue;

                // "  - 标签" 这种列表项
                if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed == "-")
                {
                    string item = Unquote(trimmed.Length > 2 ? trimmed.Substring(2).Trim() : string.Empty);
                    if (item.Length == 0) continue;
                    if (pendingListKey != null && IsTagKey(pendingListKey)) Tags.Add(item);
                    continue;
                }

                int colon = trimmed.IndexOf(':');
                if (colon <= 0) continue;

                string key = trimmed.Substring(0, colon).Trim();
                string value = trimmed.Substring(colon + 1).Trim();

                if (value.Length == 0)
                {
                    pendingListKey = key;
                    continue;
                }

                pendingListKey = null;
                value = Unquote(value);
                Values[key] = value;

                if (IsTagKey(key)) AddTags(value);
            }
        }

        private static bool IsTagKey(string key)
        {
            return string.Equals(key, "tags", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "keywords", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "categories", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>支持 [a, b] / a, b / "a b" 三种标签写法。</summary>
        private void AddTags(string value)
        {
            string text = value.Trim();
            if (text.StartsWith("[", StringComparison.Ordinal) && text.EndsWith("]", StringComparison.Ordinal))
                text = text.Substring(1, text.Length - 2);

            foreach (var piece in text.Split(','))
            {
                string tag = Unquote(piece.Trim());
                if (tag.Length > 0 && !Tags.Contains(tag)) Tags.Add(tag);
            }
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2)
            {
                char first = value[0];
                char last = value[value.Length - 1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                    return value.Substring(1, value.Length - 2);
            }
            return value;
        }

        /// <summary>
        /// 渲染成一张信息卡。只显示「读文档的人想看」的键，其余忽略 ——
        /// 把 Jekyll 的 layout / permalink 全列出来只是噪音。
        /// </summary>
        public string ToHtml()
        {
            if (!Found) return string.Empty;

            var meta = new List<KeyValuePair<string, string>>();
            AddIfPresent(meta, "作者", "author");
            AddIfPresent(meta, "日期", "date");
            AddIfPresent(meta, "更新", "updated");
            AddIfPresent(meta, "分类", "categories");

            string title = Title;
            bool hasTags = Tags.Count > 0;
            if (title.Length == 0 && meta.Count == 0 && !hasTags) return string.Empty;

            var sb = new StringBuilder(512);
            sb.Append("<div class=\"frontmatter\">");
            if (title.Length > 0)
                sb.Append("<div class=\"fm-title\">").Append(WebUtility.HtmlEncode(title)).Append("</div>");

            if (meta.Count > 0)
            {
                sb.Append("<div class=\"fm-meta\">");
                for (int i = 0; i < meta.Count; i++)
                {
                    if (i > 0) sb.Append("<span class=\"fm-sep\">·</span>");
                    sb.Append("<span class=\"fm-key\">").Append(meta[i].Key).Append("</span>");
                    sb.Append("<span class=\"fm-val\">").Append(WebUtility.HtmlEncode(meta[i].Value)).Append("</span>");
                }
                sb.Append("</div>");
            }

            if (hasTags)
            {
                sb.Append("<div class=\"fm-tags\">");
                foreach (var tag in Tags)
                    sb.Append("<span class=\"fm-tag\">").Append(WebUtility.HtmlEncode(tag)).Append("</span>");
                sb.Append("</div>");
            }

            sb.Append("</div>");
            return sb.ToString();
        }

        private void AddIfPresent(List<KeyValuePair<string, string>> list, string label, string key)
        {
            string value = Get(key);
            if (value.Length == 0) return;
            list.Add(new KeyValuePair<string, string>(label, value));
        }
    }
}
