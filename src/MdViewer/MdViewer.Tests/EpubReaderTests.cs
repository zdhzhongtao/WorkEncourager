using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using MdViewer.Core;

namespace MdViewer.Tests
{
    public class EpubReaderTests
    {
        private const string ContainerXml =
            "<?xml version=\"1.0\"?>\r\n"
            + "<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">\r\n"
            + "  <rootfiles>\r\n"
            + "    <rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/>\r\n"
            + "  </rootfiles>\r\n"
            + "</container>";

        private const string Opf3 =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n"
            + "<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"bookid\">\r\n"
            + "  <metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">\r\n"
            + "    <dc:title>测试书</dc:title>\r\n"
            + "    <dc:creator>某人</dc:creator>\r\n"
            + "    <dc:language>zh-CN</dc:language>\r\n"
            + "  </metadata>\r\n"
            + "  <manifest>\r\n"
            + "    <item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>\r\n"
            + "    <item id=\"c1\" href=\"Text/ch1.xhtml\" media-type=\"application/xhtml+xml\"/>\r\n"
            + "    <item id=\"c2\" href=\"Text/ch2.xhtml\" media-type=\"application/xhtml+xml\"/>\r\n"
            + "    <item id=\"img1\" href=\"Images/pic.png\" media-type=\"image/png\"/>\r\n"
            + "  </manifest>\r\n"
            + "  <spine>\r\n"
            + "    <itemref idref=\"c1\"/>\r\n"
            + "    <itemref idref=\"c2\"/>\r\n"
            + "  </spine>\r\n"
            + "</package>";

        private const string Nav =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n"
            + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\r\n"
            + "<head><title>目录</title></head>\r\n"
            + "<body>\r\n"
            + "  <nav epub:type=\"toc\"><ol>\r\n"
            + "    <li><a href=\"Text/ch1.xhtml\">第一章 起</a></li>\r\n"
            + "    <li><a href=\"Text/ch2.xhtml#sec2\">第二章 承</a></li>\r\n"
            + "  </ol></nav>\r\n"
            + "</body></html>";

        private const string Ch1 =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n"
            + "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>1</title>\r\n"
            + "<style type=\"text/css\">p { color: red; }</style>\r\n"
            + "<link rel=\"stylesheet\" href=\"../Styles/book.css\"/>\r\n"
            + "</head><body>\r\n"
            + "<h1 id=\"top\">第一章 起</h1>\r\n"
            + "<p>正文一</p>\r\n"
            + "<p><img src=\"../Images/pic.png\" alt=\"图\"/></p>\r\n"
            + "<p><a href=\"ch2.xhtml\">去第二章</a> 和 <a href=\"#note\">脚注</a></p>\r\n"
            + "<p id=\"note\">注</p>\r\n"
            + "</body></html>";

        private const string Ch2 =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n"
            + "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>2</title></head><body>\r\n"
            + "<h1 id=\"sec2\">第二章 承</h1>\r\n"
            + "<p>正文二</p>\r\n"
            + "</body></html>";

        private static string BuildEpub(TempDir dir, bool withContainer = true,
            string opf = Opf3, string nav = Nav, string ch1 = Ch1, string ch2 = Ch2)
        {
            string path = Path.Combine(dir.Path, "book.epub");
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                Add(zip, "mimetype", "application/epub+zip");
                if (withContainer) Add(zip, "META-INF/container.xml", ContainerXml);
                Add(zip, "OEBPS/content.opf", opf);
                Add(zip, "OEBPS/nav.xhtml", nav);
                Add(zip, "OEBPS/Text/ch1.xhtml", ch1);
                Add(zip, "OEBPS/Text/ch2.xhtml", ch2);
                Write(zip, "OEBPS/Images/pic.png", new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            }
            return path;
        }

        private static void Add(ZipArchive zip, string name, string text)
        {
            Write(zip, name, Encoding.UTF8.GetBytes(text));
        }

        private static void Write(ZipArchive zip, string name, byte[] bytes)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
            using (var stream = entry.Open()) stream.Write(bytes, 0, bytes.Length);
        }

        [Fact]
        public void 能读出书名作者和章节数()
        {
            using (var dir = new TempDir())
            {
                string path = BuildEpub(dir);
                string outDir = Path.Combine(dir.Path, "extract");

                var book = EpubReader.Open(path, outDir, "https://epub1.local");

                Assert.Equal("测试书", book.Title);
                Assert.Equal("某人", book.Author);
                Assert.Equal(2, book.ChapterCount);
                Assert.Equal(0, book.SkippedChapters);
            }
        }

