using System.Collections.Generic;
using MdViewer.Core;

namespace MdViewer.Tests
{
    public class TocTests
    {
        private static List<TocItem> Toc(string markdown)
        {
            return MarkdownRenderer.Render(markdown).Toc;
        }

        [Fact]
        public void 只收h1到h3()
        {
            var toc = Toc("# 一\n\n## 二\n\n### 三\n\n#### 四\n\n##### 五\n\n###### 六");
            Assert.Equal(3, toc.Count);
            Assert.Equal(1, toc[0].Level);
            Assert.Equal(2, toc[1].Level);
            Assert.Equal(3, toc[2].Level);
        }

        [Fact]
        public void 层级与文字都对得上()
        {
            var toc = Toc("## 安装步骤\n\n### 第一步");
            Assert.Equal(2, toc.Count);
            Assert.Equal("安装步骤", toc[0].Text);
            Assert.Equal(3, toc[1].Level);
            Assert.Equal("第一步", toc[1].Text);
        }

        [Fact]
        public void 标题里的行内标记被去掉只留文字()
        {
            var toc = Toc("## 带 `代码` 和 **粗体** 的标题");
            Assert.Single(toc);
            Assert.Equal("带 代码 和 粗体 的标题", toc[0].Text);
        }

        [Fact]
        public void 重复标题的id唯一()
        {
            var toc = Toc("# 重复\n\n# 重复\n\n# 重复");
            Assert.Equal(3, toc.Count);
            Assert.Equal("重复", toc[0].Id);
            Assert.Equal("重复-1", toc[1].Id);
            Assert.Equal("重复-2", toc[2].Id);
            Assert.NotEqual(toc[0].Id, toc[1].Id);
        }

        [Fact]
        public void 中文标题生成非空id且不含空格()
        {
            var toc = Toc("# 环境准备与安装");
            Assert.Single(toc);
            Assert.False(string.IsNullOrWhiteSpace(toc[0].Id));
            Assert.DoesNotContain(" ", toc[0].Id);
        }

        [Fact]
        public void 没有标题时目录为空()
        {
            Assert.Empty(Toc("只有一段正文，没有任何标题。"));
            Assert.Empty(Toc(string.Empty));
        }

        [Fact]
        public void html里的标题带上了对应id()
        {
            string html = MarkdownRenderer.RenderBody("# 第一节\n\n## 第二节");
            var toc = MarkdownRenderer.Render("# 第一节\n\n## 第二节").Toc;
            Assert.Contains("id=\"" + toc[0].Id + "\"", html);
            Assert.Contains("id=\"" + toc[1].Id + "\"", html);
        }

        [Fact]
        public void slug处理标点与空格()
        {
            Assert.Equal("hello-world", HeadingAnchors.Slug("Hello, World!"));
            Assert.Equal("a-b", HeadingAnchors.Slug("  a / b  "));
            Assert.Equal("section", HeadingAnchors.Slug("！！！"));
        }

        [Fact]
        public void 标题里写了id时沿用原有id()
        {
            var scan = HeadingAnchors.Process("<h2 id=\"custom\">自定义</h2>");
            Assert.Single(scan.Toc);
            Assert.Equal("custom", scan.Toc[0].Id);
        }

        [Fact]
        public void 没有任何h标签时html原样返回()
        {
            var scan = HeadingAnchors.Process("<p>纯段落</p>");
            Assert.Equal("<p>纯段落</p>", scan.Html);
            Assert.Empty(scan.Toc);
        }
    }
}
