using System;
using MdViewer.Core;

namespace MdViewer.Tests
{
    public class SanitizeTests
    {
        [Fact]
        public void script标签被剥掉()
        {
            string html = MarkdownRenderer.RenderBody("正常文字\n\n<script>alert(1)</script>");
            Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("alert(1)", html);
            Assert.Contains("正常文字", html);
        }

        [Fact]
        public void 自闭合script也被剥掉()
        {
            string clean = HtmlSanitizer.Clean("<p>a</p><script src=\"x.js\" />");
            Assert.DoesNotContain("<script", clean, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void onclick等事件属性被剥掉()
        {
            string clean = HtmlSanitizer.Clean("<p onclick=\"alert(1)\">点我</p>");
            Assert.DoesNotContain("onclick", clean, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("点我", clean);

            string single = HtmlSanitizer.Clean("<p onmouseover='x()'>悬停</p>");
            Assert.DoesNotContain("onmouseover", single, StringComparison.OrdinalIgnoreCase);

            string bare = HtmlSanitizer.Clean("<p onload=boom>裸值</p>");
            Assert.DoesNotContain("onload", bare, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void javascript链接被换成井号()
        {
            string clean = HtmlSanitizer.Clean("<a href=\"javascript:alert(1)\">点</a>");
            Assert.DoesNotContain("javascript:", clean, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"#\"", clean);

            string single = HtmlSanitizer.Clean("<a href='javascript:alert(1)'>点</a>");
            Assert.DoesNotContain("javascript:", single, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void iframe被剥掉()
        {
            string clean = HtmlSanitizer.Clean("<p>前</p><iframe src=\"https://evil\"></iframe><p>后</p>");
            Assert.DoesNotContain("<iframe", clean, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("前", clean);
            Assert.Contains("后", clean);
        }

        [Fact]
        public void object与embed被剥掉()
        {
            string clean = HtmlSanitizer.Clean("<object data=\"x.swf\"></object><embed src=\"y.swf\">");
            Assert.DoesNotContain("<object", clean, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<embed", clean, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void 普通html标签保留()
        {
            string clean = HtmlSanitizer.Clean("<p><strong>粗</strong>与<em>斜</em></p>");
            Assert.Contains("<strong>粗</strong>", clean);
            Assert.Contains("<em>斜</em>", clean);
        }

        [Fact]
        public void style标签整块被剥掉()
        {
            string clean = HtmlSanitizer.Clean("<p>前</p><style>body{display:none}</style><p>后</p>");

            Assert.DoesNotContain("<style", clean, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("display:none", clean);
            Assert.Contains("前", clean);
            Assert.Contains("后", clean);
        }

        [Fact]
        public void 没闭合的style开标签也被剥掉()
        {
            string clean = HtmlSanitizer.Clean("<style>p{color:red}<p>正文</p>");

            Assert.DoesNotContain("<style", clean, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("正文", clean);
        }

        [Fact]
        public void link与meta与base被剥掉()
        {
            string clean = HtmlSanitizer.Clean(
                "<link rel=\"stylesheet\" href=\"https://evil/a.css\">"
                + "<meta http-equiv=\"refresh\" content=\"0;url=https://evil\">"
                + "<base href=\"https://evil/\">"
                + "<p>正文</p>");

            Assert.DoesNotContain("<link", clean, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<meta", clean, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<base", clean, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("evil", clean);
            Assert.Contains("正文", clean);
        }

        [Fact]
        public void md里的style不会污染正文()
        {
            string body = MarkdownRenderer.RenderBody("# 标题\n\n<style>.markdown-body{display:none}</style>\n\n正文");

            Assert.DoesNotContain("display:none", body);
            Assert.Contains("正文", body);
        }
    }
}
