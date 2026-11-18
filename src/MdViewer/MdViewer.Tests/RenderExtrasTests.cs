using System;
using System.IO;
using MdViewer.Core;

namespace MdViewer.Tests
{
    /// <summary>0.5.0 新增的渲染分支：数学公式、Mermaid、代码高亮、front matter 信息卡。</summary>
    public class RenderExtrasTests
    {
        [Fact]
        public void 行内公式被识别成math节点()
        {
            RenderedMarkdown doc = MarkdownRenderer.Render("质能方程 $E = mc^2$ 就这样。");

            Assert.True(doc.HasMath);
            Assert.Contains("class=\"math\"", doc.BodyHtml);
            Assert.Contains("\\(E = mc^2\\)", doc.BodyHtml);
        }

        [Fact]
        public void 块级公式用反斜杠方括号包起来()
        {
            RenderedMarkdown doc = MarkdownRenderer.Render("$$\na^2 + b^2 = c^2\n$$");

            Assert.True(doc.HasMath);
            // Markdig 会把块级公式按行拆开包进 div.math
            Assert.Contains("class=\"math\"", doc.BodyHtml);
            Assert.Contains("\\[", doc.BodyHtml);
            Assert.Contains("\\]", doc.BodyHtml);
            Assert.Contains("a^2 + b^2 = c^2", doc.BodyHtml);
        }

        [Fact]
        public void 公式里的上标不会被当成markdown上标吃掉()
        {
            // UseMathematics 必须排在 UseEmphasisExtras 前面，否则 $a^2$ 的 ^ 先被上标语法吃走
            RenderedMarkdown doc = MarkdownRenderer.Render("$x^2$");

            Assert.Contains("\\(x^2\\)", doc.BodyHtml);
            Assert.DoesNotContain("<sup>", doc.BodyHtml);
        }

        [Fact]
        public void 普通文字里没有美元素断言()
        {
            RenderedMarkdown doc = MarkdownRenderer.Render("就是一段普通的话。");

            Assert.False(doc.HasMath);
            Assert.False(doc.HasMermaid);
        }

        [Fact]
        public void mermaid代码块被标记出来()
        {
            RenderedMarkdown doc = MarkdownRenderer.Render("```mermaid\ngraph LR\n  A --> B\n```");

            Assert.True(doc.HasMermaid);
            Assert.True(doc.HasCodeBlock);
            Assert.Contains("language-mermaid", doc.BodyHtml);
        }

        [Fact]
        public void 普通代码块只提高亮开关不引mermaid()
        {
            RenderedMarkdown doc = MarkdownRenderer.Render("```csharp\nvar x = 1;\n```");

            Assert.True(doc.HasCodeBlock);
            Assert.False(doc.HasMermaid);
        }

        [Fact]
        public void 需要时才把vendor脚本写进页面()
        {
            var options = new PageOptions
            {
                Title = "t",
                ThemeId = "github-light",
                FontSize = 16,
                VendorBase = "https://mdviewer.tmp/vendor/",
                IncludeMath = true,
                IncludeMermaid = false,
                IncludeCodeHighlight = true
            };

            string page = MarkdownRenderer.BuildPage(
                MarkdownRenderer.Render("$x$ 和 ```js\na\n```"), options);

            Assert.Contains("katex.min.js", page);
            Assert.Contains("katex.min.css", page);
            Assert.Contains("highlight.min.js", page);
            Assert.DoesNotContain("mermaid.min.js", page);
        }

        [Fact]
        public void 没有公式和代码时不引任何vendor脚本()
        {
            var options = new PageOptions
            {
                Title = "t",
                ThemeId = "github-light",
                FontSize = 16,
                VendorBase = "https://mdviewer.tmp/vendor/"
            };

            string page = MarkdownRenderer.BuildPage(MarkdownRenderer.Render("# 只有标题"), options);

            // style.css 里本来就有 .katex-display 之类的规则，所以只能查"有没有真的引脚本"
            Assert.DoesNotContain("katex/katex.min.js", page);
            Assert.DoesNotContain("katex/katex.min.css", page);
            Assert.DoesNotContain("mermaid/mermaid.min.js", page);
            Assert.DoesNotContain("hljs/highlight.min.js", page);
        }

        [Fact]
        public void 字体只写用户真选过的那些()
        {
            var options = new PageOptions
            {
                Title = "t",
                ThemeId = "github-light",
                FontSize = 16,
                FontBody = "LXGW WenKai",
                FontCode = string.Empty,
                FontHeading = string.Empty
            };

            string page = MarkdownRenderer.BuildPage(MarkdownRenderer.Render("正文"), options);

            // 只查 <body> 那一段的内联 style，别被 style.css 里的注释和规则误导
            int bodyStart = page.IndexOf("<body", StringComparison.Ordinal);
            int tagEnd = page.IndexOf("\">", bodyStart, StringComparison.Ordinal);
            Assert.True(bodyStart >= 0 && tagEnd > bodyStart);
            string bodyTag = page.Substring(bodyStart, tagEnd - bodyStart);

            Assert.Contains("--font-body:", bodyTag);
            Assert.Contains("LXGW WenKai", bodyTag);
            Assert.DoesNotContain("--font-code:", bodyTag);
            Assert.DoesNotContain("--font-heading:", bodyTag);
        }

        [Fact]
        public void 自定义css排在自带样式之后()
        {
            var options = new PageOptions
            {
                Title = "t",
                ThemeId = "github-light",
                FontSize = 16,
                CustomCss = ".x{color:red}"
            };

            string page = MarkdownRenderer.BuildPage(MarkdownRenderer.Render("正文"), options);

            int custom = page.IndexOf(".x{color:red}", StringComparison.Ordinal);
            int builtin = page.IndexOf("</style>", StringComparison.Ordinal);
            Assert.True(custom >= 0);
            Assert.True(builtin >= 0);
            Assert.True(custom > builtin, "自定义皮肤必须排在自带样式后面，否则盖不住");
        }

        [Fact]
        public void 公式的字体和样式来源进了csp()
        {
            var options = new PageOptions
            {
                Title = "t",
                ThemeId = "github-light",
                FontSize = 16,
                ContentOrigin = "https://mdviewer.tmp",
                VendorBase = "https://mdviewer.tmp/vendor/",
                IncludeMath = true
            };

            string page = MarkdownRenderer.BuildPage(MarkdownRenderer.Render("$x$"), options);

            // KaTeX 的 woff2 是本地虚拟域名下的，font-src 得放行
            Assert.Contains("font-src", page);
            Assert.Contains("mdviewer.tmp", page);
            Assert.Contains("nonce-", page);
        }
    }
}
