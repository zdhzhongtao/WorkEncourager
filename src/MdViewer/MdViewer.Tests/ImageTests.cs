using System;
using System.IO;
using MdViewer.Core;

namespace MdViewer.Tests
{
    public class ImageTests
    {
        [Fact]
        public void 当前目录下的相对路径能算出绝对路径()
        {
            string baseDir = @"C:\docs\guide";
            string full = ImagePathResolver.Resolve("./img/a.png", baseDir);
            Assert.Equal(@"C:\docs\guide\img\a.png", full);
        }

        [Fact]
        public void 上级目录的相对路径能算出绝对路径()
        {
            string baseDir = @"C:\docs\guide";
            string full = ImagePathResolver.Resolve("../b.png", baseDir);
            Assert.Equal(@"C:\docs\b.png", full);
        }

        [Fact]
        public void 绝对url不被改写()
        {
            Assert.True(ImagePathResolver.IsRemote("https://example.com/a.png"));
            Assert.Null(ImagePathResolver.Resolve("https://example.com/a.png", @"C:\docs"));
            Assert.Equal("https://example.com/a.png", ImagePathResolver.Normalize("https://example.com/a.png"));
        }

        [Fact]
        public void 反斜杠写法也能认出相对路径()
        {
            Assert.Equal("img/a.png", ImagePathResolver.Normalize(@".\img\a.png"));
            Assert.Equal("img/a.png", ImagePathResolver.ToVirtualRelative(@".\img\a.png", @"C:\docs"));
        }

        [Fact]
        public void 跳出根目录的返回空()
        {
            Assert.Null(ImagePathResolver.ToVirtualRelative("../b.png", @"C:\docs\guide"));
            Assert.True(ImagePathResolver.IsUnderRoot(@"C:\docs\guide\x.png", @"C:\docs\guide"));
            Assert.False(ImagePathResolver.IsUnderRoot(@"C:\docs\other\x.png", @"C:\docs\guide"));
        }

        [Fact]
        public void 目录内图片改写成虚拟域名()
        {
            string html = "<p><img src=\"./img/a.png\" alt=\"图\" /></p>";
            string result = ImageRewriter.Rewrite(html, @"C:\docs\guide", "mdviewer.local");
            Assert.Contains("https://mdviewer.local/img/a.png", result);
            Assert.Contains("alt=\"图\"", result);
        }

        [Fact]
        public void 目录外图片内联成data_uri()
        {
            using (var dir = new TempDir())
            {
                string parent = Path.GetDirectoryName(dir.Path);
                string imagePath = Path.Combine(parent, "outside-" + Guid.NewGuid().ToString("N") + ".png");
                File.WriteAllBytes(imagePath, new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 });

                try
                {
                    string html = "<img src=\"../" + Path.GetFileName(imagePath) + "\">";
                    string result = ImageRewriter.Rewrite(html, dir.Path, "mdviewer.local");
                    Assert.Contains("data:image/png;base64,", result);
                }
                finally
                {
                    try { File.Delete(imagePath); } catch { }
                }
            }
        }

        [Fact]
        public void 远程图片保持原样()
        {
            string html = "<img src=\"https://example.com/a.png\">";
            string result = ImageRewriter.Rewrite(html, @"C:\docs", "mdviewer.local");
            Assert.Equal(html, result);
        }

        [Fact]
        public void 路径里的空格被转义()
        {
            string html = "<img src=\"./my pics/a.png\">";
            string result = ImageRewriter.Rewrite(html, @"C:\docs", "mdviewer.local");
            Assert.Contains("https://mdviewer.local/my%20pics/a.png", result);
        }

        [Fact]
        public void mime类型对得上()
        {
            Assert.Equal("image/png", ImageRewriter.MimeOf(".PNG"));
            Assert.Equal("image/jpeg", ImageRewriter.MimeOf(".jpg"));
            Assert.Equal("image/svg+xml", ImageRewriter.MimeOf(".svg"));
        }

        [Fact]
        public void 相对链接改写成虚拟域名()
        {
            string html = "<p><a href=\"./other.md\">另一篇</a></p>";
            string result = ImageRewriter.RewriteLinks(html, @"C:\docs", "md1.local");
            Assert.Contains("href=\"https://md1.local/other.md\"", result);
        }

        [Fact]
        public void 锚点链接和远程链接不改写()
        {
            string html = "<a href=\"#表格\">锚点</a><a href=\"https://example.com/x\">外链</a>";
            string result = ImageRewriter.RewriteLinks(html, @"C:\docs", "md1.local");
            Assert.Contains("href=\"#表格\"", result);
            Assert.Contains("href=\"https://example.com/x\"", result);
        }

        [Fact]
        public void 导出时路径只做斜杠规整()
        {
            string html = "<img src=\".\\img\\a.png\">";
            string result = ImageRewriter.NormalizeRelative(html);
            Assert.Contains("src=\"img/a.png\"", result);
        }
    }
}