        [Fact]
        public void 章节合并成一页且每章有独立锚点()
        {
            using (var dir = new TempDir())
            {
                string path = BuildEpub(dir);
                var book = EpubReader.Open(path, Path.Combine(dir.Path, "extract"), "https://epub1.local");

                Assert.Contains("id=\"c1\"", book.BodyHtml);
                Assert.Contains("id=\"c2\"", book.BodyHtml);
                Assert.Contains("class=\"epub-chapter\"", book.BodyHtml);
                Assert.Contains("正文一", book.BodyHtml);
                Assert.Contains("正文二", book.BodyHtml);
            }
        }

        [Fact]
        public void 章节内id加了章号前缀避免撞车()
        {
            using (var dir = new TempDir())
            {
                string path = BuildEpub(dir);
                var book = EpubReader.Open(path, Path.Combine(dir.Path, "extract"), "https://epub1.local");

                Assert.Contains("id=\"c1-top\"", book.BodyHtml);
                Assert.Contains("id=\"c1-note\"", book.BodyHtml);
                Assert.Contains("id=\"c2-sec2\"", book.BodyHtml);
                Assert.DoesNotContain("id=\"top\"", book.BodyHtml);
            }
        }

        [Fact]
        public void 图片路径换成可加载的绝对地址()
        {
            using (var dir = new TempDir())
            {
                string path = BuildEpub(dir);
                var book = EpubReader.Open(path, Path.Combine(dir.Path, "extract"), "https://epub1.local");

                Assert.Contains("src=\"https://epub1.local/OEBPS/Images/pic.png\"", book.BodyHtml);
                Assert.True(File.Exists(Path.Combine(book.RootDir, "OEBPS", "Images", "pic.png")));
            }
        }

        [Fact]
        public void 跨章链接变成页内锚点()
        {
            using (var dir = new TempDir())
            {
                string path = BuildEpub(dir);
                var book = EpubReader.Open(path, Path.Combine(dir.Path, "extract"), "https://epub1.local");

                Assert.Contains("href=\"#c2\"", book.BodyHtml);
                Assert.Contains("href=\"#c1-note\"", book.BodyHtml);
            }
        }

        [Fact]
        public void 书里的样式表被剥掉()
        {
            using (var dir = new TempDir())
            {
                string path = BuildEpub(dir);
                var book = EpubReader.Open(path, Path.Combine(dir.Path, "extract"), "https://epub1.local");

                Assert.DoesNotContain("<style", book.BodyHtml, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("<link", book.BodyHtml, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("color: red", book.BodyHtml);
            }
        }

        [Fact]
        public void 目录来自EPUB3的nav()
        {
            using (var dir = new TempDir())
            {
                string path = BuildEpub(dir);
                var book = EpubReader.Open(path, Path.Combine(dir.Path, "extract"), "https://epub1.local");

                Assert.Equal(2, book.Toc.Count);
                Assert.Equal("第一章 起", book.Toc[0].Text);
                Assert.Equal("c1", book.Toc[0].Id);
                Assert.Equal("第二章 承", book.Toc[1].Text);
                Assert.Equal("c2-sec2", book.Toc[1].Id);
            }
        }

        [Fact]
        public void 没有container时退回去找opf()
        {
            using (var dir = new TempDir())
            {
                string path = BuildEpub(dir, withContainer: false);
                var book = EpubReader.Open(path, Path.Combine(dir.Path, "extract"), "https://epub1.local");

                Assert.Equal("测试书", book.Title);
                Assert.Equal(2, book.ChapterCount);
            }
        }

        [Fact]
        public void EPUB2的ncx目录也能读()
        {
            string opf2 =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n"
                + "<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"2.0\" unique-identifier=\"bookid\">\r\n"
                + "  <metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">\r\n"
                + "    <dc:title>老书</dc:title>\r\n"
                + "  </metadata>\r\n"
                + "  <manifest>\r\n"
                + "    <item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>\r\n"
                + "    <item id=\"c1\" href=\"Text/ch1.xhtml\" media-type=\"application/xhtml+xml\"/>\r\n"
                + "  </manifest>\r\n"
                + "  <spine toc=\"ncx\"><itemref idref=\"c1\"/></spine>\r\n"
                + "</package>";

            string ncx =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n"
                + "<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" version=\"2005-1\">\r\n"
                + "  <navMap>\r\n"
                + "    <navPoint id=\"n1\" playOrder=\"1\">\r\n"
                + "      <navLabel><text>唯一一章</text></navLabel>\r\n"
                + "      <content src=\"Text/ch1.xhtml\"/>\r\n"
                + "    </navPoint>\r\n"
                + "  </navMap>\r\n"
                + "</ncx>";

            using (var dir = new TempDir())
            {
                string path = Path.Combine(dir.Path, "old.epub");
                using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    Add(zip, "mimetype", "application/epub+zip");
                    Add(zip, "META-INF/container.xml", ContainerXml);
                    Add(zip, "OEBPS/content.opf", opf2);
                    Add(zip, "OEBPS/toc.ncx", ncx);
                    Add(zip, "OEBPS/Text/ch1.xhtml", Ch1);
                }

                var book = EpubReader.Open(path, Path.Combine(dir.Path, "extract"), "https://epub1.local");

                Assert.Equal("老书", book.Title);
                Assert.Single(book.Toc);
                Assert.Equal("唯一一章", book.Toc[0].Text);
                Assert.Equal("c1", book.Toc[0].Id);
            }
        }

        [Fact]
        public void 压缩包里想跳出目录的文件不会被写出去()
        {
            using (var dir = new TempDir())
            {
                string path = Path.Combine(dir.Path, "evil.epub");
                using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    Add(zip, "META-INF/container.xml", ContainerXml);
                    Add(zip, "OEBPS/content.opf", Opf3);
                    Add(zip, "OEBPS/nav.xhtml", Nav);
                    Add(zip, "OEBPS/Text/ch1.xhtml", Ch1);
                    Add(zip, "OEBPS/Text/ch2.xhtml", Ch2);
                    Add(zip, "../../escaped.txt", "不该出现在这里");
                }

                string outDir = Path.Combine(dir.Path, "extract");
                EpubReader.Open(path, outDir, "https://epub1.local");

                string escaped = Path.GetFullPath(Path.Combine(outDir, "..", "..", "escaped.txt"));
                Assert.False(File.Exists(escaped));
            }
        }

