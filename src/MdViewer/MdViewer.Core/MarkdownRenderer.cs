using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;

namespace MdViewer.Core
{
    /// <summary>渲染结果：可以插进页面的 html 片段 + 目录。</summary>
    public sealed class RenderedMarkdown
    {
        public string BodyHtml { get; set; }
        public List<TocItem> Toc { get; set; }

        /// <summary>非空表示渲染降级了（例如嵌套过深被 Markdig 拒绝），宿主可以提示用户。</summary>
        public string Error { get; set; }

        /// <summary>文档头的 YAML front matter（没有就是 Found=false）。</summary>
        public FrontMatter FrontMatter { get; set; }

        /// <summary>正文里有数学公式 —— 决定要不要把 KaTeX 加载进来。</summary>
        public bool HasMath
        {
            get { return !string.IsNullOrEmpty(BodyHtml) && BodyHtml.Contains("class=\"math\""); }
        }

        /// <summary>正文里有 mermaid 图表代码块。</summary>
        public bool HasMermaid
        {
            get
            {
                if (string.IsNullOrEmpty(BodyHtml)) return false;
                return BodyHtml.Contains("language-mermaid") || BodyHtml.Contains("class=\"mermaid\"");
            }
        }

        /// <summary>正文里有代码块 —— 决定要不要把 highlight.js 加载进来。</summary>
        public bool HasCodeBlock
        {
            get { return !string.IsNullOrEmpty(BodyHtml) && BodyHtml.Contains("<pre"); }
        }
    }

    /// <summary>整页 html 的选项。</summary>
    public sealed class PageOptions
    {
        public string Title { get; set; }

        /// <summary>主题 id（见 <see cref="MarkdownThemes"/>）。留空则按 <see cref="DarkTheme"/> 取默认主题。</summary>
        public string ThemeId { get; set; }

        /// <summary>老字段：只表达明暗。ThemeId 有效时以 ThemeId 为准。</summary>
        public bool DarkTheme { get; set; }

        public int FontSize { get; set; }

        /// <summary>正文字体名（空 = 用 css 里的默认字体栈）。</summary>
        public string FontBody { get; set; }

        /// <summary>代码字体名。</summary>
        public string FontCode { get; set; }

        /// <summary>标题字体名（空 = 跟随正文）。</summary>
        public string FontHeading { get; set; }

        /// <summary>正文栏最大宽度（px），控制每行字数。0 表示不限制。</summary>
        public int ContentWidth { get; set; } = 880;

        /// <summary>
        /// 页面允许加载内容的来源（形如 https://md1.local）。给了就写 CSP：除此来源与 data: 之外一律不加载。
        /// 刻意不使用 &lt;base&gt;：用了以后 md 里的 #锚点 会被拼成跨文档跳转，页面内跳转就废了。
        /// </summary>
        public string ContentOrigin { get; set; }

        /// <summary>
        /// 额外允许的图片来源。EPUB 的正文和图片来自两个不同的虚拟域名（一个放渲染缓存、
        /// 一个映射解压目录），只给一个源会把图片全拦下来。
        /// </summary>
        public List<string> ExtraOrigins { get; set; }

        /// <summary>加到 body 上的额外 class（EPUB 用来挂特殊排版）。</summary>
        public string BodyClass { get; set; }

        public bool ForExport { get; set; }

        /// <summary>是否加载 KaTeX（正文里真有公式才加载，省一次解析）。</summary>
        public bool IncludeMath { get; set; }

        /// <summary>是否加载 Mermaid（正文里真有图表才加载 —— 它有 2.5MB）。</summary>
        public bool IncludeMermaid { get; set; }

        /// <summary>是否加载 highlight.js 做代码高亮（有代码块才加载）。</summary>
        public bool IncludeCodeHighlight { get; set; }

        /// <summary>
        /// 第三方脚本 / 样式所在的虚拟域名目录，形如 <c>https://mdviewer.tmp/vendor/</c>。
        /// 留空则不注入任何外部资源（导出用）。
        /// </summary>
        public string VendorBase { get; set; }

