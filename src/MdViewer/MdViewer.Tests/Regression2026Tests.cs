using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MdViewer.Core;

namespace MdViewer.Tests
{
    /// <summary>2026-11-28 评审报告里修掉的问题的回归测试。</summary>
    public class Regression2026Tests
    {
        // ───────────────────────── 编码 ─────────────────────────

        [Fact]
        public void GBK保存的文档能正确解码()
        {
            // “中文” 的 GBK 字节
            byte[] gbk = { 0xD6, 0xD0, 0xCE, 0xC4 };

            string text = TextLoader.Decode(gbk);

            Assert.Equal("中文", text);
        }

        [Fact]
        public void GB18030代码页是54936而不是18030()
        {
            Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            Assert.Throws<NotSupportedException>(() => Encoding.GetEncoding(18030));
            Assert.NotNull(Encoding.GetEncoding(54936));
        }

        // ───────────────────────── 清洗器 ─────────────────────────

        [Fact]
        public void 正文里提到事件属性不会被删字()
        {
            string clean = HtmlSanitizer.Clean("<p>属性 onerror=\"alert(1)\" 是危险的</p>");

            Assert.Contains("onerror=\"alert(1)\"", clean);
            Assert.Contains("是危险的", clean);
        }

        [Fact]
        public void 代码块里的HTML示例不会被削掉()
        {
            string html = MarkdownRenderer.RenderBody("```html\n<img src=x onerror=alert(1)>\n```");

            Assert.Contains("onerror=alert(1)", html);
        }