        [Fact]
        public void 不是epub的文件会给出人话错误()
        {
            using (var dir = new TempDir())
            {
                string path = dir.File("fake.epub", "这不是 zip");
                var ex = Assert.ThrowsAny<Exception>(() =>
                    EpubReader.Open(path, Path.Combine(dir.Path, "extract"), "https://epub1.local"));
                Assert.NotNull(ex);
            }
        }

        [Fact]
        public void 文件不存在时报友好错误()
        {
            using (var dir = new TempDir())
            {
                string path = Path.Combine(dir.Path, "nope.epub");
                var ex = Assert.Throws<FriendlyException>(() =>
                    EpubReader.Open(path, Path.Combine(dir.Path, "extract"), "https://epub1.local"));
                Assert.Contains("找不到", ex.Message);
            }
        }

        [Fact]
        public void 扩展名判断不区分大小写()
        {
            Assert.True(EpubReader.IsEpubFile("a.epub"));
            Assert.True(EpubReader.IsEpubFile("a.EPUB"));
            Assert.False(EpubReader.IsEpubFile("a.md"));
            Assert.False(EpubReader.IsEpubFile(null));
        }
        [Fact]
        public void 章节里单引号属性不能注入HTML()
        {
            using (var dir = new TempDir())
            {
                const string evil = "<html><head><title>x</title></head><body><p>正文</p>"
                    + "<a href='#x\" ><div style=position:fixed;top:0;left:0;width:100%;height:100%;background:white>FAKE</div>'>anchor</a>"
                    + "</body></html>";

                string path = BuildEpub(dir, ch1: evil);
                EpubDocument doc = EpubReader.Open(path, Path.Combine(dir.Path, "out"), "https://epub.local");

                Assert.Contains("正文", doc.BodyHtml);
                Assert.Contains("anchor", doc.BodyHtml);
                Assert.DoesNotContain("<div", doc.BodyHtml);
            }
        }

        [Fact]
        public void 未闭合的style不会吞掉章节内容()
        {
            using (var dir = new TempDir())
            {
                const string evil = "<html><head><title>x</title></head><body>"
                    + "<style>/* 只写了一半 */<p>正文</p>"
                    + "</body></html>";

                string path = BuildEpub(dir, ch1: evil);
                EpubDocument doc = EpubReader.Open(path, Path.Combine(dir.Path, "out"), "https://epub.local");

                Assert.Contains("正文", doc.BodyHtml);
                Assert.DoesNotContain("<style", doc.BodyHtml, StringComparison.OrdinalIgnoreCase);
            }
        }    }
}
