using System;
using System.IO;
using MdViewer.Core;

namespace MdViewer.Tests
{
    public class FrontMatterTests
    {
        [Fact]
        public void 开头的yaml被切走且正文完整()
        {
            string markdown = "---\ntitle: 标题\nauthor: 张三\n---\n\n# 正文\n\n内容\n";

            FrontMatter fm;
            string body = FrontMatter.Split(markdown, out fm);

            Assert.True(fm.Found);
            Assert.Equal("标题", fm.Title);
            Assert.Equal("张三", fm.Get("author"));
            Assert.StartsWith("# 正文", body.TrimStart());
            Assert.DoesNotContain("title:", body);
        }

        [Fact]
        public void 没有frontmatter时整篇当正文()
        {
            string markdown = "# 只有正文\n\n没有头部。";

            FrontMatter fm;
            string body = FrontMatter.Split(markdown, out fm);

            Assert.False(fm.Found);
            Assert.Equal(string.Empty, fm.Title);
            Assert.Contains("只有正文", body);
        }

        [Fact]
        public void 没闭合的横线不能吞掉正文()
        {
            // 有人就爱在开头画一条分隔线，后面再没有第二条。
            // 这种情况必须把整篇当正文，宁可少一张信息卡，也不能让内容消失。
            string markdown = "---\n\n# 标题\n\n正文段落\n";

            FrontMatter fm;
            string body = FrontMatter.Split(markdown, out fm);

            Assert.False(fm.Found);
            Assert.Contains("正文段落", body);
        }

        [Fact]
        public void 三种标签写法都能认出来()
        {
            FrontMatter inline;
            FrontMatter.Split("---\ntags: [a, b]\n---\nx", out inline);
            Assert.Equal(new[] { "a", "b" }, inline.Tags);

            FrontMatter block;
            FrontMatter.Split("---\ntags:\n  - x\n  - y\n---\nx", out block);
            Assert.Equal(new[] { "x", "y" }, block.Tags);

            FrontMatter plain;
            FrontMatter.Split("---\ntags: 甲, 乙\n---\nx", out plain);
            Assert.Equal(new[] { "甲", "乙" }, plain.Tags);
        }

        [Fact]
        public void 标签去重且去引号()
        {
            FrontMatter fm;
            FrontMatter.Split("---\ntags: [\"a\", a, 'b']\n---\nx", out fm);

            Assert.Equal(2, fm.Tags.Count);
            Assert.Contains("a", fm.Tags);
            Assert.Contains("b", fm.Tags);
        }

        [Fact]
        public void 信息卡只放读者关心的字段()
        {
            string markdown = "---\n"
                + "title: 说明文档\n"
                + "author: 李四\n"
                + "date: 2026-01-02\n"
                + "layout: post\n"
                + "permalink: /x/\n"
                + "tags: [md, 阅读]\n"
                + "---\n正文\n";

            FrontMatter fm;
            FrontMatter.Split(markdown, out fm);
            string html = fm.ToHtml();

            Assert.Contains("fm-title", html);
            Assert.Contains("说明文档", html);
            Assert.Contains("李四", html);
            Assert.Contains("2026-01-02", html);
            Assert.Contains("md", html);

            // Jekyll 那些字段不该出现在卡片上
            Assert.DoesNotContain("layout", html);
            Assert.DoesNotContain("permalink", html);
        }

        [Fact]
        public void 标题里的尖括号被转义()
        {
            FrontMatter fm;
            FrontMatter.Split("---\ntitle: <script>x</script>\n---\n正文", out fm);

            string html = fm.ToHtml();
            Assert.DoesNotContain("<script>", html);
            Assert.Contains("&lt;script&gt;", html);
        }

        [Fact]
        public void 空的frontmatter不生成卡片()
        {
            FrontMatter fm;
            FrontMatter.Split("---\n---\n正文", out fm);

            Assert.True(fm.Found);
            Assert.Equal(string.Empty, fm.ToHtml());
        }

        [Fact]
        public void 开头的空行不影响识别()
        {
            FrontMatter fm;
            string body = FrontMatter.Split("\n\n---\ntitle: T\n---\n正文", out fm);

            Assert.True(fm.Found);
            Assert.Equal("T", fm.Title);
            Assert.Contains("正文", body);
        }

        [Fact]
        public void 渲染器会把信息卡放到正文前面()
        {
            RenderedMarkdown doc = MarkdownRenderer.Render("---\ntitle: 头部\n---\n\n# 一级标题\n\n段落");

            Assert.True(doc.FrontMatter.Found);
            Assert.Equal("头部", doc.FrontMatter.Title);

            int card = doc.BodyHtml.IndexOf("class=\"frontmatter\"", StringComparison.Ordinal);
            int heading = doc.BodyHtml.IndexOf("<h1", StringComparison.Ordinal);
            Assert.True(card >= 0, "正文里没有信息卡");
            Assert.True(heading >= 0, "正文里没有一级标题");
            Assert.True(card < heading, "信息卡应该在正文最前面");
        }
    }
}
