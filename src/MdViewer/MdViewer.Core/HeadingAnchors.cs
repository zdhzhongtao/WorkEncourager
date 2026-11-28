using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MdViewer.Core
{
    /// <summary>目录侧栏的一项。</summary>
    public sealed class TocItem
    {
        public int Level { get; set; }
        public string Text { get; set; }
        public string Id { get; set; }

        public override string ToString()
        {
            return Level + "|" + Id + "|" + Text;
        }
    }

    /// <summary>标题扫描结果：加了 id 的 html + 目录树。</summary>
    public sealed class HeadingScan
    {
        public string Html { get; set; }
        public List<TocItem> Toc { get; set; }
    }

    /// <summary>
    /// 给 h1~h6 补 id（Markdig 不生成 id 时由这里统一生成），同时抽出 h1~h3 做目录。
    /// id 由标题文本算 slug，重名自动加 -1 / -2 后缀。
    /// </summary>
    public static class HeadingAnchors
    {
        private const int TocMaxLevel = 3;

        private static readonly Regex HeadingTag = new Regex(
            @"<h([1-6])([^>]*)>(.*?)</h\1\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex ExistingId = new Regex(
            @"\sid\s*=\s*(?:""([^""]*)""|'([^']*)')",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static HeadingScan Process(string html)
        {
            var result = new HeadingScan { Html = html ?? string.Empty, Toc = new List<TocItem>() };
            if (string.IsNullOrEmpty(html)) return result;

            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var toc = result.Toc;

            result.Html = HeadingTag.Replace(html, match =>
            {
                int level = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                string attrs = match.Groups[2].Value ?? string.Empty;
                string inner = match.Groups[3].Value ?? string.Empty;

                string id = null;
                var existing = ExistingId.Match(attrs);
                if (existing.Success)
                    id = existing.Groups[1].Success ? existing.Groups[1].Value : existing.Groups[2].Value;
                if (string.IsNullOrWhiteSpace(id)) id = Slug(HtmlSanitizer.ToPlainText(inner));
                id = Unique(seen, id);

                // 重复的显式 id 也要改写成唯一值，否则目录里是 "dup-1"、页面里还是两个 "dup"，
                // 点第二条目录永远跳不过去。
                attrs = SetId(attrs, id);

                if (level <= TocMaxLevel)
                {
                    string text = HtmlSanitizer.ToPlainText(inner);
                    if (text.Length == 0) text = "（无标题）";
                    toc.Add(new TocItem { Level = level, Text = text, Id = id });
                }

                return "<h" + level + attrs + ">" + inner + "</h" + level + ">";
            });

            return result;
        }

        /// <summary>标题文本 → 锚点 id。保留汉字与字母数字，空白与连字转 -。</summary>
        public static string Slug(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "section";

            var sb = new StringBuilder();
            foreach (char ch in text.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                else if (ch >= 0x2E80 && !char.IsPunctuation(ch) && !char.IsSeparator(ch)) sb.Append(ch); // 汉字 / 假名 / emoji
                else if (ch == ' ' || ch == '\t' || ch == '-' || ch == '_' || ch == '.' || ch == '/') sb.Append('-');
                // 其余标点一律丢掉
            }

            string slug = sb.ToString();
            while (slug.Contains("--")) slug = slug.Replace("--", "-");
            slug = slug.Trim('-');
            return slug.Length == 0 ? "section" : slug;
        }

        /// <summary>把标签属性里的 id 换成给定值；没有就补一个。</summary>
        private static string SetId(string attrs, string id)
        {
            string safe = Regex.Replace(id ?? string.Empty, "[\"<>]", string.Empty);
            Match existing = ExistingId.Match(attrs);
            if (existing.Success)
                return attrs.Substring(0, existing.Index) + " id=\"" + safe + "\"" + attrs.Substring(existing.Index + existing.Length);

            return attrs + " id=\"" + safe + "\"";
        }

        private static string Unique(Dictionary<string, int> seen, string id)
        {            if (!seen.TryGetValue(id, out int used))
            {
                seen[id] = 0;
                return id;
            }
            int next = used + 1;
            seen[id] = next;
            string candidate = id + "-" + next.ToString(CultureInfo.InvariantCulture);
            // 万一 -1 也被别的标题占了，继续往后找
            while (seen.ContainsKey(candidate))
            {
                next++;
                seen[id] = next;
                candidate = id + "-" + next.ToString(CultureInfo.InvariantCulture);
            }
            seen[candidate] = 0;
            return candidate;
        }
    }
}
