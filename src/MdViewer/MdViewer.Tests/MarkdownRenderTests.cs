using System;
using System.Collections.Generic;
using MdViewer.Core;

namespace MdViewer.Tests
{
    /// <summary>测试用的临时目录，用完删干净。</summary>
    internal sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MdViewerTests",
                Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Path);
        }

        public string File(string name, string text)
        {
            string full = System.IO.Path.Combine(Path, name);
            string dir = System.IO.Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(full, text);
            return full;
        }

        public string File(string name, byte[] bytes)
        {
            string full = System.IO.Path.Combine(Path, name);
            string dir = System.IO.Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllBytes(full, bytes);
            return full;
        }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Path, true); } catch { /* 测试目录删不掉就算了 */ }
        }
    }

    public class MarkdownRenderTests
    {
        private static string Body(string markdown)
        {
            return MarkdownRenderer.RenderBody(markdown);
        }

        [Fact]
        public void 六级标题都能渲染成h标签()
        {
            string html = Body("# 一\n\n## 二\n\n### 三\n\n#### 四\n\n##### 五\n\n###### 六");
            for (int level = 1; level <= 6; level++)
                Assert.Contains("<h" + level + " id=", html);
            Assert.Contains("</h6>", html);
        }

        [Fact]
        public void 段落包在p标签里()
        {
            Assert.Contains("<p>就是一段话。</p>", Body("就是一段话。"));
        }

        [Fact]
        public void 加粗与斜体()
        {
            string html = Body("**粗** 和 *斜*");
            Assert.Contains("<strong>粗</strong>", html);
            Assert.Contains("<em>斜</em>", html);
        }

        [Fact]
        public void 删除线()
        {
            Assert.Contains("<del>过期</del>", Body("~~过期~~"));
        }

        [Fact]
        public void 行内代码()
        {
            string html = Body("运行 `dotnet build` 即可");
            Assert.Contains("<code>dotnet build</code>", html);
        }

        [Fact]
        public void 围栏代码块带语言()
        {
            string html = Body("```csharp\nvar a = 1;\n```");
            Assert.Contains("<pre>", html);
            Assert.Contains("language-csharp", html);
            Assert.Contains("var a = 1;", html);
        }

        [Fact]
        public void 围栏代码块无语言也不报错()
        {
            string html = Body("```\n纯文本\n```");
            Assert.Contains("<pre>", html);
            Assert.Contains("纯文本", html);
        }

        [Fact]
        public void 有序列表()
        {
            string html = Body("1. 甲\n2. 乙");
            Assert.Contains("<ol>", html);
            Assert.Contains("<li>甲</li>", html);
        }

        [Fact]
        public void 无序列表()
        {
            string html = Body("- 甲\n- 乙");
            Assert.Contains("<ul>", html);
            Assert.Contains("<li>乙</li>", html);
        }

        [Fact]
        public void 嵌套列表()
        {
            string html = Body("1. 甲\n   - 甲一\n   - 甲二\n2. 乙");
            Assert.Contains("<ol>", html);
            // 嵌套一层就是两个 <ul>
            int count = 0;
            int index = 0;
            while ((index = html.IndexOf("<ul>", index, StringComparison.Ordinal)) >= 0) { count++; index += 4; }
            Assert.True(count >= 1, "嵌套列表里应该有内层 ul");
            Assert.Contains("甲一", html);
        }

        [Fact]
        public void 表格()
        {
            string html = Body("| 名称 | 金额 |\n|:--|--:|\n| 甲 | 12.50 |");
            Assert.Contains("<table>", html);
            Assert.Contains("<th", html);
            Assert.Contains("甲", html);
            Assert.Contains("12.50", html);
        }

        [Fact]
        public void 表格对齐方式被保留()
        {
            string html = Body("| 左 | 中 | 右 |\n|:--|:-:|--:|\n| a | b | c |");
            Assert.Contains("text-align: left", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("text-align: center", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("text-align: right", html, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void 任务列表换成方框字形()
        {
            string html = Body("- [x] 已完成\n- [ ] 未完成");
            Assert.Contains("task-box done", html);
            Assert.Contains("\u2611", html);
            Assert.Contains("task-box todo", html);
            Assert.Contains("\u2610", html);
            Assert.DoesNotContain("type=\"checkbox\"", html);
        }

        [Fact]
        public void 引用块()
        {
            string html = Body("> 引一句\n> 再引一句");
            Assert.Contains("<blockquote>", html);
            Assert.Contains("引一句", html);
        }

        [Fact]
        public void 脚注()
        {
            string html = Body("正文有脚注[^1]。\n\n[^1]: 脚注内容。");
            Assert.Contains("footnote", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("脚注内容", html);
        }

        [Fact]
        public void 自动链接()
        {
            string html = Body("看这个 https://example.com/x 地址");
            Assert.Contains("<a href=\"https://example.com/x\"", html);
        }

        [Fact]
        public void 中文与emoji原样保留()
        {
            string html = Body("# 中文标题 😀🎉\n\n正文也有 emoji 🚀");
            Assert.Contains("中文标题", html);
            Assert.Contains("😀", html);
            Assert.Contains("🚀", html);
        }

        [Fact]
        public void HTML特殊字符被转义()
        {
            string html = Body("条件是 a < b & c > d");
            Assert.Contains("&lt;", html);
            Assert.Contains("&amp;", html);
            Assert.DoesNotContain("<b &", html);
        }

        [Fact]
        public void 整页包含样式与脚本且无远程引用()
        {
            var doc = MarkdownRenderer.Render("# 标题\n\n正文");
            string page = MarkdownRenderer.BuildPage(doc, new PageOptions
            {
                Title = "测试.md",
                DarkTheme = true,
                FontSize = 18,
                ContentOrigin = "https://md1.local"
            });

            Assert.StartsWith("<!DOCTYPE html>", page);
            Assert.Contains("<title>测试.md</title>", page);
            Assert.Contains("<style>", page);
            Assert.Contains("<script nonce=", page);
            Assert.Contains("data-theme=\"github-dark\"", page);
            Assert.Contains("data-mode=\"dark\"", page);
            Assert.Contains("--md-font-size:18px", page);
            Assert.Contains("Content-Security-Policy", page);
            Assert.Contains("img-src https://md1.local data:", page);
            // 内联脚本靠 nonce 放行，不再开 'unsafe-inline'
            Assert.Contains("script-src 'nonce-", page);
            Assert.DoesNotContain("script-src 'unsafe-inline'", page);
            // 刻意不用 <base>，否则 md 里的 #锚点 会被拼成跨文档跳转
            Assert.DoesNotContain("<base", page, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("http://", page.Replace("https://md1.local", string.Empty));
            Assert.DoesNotContain("cdn.", page, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void 指定主题会写进页面属性()
        {
            var doc = MarkdownRenderer.Render("正文");
            string page = MarkdownRenderer.BuildPage(doc, new PageOptions { ThemeId = "nord" });

            Assert.Contains("data-theme=\"nord\"", page);
            Assert.Contains("data-mode=\"dark\"", page);
        }

        [Fact]
        public void 认不出的主题回落到默认浅色()
        {
            var doc = MarkdownRenderer.Render("正文");
            string page = MarkdownRenderer.BuildPage(doc, new PageOptions { ThemeId = "不存在的主题" });

            Assert.Contains("data-theme=\"github-light\"", page);
            Assert.Contains("data-mode=\"light\"", page);
        }

        [Fact]
        public void 导出页带导出标记且不锁图片()
        {
            var doc = MarkdownRenderer.Render("正文");
            string page = MarkdownRenderer.BuildPage(doc, new PageOptions
            {
                Title = "导出",
                ForExport = true,
                ContentOrigin = null
            });

            Assert.Contains("for-export", page);
            // 导出页给别人用浏览器打开，图片要能加载，所以不写 default-src 'none'
            Assert.DoesNotContain("default-src 'none'", page);
            // 但脚本仍然只认 nonce
            Assert.Contains("script-src 'nonce-", page);
        }

        [Fact]
        public void 宽度为零时输出none而不是0px()
        {
            var doc = MarkdownRenderer.Render("正文");
            string page = MarkdownRenderer.BuildPage(doc, new PageOptions { ContentWidth = 0 });

            Assert.Contains("--content-width:none", page);
            Assert.DoesNotContain("--content-width:0px", page);
        }

        [Fact]
        public void 宽屏页面是不限宽度加wide类()
        {
            var doc = MarkdownRenderer.Render("正文");
            string page = MarkdownRenderer.BuildPage(doc, new PageOptions
            {
                ContentWidth = 0,
                BodyClass = "wide"
            });

            Assert.Contains("--content-width:none", page);
            Assert.Contains("class=\"wide\"", page);
        }

        [Fact]
        public void 宽屏导出的html也铺满不锁在880()
        {
            var doc = MarkdownRenderer.Render("正文");
            string page = MarkdownRenderer.BuildPage(doc, new PageOptions
            {
                Title = "导出",
                ContentWidth = 0,
                BodyClass = "wide",
                ForExport = true
            });

            Assert.Contains("--content-width:none", page);
            // for-export 和 wide 得同时留在 class 上：前者藏阅读器外壳，后者收窄左右留白
            Assert.Contains("class=\"for-export wide\"", page);
        }

        [Fact]
        public void 表格不产生自己的滚动条只跟正文一起滚()
        {
            string css = Assets.StyleCss;

            // 表格不再套滚动容器：宽表靠单元格换行收进正文，整页只留一个滚动条
            Assert.Contains("min-width: 0", css);
            Assert.Contains("overflow-wrap: anywhere", css);
            Assert.DoesNotContain(".table-wrap { overflow-x: auto; }", css);
            Assert.DoesNotContain(".table-wrap.table-tall", css);

            // 打印时同样没有嵌套滚动条
            int printAt = css.IndexOf("@media print", StringComparison.Ordinal);
            Assert.True(printAt > 0, "样式里应该有 @media print 块");
            Assert.Contains(".table-wrap { overflow: visible; }", css.Substring(printAt));
        }

        [Fact]
        public void 纯文本降级去掉了标签()
        {
            string text = MarkdownRenderer.ToPlainText("# 标题\n\n- 甲\n- 乙");
            Assert.Contains("标题", text);
            Assert.Contains("甲", text);
            Assert.DoesNotContain("<", text);
        }

        [Fact]
        public void 空文档也能渲染()
        {
            var doc = MarkdownRenderer.Render(string.Empty);
            Assert.Empty(doc.Toc);
            Assert.Equal(string.Empty, doc.BodyHtml);
            string page = MarkdownRenderer.BuildPage(doc, null);
            Assert.Contains("<body", page);
        }
    }
}