        [Theory]
        [InlineData("<img src=\"x\"onerror=alert(1)>")]
        [InlineData("<img src='x'onerror=alert(1)>")]
        [InlineData("<img src=x/onerror=alert(1)>")]
        [InlineData("<div class=\"a\"onclick=alert(1)>x</div>")]
        public void 属性值后没有空格的事件属性也要删掉(string html)
        {
            string clean = HtmlSanitizer.Clean(html);

            Assert.DoesNotContain("onerror", clean, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("onclick", clean, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void 能盖住界面的内联样式被剥掉而排版样式保留()
        {
            string clean = HtmlSanitizer.Clean(
                "<div style=\"position:fixed;top:0;left:0;width:100%;height:100%;z-index:9;background:#fff;text-align:left\">x</div>");

            Assert.DoesNotContain("position:", clean);
            Assert.DoesNotContain("z-index", clean);
            Assert.Contains("text-align: left", clean);
        }

        [Fact]
        public void 表单控件被删掉()
        {
            string clean = HtmlSanitizer.Clean("<form action=\"https://evil\"><input type=\"password\"></form>正文");

            Assert.DoesNotContain("<form", clean, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<input", clean, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("正文", clean);
        }

        [Fact]
        public void 未闭合的注释不会吞掉后文()
        {
            string clean = HtmlSanitizer.Clean("<p>前</p><!-- 只写了一半");

            Assert.Contains("前", clean);
            Assert.DoesNotContain("<!--", clean);

            string body = MarkdownRenderer.RenderBody("前\n\n<!-- 只写了一半\n\n# 标题\n\n后面还有正文");
            Assert.Contains("后面还有正文", body);
        }

        [Fact]
        public void 表格对齐样式保留()
        {
            string clean = HtmlSanitizer.Clean("<table><tr><th style=\"text-align: left;\">a</th></tr></table>");

            Assert.Contains("style=\"text-align: left", clean);
        }

        [Fact]
        public void 不带引号的遮罩样式也会被剥掉()
        {
            string clean = HtmlSanitizer.Clean(
                "<div style=position:fixed;top:0;left:0;width:100%;height:100%;z-index:9;background:#fff>遮罩</div>");

            Assert.DoesNotContain("position:", clean, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("z-index", clean, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("遮罩", clean);
        }

        [Fact]
        public void 大量未闭合script不会让清洗卡住()
        {
            var payload = new StringBuilder();
            for (int i = 0; i < 4000; i++) payload.Append("<script");
            payload.Append("正文");

            var watch = System.Diagnostics.Stopwatch.StartNew();
            string clean = HtmlSanitizer.Clean(payload.ToString());
            watch.Stop();

            // 这些 `<script` 都没闭合，按 HTML 的规则后面会被当标签丢掉；这里只验证不卡死
            Assert.True(watch.ElapsedMilliseconds < 3000, "清洗耗时 " + watch.ElapsedMilliseconds + "ms");
            Assert.NotNull(clean);
        }

        [Fact]
        public void 没有BOM的UTF16也能读()
        {
            byte[] bytes = Encoding.Unicode.GetBytes("# 标题\r\n正文");

            string text = TextLoader.Decode(bytes);

            Assert.Contains("标题", text);
            Assert.Contains("正文", text);
        }

        [Fact]
        public void 并发写同一个文件不会留下坏内容()
        {
            using (var dir = new TempDir())
            {
                string path = Path.Combine(dir.Path, "data.json");
                var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
                System.Threading.Tasks.Parallel.For(0, 200, i =>
                {
                    try { AtomicFile.WriteAllText(path, "{\"id\":" + i + "}"); }
                    catch (Exception ex) { errors.Add(ex); }
                });

                Assert.Empty(errors);
                string text = File.ReadAllText(path);
                Assert.StartsWith("{\"id\":", text);
                Assert.EndsWith("}", text.Trim());
            }
        }
        // ───────────────────────── front matter ─────────────────────────

        [Fact]
        public void 开头画分隔线不再被当成frontmatter吞掉()
        {
            string body = MarkdownRenderer.RenderBody("---\n\n第一段\n\n---\n\n第二段");

            Assert.Contains("第一段", body);
            Assert.Contains("第二段", body);
        }

        [Fact]
        public void 真正的frontmatter仍然识别()
        {
            FrontMatter fm;
            string body = FrontMatter.Split("---\ntitle: T\n---\n正文", out fm);

            Assert.True(fm.Found);
            Assert.Equal("T", fm.Title);
            Assert.Contains("正文", body);
        }

        // ───────────────────────── Markdown 兜底 ─────────────────────────

        [Fact]
        public void 深层嵌套不再让文档打不开()
        {
            string deep = new string('>', 220) + " 文本";
            RenderedMarkdown doc = MarkdownRenderer.Render(deep);

            Assert.NotNull(doc.Error);
            Assert.Contains("纯文本", doc.BodyHtml);
            Assert.Contains("文本", doc.BodyHtml);
        }

        // ───────────────────────── 图片 / UNC ─────────────────────────

        [Fact]
        public void UNC路径按远程处理不会去探测网络()
        {
            Assert.True(ImagePathResolver.IsRemote(@"\\10.255.255.1\share\x.png"));
            Assert.True(ImagePathResolver.IsRemote(@"\\server\share\x.png"));
            Assert.Null(ImagePathResolver.Resolve(@"\\server\share\x.png", @"C:\docs"));
        }

        // ───────────────────────── 设置 ─────────────────────────

        [Fact]
        public void 非法的窗口尺寸会被收敛()
        {
            var settings = new AppSettings
            {
                WindowWidth = double.NaN,
                WindowHeight = double.PositiveInfinity,
                WindowLeft = double.NaN,
                WindowTop = 999999,
                TocWidth = -30
            };

            SettingsStore.Normalize(settings);

            Assert.Equal(1200, settings.WindowWidth);
            Assert.Equal(820, settings.WindowHeight);
            Assert.Equal(-1, settings.WindowLeft);
            Assert.Equal(-1, settings.WindowTop);
            Assert.Equal(240, settings.TocWidth);
        }

        [Fact]
        public void 标签列表按大小写去重并收敛成完整路径()
        {
            var settings = new AppSettings
            {
                OpenTabs = new List<string> { @"c:\x.md", @"C:\X.md", @"c:\y.md" }
            };

            SettingsStore.Normalize(settings);

            Assert.Equal(2, settings.OpenTabs.Count);
        }

        [Fact]
        public void 滚动比例里的坏值被清掉()
        {
            var settings = new AppSettings();
            settings.ScrollPositions[@"c:\a.md"] = double.NaN;
            settings.ScrollPositions[@"c:\b.md"] = 2.5;
            settings.ScrollPositions[@"c:\c.md"] = 0.5;

            SettingsStore.Normalize(settings);

            Assert.Single(settings.ScrollPositions);
            Assert.True(settings.ScrollPositions.ContainsKey(@"c:\c.md"));
        }

        // ───────────────────────── 更新 ─────────────────────────

        [Fact]
        public void 预发布版本不会被当成正式更新()
        {
            const string xml = @"<feed xmlns=""http://www.w3.org/2005/Atom"">
  <entry><id>tag:github.com,2008:Repository/1/mdviewer-v0.6.0-rc.1</id><title>mdviewer-v0.6.0-rc.1</title></entry>
  <entry><id>tag:github.com,2008:Repository/1/mdviewer-v0.5.3</id><title>mdviewer-v0.5.3</title></entry>
</feed>";

            UpdateResult result = UpdateChecker.PickFromAtom(xml, "z/W", "0.5.2", "MdViewer");

            Assert.Null(result.Error);
            Assert.Equal("0.5.3", result.Latest);
            Assert.Equal("mdviewer-v0.5.3", result.Tag);
        }

        // ───────────────────────── 命令行 ─────────────────────────

        [Fact]
        public void 单个短横线的help也能认()
        {
            Assert.True(CommandLine.Parse(new[] { "-h" }).Help);
            Assert.True(CommandLine.Parse(new[] { "-help" }).Help);
            Assert.True(CommandLine.Parse(new[] { "-demo" }).Demo);
        }

        // ───────────────────────── 缓存 ─────────────────────────

        [Fact]
        public void 孤儿的bak文件会被清掉()
        {
            using (var dir = new TempDir())
            {
                File.WriteAllText(Path.Combine(dir.Path, "view-1.html"), "a");
                File.WriteAllText(Path.Combine(dir.Path, "view-1.html.bak"), "a");
                File.WriteAllText(Path.Combine(dir.Path, "view-2.html.bak"), "b");

                CacheStore.Trim(dir.Path, keep: 20);

                Assert.True(File.Exists(Path.Combine(dir.Path, "view-1.html")));
                Assert.True(File.Exists(Path.Combine(dir.Path, "view-1.html.bak")));
                Assert.False(File.Exists(Path.Combine(dir.Path, "view-2.html.bak")));
            }
        }

        // ───────────────────────── 笔记 ─────────────────────────

        [Fact]
        public void notes里的null条目不会让读取崩溃()
        {
            using (var dir = new TempDir())
            {
                string path = Path.Combine(dir.Path, "notes.json");
                File.WriteAllText(path,
                    "{\"Version\":1,\"Annotations\":[null,{"
                    + "\"Id\":\"a\",\"Path\":\"C:\\\\a.md\",\"Start\":0,\"End\":1,\"Quote\":\"x\""
                    + "}],\"Bookmarks\":[null]}");

                NoteBook book = NoteStore.Load(dir.Path);

                Assert.Single(book.Annotations);
                Assert.Empty(book.Bookmarks);
                Assert.Single(NoteStore.AnnotationsFor(book, @"C:\a.md"));
            }
        }

        [Fact]
        public void 路径里的点点会被收敛()
        {
            Assert.True(NoteStore.SamePath(@"C:\docs\..\a.md", @"C:\a.md"));
        }

        // ───────────────────────── 标题 id ─────────────────────────

        [Fact]
        public void 重复的显式id会被改写成不同值()
        {
            HeadingScan scan = HeadingAnchors.Process("<h2 id=\"dup\">甲</h2><h2 id=\"dup\">乙</h2>");

            Assert.Contains("id=\"dup\"", scan.Html);
            Assert.Contains("id=\"dup-1\"", scan.Html);
            Assert.Equal("dup", scan.Toc[0].Id);
            Assert.Equal("dup-1", scan.Toc[1].Id);
        }
    }
}