        /// <summary>用户自定义皮肤 css（空 = 不用）。排在 style.css 之后，好让它覆盖变量。</summary>
        public string CustomCss { get; set; }

        public PageOptions()
        {
            Title = "Markdown";
            FontSize = 16;
            ExtraOrigins = new List<string>();
        }

        /// <summary>把设置里的字体名拼成 css 的 font-family 值。字体名里的单引号已在设置侧清掉。</summary>
        public static string FontStack(string name, string fallback)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string value = "'" + name.Trim() + "'";
            return string.IsNullOrWhiteSpace(fallback) ? value : value + ", " + fallback;
        }
    }


    /// <summary>
    /// md → html。流程固定为：Markdig 渲染 → 清洗内联 HTML → 任务列表换字形 → 补标题 id。
    /// 顺序不能换：清洗必须在补 id 之前，否则 md 里写死的 on* 会跟着标题一起进页面。
    /// </summary>
    public static class MarkdownRenderer
    {
        private static readonly Regex Checkbox = new Regex(
            @"<input\b[^>]*type\s*=\s*""checkbox""[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);


        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseEmphasisExtras()
            .UsePipeTables()
            .UseTaskLists()
            .UseFootnotes()
            .UseAutoLinks()
            // 数学要排在 EmphasisExtras 之前处理（Markdig 内部按扩展优先级走），
            // 否则 $a^2$ 里的 ^ 会被当成上标语法吃掉。
            .UseMathematics()
            .Build();

        public static RenderedMarkdown Render(string markdown)
        {
            FrontMatter frontMatter;
            string body = FrontMatter.Split(markdown ?? string.Empty, out frontMatter);

            string raw;
            try
            {
                raw = Markdown.ToHtml(body, Pipeline);
            }
            catch (Exception ex)
            {
                // Markdig 对 >200 层嵌套会抛 ArgumentException（203 字节就能触发）。
                // 不能让它冒到全局 handler —— 那样文档打不开、也没有任何提示。
                return PlainFallback(body, frontMatter, ex);
            }

            // 任务列表要先生成字形再清洗：清洗器会丢弃 input（表单控件），
            // checkbox 必须在这之前换成 span。
            string clean = NormalizeTaskLists(raw);
            clean = HtmlSanitizer.Clean(clean);
            HeadingScan scan = HeadingAnchors.Process(clean);

            // 信息卡放在正文最前面。它不属于 Markdown 语法，但读文档的人需要它。
            string card = frontMatter.ToHtml();
            string html = card.Length > 0 ? card + scan.Html : scan.Html;

            return new RenderedMarkdown
            {
                BodyHtml = html,
                Toc = scan.Toc,
                FrontMatter = frontMatter
            };
        }

        /// <summary>渲染失败时退回纯文本，至少还能读内容。</summary>
        private static RenderedMarkdown PlainFallback(string body, FrontMatter frontMatter, Exception error)
        {
            string escaped = WebUtility.HtmlEncode(body ?? string.Empty);
            return new RenderedMarkdown
            {
                BodyHtml = "<p class=\"render-error\">这篇文档的结构过于复杂（可能是深层嵌套），已按纯文本显示。</p>"
                           + "<pre class=\"plain-fallback\">" + escaped + "</pre>",
                Toc = new List<TocItem>(),
                FrontMatter = frontMatter,
                Error = error == null ? "渲染失败" : error.Message
            };
        }

        /// <summary>只出 html 片段，不包整页。</summary>
        public static string RenderBody(string markdown)
        {
            return Render(markdown).BodyHtml;
        }

        /// <summary>把 GFM 的 input checkbox 换成 ☐ / ☑（规范 8.3 要求用字形）。</summary>
        public static string NormalizeTaskLists(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            return Checkbox.Replace(html, match =>
            {
                bool done = match.Value.IndexOf("checked", StringComparison.OrdinalIgnoreCase) >= 0;
                return done
                    ? "<span class=\"task-box done\">\u2611</span>"
                    : "<span class=\"task-box todo\">\u2610</span>";
            });
        }

        /// <summary>整页 html（样式与脚本全部内联，零远程引用）。</summary>
        public static string BuildPage(RenderedMarkdown doc, PageOptions options)
        {
            if (options == null) options = new PageOptions();
            if (doc == null) doc = new RenderedMarkdown { BodyHtml = string.Empty, Toc = new List<TocItem>() };

            int fontSize = options.FontSize <= 0 ? 16 : options.FontSize;
            string title = string.IsNullOrWhiteSpace(options.Title) ? "Markdown" : options.Title;

            // 主题变量定义在 <html data-theme=...> 上，body 继承。
            // 这样正文两侧的画布（由 <html> 绘制）和 body 一定同色 —— 只给 body 上色会露出白边。
            MarkdownTheme theme = MarkdownThemes.Resolve(options.ThemeId) ?? MarkdownThemes.ForMode(options.DarkTheme);
            string mode = theme.IsDark ? "dark" : "light";

            // 内联脚本用 nonce 放行。HtmlSanitizer 是正则黑名单，天然有绕过空间；
            // 把 script-src 从 'unsafe-inline' 收紧成 nonce，等于给清洗再加一道硬保险。
            string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));

            string bodyClass = options.ForExport ? "for-export" : string.Empty;
            if (!string.IsNullOrWhiteSpace(options.BodyClass))
                bodyClass = bodyClass.Length == 0 ? options.BodyClass.Trim() : bodyClass + " " + options.BodyClass.Trim();

            var sb = new StringBuilder(16 * 1024);
            sb.Append("<!DOCTYPE html>\r\n<html lang=\"zh-CN\" data-theme=\"").Append(WebUtility.HtmlEncode(theme.Id))
              .Append("\" data-mode=\"").Append(mode).Append("\">\r\n<head>\r\n");
            sb.Append("<meta charset=\"utf-8\">\r\n");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\r\n");
            sb.Append("<title>").Append(WebUtility.HtmlEncode(title)).Append("</title>\r\n");

            var origins = new List<string>();
            if (!string.IsNullOrEmpty(options.ContentOrigin))
            {
                origins.Add(options.ContentOrigin.TrimEnd('/'));
                if (options.ExtraOrigins != null)
                {
                    foreach (var extra in options.ExtraOrigins)
                    {
                        if (string.IsNullOrWhiteSpace(extra)) continue;
                        string trimmed = extra.TrimEnd('/');
                        if (!origins.Contains(trimmed)) origins.Add(trimmed);
                    }
                }
            }

            // 第三方库（KaTeX / Mermaid / highlight.js）从虚拟域按需加载：
            // 内联进每一页会把每篇文档都拖上 3MB，而绝大多数文档根本用不到它们。
            string vendorBase = null;
            string vendorOrigin = null;
            if (!string.IsNullOrWhiteSpace(options.VendorBase))
            {
                vendorBase = options.VendorBase.TrimEnd('/') + "/";
                try { vendorOrigin = new Uri(vendorBase).GetLeftPart(UriPartial.Authority); }
                catch (UriFormatException) { vendorBase = null; vendorOrigin = null; }
            }

            // 有 ContentOrigin = 阅读器里的页面，最严格；没有 = 导出给别人看的页面，
            // 只锁脚本，图片/字体照常放行（导出的 md 经常挂着在线图片）。
            sb.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"");
            if (origins.Count > 0)
            {
                sb.Append("default-src 'none'; base-uri 'none'; img-src ");
                sb.Append(WebUtility.HtmlEncode(string.Join(" ", origins.ToArray())));
                sb.Append(" data:; style-src 'unsafe-inline'");
                if (vendorOrigin != null) sb.Append(' ').Append(vendorOrigin);
                sb.Append("; font-src data:");
                if (vendorOrigin != null) sb.Append(' ').Append(vendorOrigin);
                sb.Append("; ");
            }
            else
            {
                sb.Append("base-uri 'none'; ");
            }
            sb.Append("script-src 'nonce-").Append(nonce).Append('\'');
            if (vendorOrigin != null) sb.Append(' ').Append(vendorOrigin);
            sb.Append(";\">\r\n");

            sb.Append("<style>\r\n").Append(Assets.StyleCss).Append("\r\n</style>\r\n");

            // 数学公式的样式表必须在 style.css 之后（KaTeX 自带一套排版规则）
            if (options.IncludeMath && vendorBase != null)
                sb.Append("<link rel=\"stylesheet\" href=\"").Append(WebUtility.HtmlEncode(vendorBase))
                  .Append("katex/katex.min.css\">\r\n");

            // 用户皮肤排在最后：它要能盖掉主题变量
            if (!string.IsNullOrWhiteSpace(options.CustomCss))
                sb.Append("<style>\r\n").Append(options.CustomCss).Append("\r\n</style>\r\n");

            sb.Append("</head>\r\n");

            // 字号与栏宽走 body 的内联变量：宿主改这两个值只要 postMessage，不用重新渲染整页
            string widthValue = options.ContentWidth > 0
                ? options.ContentWidth.ToString(CultureInfo.InvariantCulture) + "px"
                : "none";

            sb.Append("<body");
            if (bodyClass.Length > 0) sb.Append(" class=\"").Append(WebUtility.HtmlEncode(bodyClass)).Append('"');
            sb.Append(" style=\"--md-font-size:").Append(fontSize.ToString(CultureInfo.InvariantCulture)).Append("px;");
            sb.Append("--content-width:").Append(widthValue).Append(';');

            // 字体只在用户真的选过时才写：留空就让 css 的默认栈和「标题跟随正文」的继承规则生效。
            // style 属性用双引号包着，所以字体名一律用单引号，避免把属性截断。
            string bodyFont = PageOptions.FontStack(options.FontBody,
                "'Microsoft YaHei UI', 'Microsoft YaHei', -apple-system, 'Segoe UI', sans-serif");
            if (bodyFont != null) sb.Append("--font-body:").Append(bodyFont).Append(';');

            string codeFont = PageOptions.FontStack(options.FontCode,
                "Consolas, 'Cascadia Mono', 'Cascadia Code', monospace");
            if (codeFont != null) sb.Append("--font-code:").Append(codeFont).Append(';');

            string headingFont = PageOptions.FontStack(options.FontHeading, null);
            if (headingFont != null) sb.Append("--font-heading:").Append(headingFont).Append(';');

            sb.Append("\">\r\n");
            sb.Append("<div class=\"markdown-body\">\r\n").Append(doc.BodyHtml).Append("\r\n</div>\r\n");

            // 外部库先加载，app.js 在 DOMContentLoaded 里才用得上它们的全局变量
            if (options.IncludeMath && vendorBase != null)
                sb.Append("<script src=\"").Append(WebUtility.HtmlEncode(vendorBase))
                  .Append("katex/katex.min.js\"></script>\r\n");
            if (options.IncludeMermaid && vendorBase != null)
                sb.Append("<script src=\"").Append(WebUtility.HtmlEncode(vendorBase))
                  .Append("mermaid/mermaid.min.js\"></script>\r\n");
            if (options.IncludeCodeHighlight && vendorBase != null)
                sb.Append("<script src=\"").Append(WebUtility.HtmlEncode(vendorBase))
                  .Append("hljs/highlight.min.js\"></script>\r\n");

            sb.Append("<script nonce=\"").Append(nonce).Append("\">\r\n").Append(Assets.AppJs).Append("\r\n</script>\r\n");
            sb.Append("</body>\r\n</html>\r\n");
            return sb.ToString();
        }

        /// <summary>md 的纯文本版本（WebView2 缺失时降级显示用）。</summary>
        public static string ToPlainText(string markdown)
        {
            string html;
            try
            {
                html = Markdown.ToHtml(markdown ?? string.Empty, Pipeline);
            }
            catch (Exception)
            {
                return (markdown ?? string.Empty).Trim();
            }
            string text = HtmlSanitizer.ToPlainText(html);
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            while (text.Contains("\n\n\n")) text = text.Replace("\n\n\n", "\n\n");
            return text.Trim();
        }
    }
}
