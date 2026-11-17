using System;
using MdViewer.Core;

namespace MdViewer.Tests
{
    /// <summary>自检发现的安全边界：实体编码 / 空白混淆的伪协议要拦住。</summary>
    public class SanitizeRegressionTests
    {
        [Theory]
        [InlineData("<a href=\"jav&#x61;script:alert(1)\">x</a>")]
        [InlineData("<a href=\"javascript&colon;alert(1)\">x</a>")]
        [InlineData("<a href=\"java&#9;script:alert(1)\">x</a>")]
        [InlineData("<a href=\"  JaVaScRiPt:alert(1)\">x</a>")]
        [InlineData("<a href=\"vbscript:msgbox(1)\">x</a>")]
        [InlineData("<img src=\"jav&#x61;script:alert(1)\">")]
        public void 伪协议的各种伪装都要被换成井号(string html)
        {
            string clean = HtmlSanitizer.Clean(html);

            Assert.DoesNotContain("script:", clean, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"#\"", clean.Replace("src=", "href="));
        }

        [Fact]
        public void data开头的html链接也要拦()
        {
            string clean = HtmlSanitizer.Clean("<a href=\"data:text/html;base64,PHNjcmlwdD4=\">x</a>");
            Assert.DoesNotContain("data:text/html", clean, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void 正常链接和锚点不能被误伤()
        {
            string clean = HtmlSanitizer.Clean(
                "<a href=\"https://example.com/a?b=1\">外链</a><a href=\"#第二节\">锚点</a><a href=\"docs/a.md\">文档</a>");

            Assert.Contains("https://example.com/a?b=1", clean);
            Assert.Contains("#第二节", clean);
            Assert.Contains("docs/a.md", clean);
        }

        [Fact]
        public void 剥掉脚本后不留下裸尖括号()
        {
            string clean = HtmlSanitizer.Clean("<p>a</p><script>alert(1)</script><p>b</p>");
            Assert.Equal("<p>a</p><p>b</p>", clean);
        }
    }
}
