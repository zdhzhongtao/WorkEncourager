using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MdViewer.Core
{
    /// <summary>
    /// 把 md 里的 &lt;img src&gt; 改写成 WebView2 能加载的形式：
    /// 目录内的相对路径 → 虚拟域名 URL；目录外的（../ 跳出根） → 内联 data:；远程地址不动。
    /// 只处理 img 标签，不碰 a 链接（跳转由宿主拦截 NavigationStarting 处理）。
    /// </summary>
    public static class ImageRewriter
    {
        public const long MaxInlineBytes = 8L * 1024 * 1024;

        private static readonly string[] InlineExtensions =
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".ico"
        };

        private static readonly Regex ImgTag = new Regex(@"<img\b[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SrcAttr = new Regex(@"\bsrc\s*=\s*(?:""([^""]*)""|'([^']*)')",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LinkTag = new Regex(@"<a\b[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex HrefAttr = new Regex(@"\bhref\s*=\s*(?:""([^""]*)""|'([^']*)')",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string Rewrite(string html, string baseDir, string virtualHost)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            if (string.IsNullOrWhiteSpace(virtualHost)) return html;
            if (string.IsNullOrWhiteSpace(baseDir)) return html;

            string root = "https://" + virtualHost.Trim().Trim('/') + "/";

            return ImgTag.Replace(html, tag => ReplaceAttr(tag.Value, SrcAttr, value =>
            {
                if (string.IsNullOrWhiteSpace(value)) return null;
                if (ImagePathResolver.IsRemote(value)) return null;

                string relative = ImagePathResolver.ToVirtualRelative(value, baseDir);
                if (relative != null) return root + EncodePath(relative);

                string full = ImagePathResolver.Resolve(value, baseDir);
                if (full != null && File.Exists(full)) return ToDataUri(full);
                return null;
            }));
        }

        /// <summary>
        /// 把相对链接（指向别的 .md / 图片 / 文件）改写成虚拟域名的绝对地址，
        /// 这样宿主能在 NavigationStarting 里截到并自己打开；#锚点 与 http(s) 链接不动。
        /// </summary>
        public static string RewriteLinks(string html, string baseDir, string virtualHost)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            if (string.IsNullOrWhiteSpace(virtualHost)) return html;
            if (string.IsNullOrWhiteSpace(baseDir)) return html;

            string root = "https://" + virtualHost.Trim().Trim('/') + "/";

            return LinkTag.Replace(html, tag => ReplaceAttr(tag.Value, HrefAttr, value =>
            {
                if (string.IsNullOrWhiteSpace(value)) return null;
                if (value.StartsWith("#", StringComparison.Ordinal)) return null;
                if (ImagePathResolver.IsRemote(value)) return null;

                string relative = ImagePathResolver.ToVirtualRelative(value, baseDir);
                if (relative == null) return null;
                return root + EncodePath(relative);
            }));
        }

        /// <summary>导出 html 用：只把反斜杠规整成正斜杠，路径保持相对（规范 8.6）。</summary>
        public static string NormalizeRelative(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            return ImgTag.Replace(html, tag => ReplaceAttr(tag.Value, SrcAttr, value =>
            {
                if (string.IsNullOrWhiteSpace(value)) return null;
                if (ImagePathResolver.IsRemote(value)) return null;
                string normalized = ImagePathResolver.Normalize(value);
                return normalized == value ? null : normalized;
            }));
        }

        /// <summary>
        /// 替换标签里某个属性（src / href）的值。map 返回 null 表示这个属性不动。
        /// 注意：属性正则是在"单个标签字符串"上跑的，所以下标都相对这个标签。
        /// </summary>
        private static string ReplaceAttr(string tag, Regex attrRegex, Func<string, string> map)
        {
            if (string.IsNullOrEmpty(tag)) return tag;

            Match match = attrRegex.Match(tag);
            if (!match.Success) return tag;

            bool doubleQuoted = match.Groups[1].Success;
            string value = doubleQuoted ? match.Groups[1].Value : match.Groups[2].Value;
            string mapped = map(value);
            if (mapped == null) return tag;

            int equals = match.Value.IndexOf('=');
            string name = equals > 0 ? match.Value.Substring(0, equals).Trim() : "src";
            string quote = doubleQuoted ? "\"" : "'";

            // 值必须编码：文件名里带 `"` / `&` 时不能把属性截断
            string encoded = System.Net.WebUtility.HtmlEncode(mapped);
            string replacement = name + "=" + quote + encoded + quote;
            return tag.Substring(0, match.Index) + replacement + tag.Substring(match.Index + match.Length);
        }

        /// <summary>把本地图片读成 data: URI；扩展名不认识、文件太大或读失败返回 null。</summary>
        public static string ToDataUri(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return null;
            string ext = Path.GetExtension(fullPath) ?? string.Empty;
            bool known = false;
            foreach (var candidate in InlineExtensions)
            {
                if (string.Equals(candidate, ext, StringComparison.OrdinalIgnoreCase)) { known = true; break; }
            }
            if (!known) return null;

            try
            {
                var info = new FileInfo(fullPath);
                if (!info.Exists || info.Length <= 0 || info.Length > MaxInlineBytes) return null;
                byte[] bytes = File.ReadAllBytes(fullPath);
                return "data:" + MimeOf(ext) + ";base64," + Convert.ToBase64String(bytes);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is OutOfMemoryException)
            {
                return null;
            }
        }

        public static string MimeOf(string extension)
        {
            switch ((extension ?? string.Empty).ToLowerInvariant())
            {
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".bmp": return "image/bmp";
                case ".webp": return "image/webp";
                case ".svg": return "image/svg+xml";
                case ".ico": return "image/x-icon";
                default: return "application/octet-stream";
            }
        }

        private static string EncodePath(string path)
        {
            var sb = new StringBuilder(path.Length + 8);
            foreach (char ch in path)
            {
                if (ch == ' ') sb.Append("%20");
                else if (ch == '#') sb.Append("%23");
                else if (ch == '?') sb.Append("%3F");
                else if (ch == '%') sb.Append("%25");
                else sb.Append(ch);
            }
            return sb.ToString();
        }
    }
}
