using System;
using System.IO;
using MdViewer.Core;

namespace MdViewer.Tests
{
    /// <summary>
    /// 第三方库是内嵌资源，首次运行释放到缓存目录。
    /// 这一组只验证"放得出来、认得准、放两遍不出事"，不校验库本身跑得对（那是浏览器的事）。
    /// </summary>
    public class VendorAssetsTests
    {
        [Fact]
        public void 内嵌资源清单包含四个必备文件()
        {
            Assert.Contains("katex/katex.min.js", VendorAssets.ListResources());
            Assert.Contains("katex/katex.min.css", VendorAssets.ListResources());
            Assert.Contains("mermaid/mermaid.min.js", VendorAssets.ListResources());
            Assert.Contains("hljs/highlight.min.js", VendorAssets.ListResources());
        }

        [Fact]
        public void 首次运行会把文件释放到缓存目录()
        {
            string cache = Path.Combine(Path.GetTempPath(), "mdviewer-vendor-" + Guid.NewGuid().ToString("N"));
            try
            {
                string vendor = VendorAssets.Ensure(cache);

                Assert.True(Directory.Exists(vendor));
                Assert.True(File.Exists(Path.Combine(vendor, "katex", "katex.min.js")));
                Assert.True(File.Exists(Path.Combine(vendor, "mermaid", "mermaid.min.js")));
                Assert.True(File.Exists(Path.Combine(vendor, "hljs", "highlight.min.js")));
                Assert.True(VendorAssets.IsUsable(vendor));
            }
            finally
            {
                try { Directory.Delete(cache, true); } catch { /* 临时目录删不掉无所谓 */ }
            }
        }

        [Fact]
        public void 第二次调用直接复用不再解压()
        {
            string cache = Path.Combine(Path.GetTempPath(), "mdviewer-vendor-" + Guid.NewGuid().ToString("N"));
            try
            {
                string first = VendorAssets.Ensure(cache);
                string katex = Path.Combine(first, "katex", "katex.min.js");
                Assert.True(File.Exists(katex));

                DateTime written = File.GetLastWriteTimeUtc(katex);

                // 版本标记对得上就该直接返回，不能又把 3MB 重写一遍
                string second = VendorAssets.Ensure(cache);
                Assert.Equal(first, second);
                Assert.Equal(written, File.GetLastWriteTimeUtc(katex));
            }
            finally
            {
                try { Directory.Delete(cache, true); } catch { /* 临时目录删不掉无所谓 */ }
            }
        }

        [Fact]
        public void 版本标记对不上时重新释放()
        {
            string cache = Path.Combine(Path.GetTempPath(), "mdviewer-vendor-" + Guid.NewGuid().ToString("N"));
            try
            {
                string vendor = VendorAssets.Ensure(cache);
                string marker = Path.Combine(vendor, "version.txt");

                // 手改成旧版本号，模拟"程序升级后库里版本变了"
                File.WriteAllText(marker, "katex-0.0.1");
                VendorAssets.Ensure(cache);

                Assert.Equal(VendorAssets.Version, File.ReadAllText(marker).Trim());
            }
            finally
            {
                try { Directory.Delete(cache, true); } catch { /* 临时目录删不掉无所谓 */ }
            }
        }

        [Fact]
        public void 缺文件时判定为不可用()
        {
            string dir = Path.Combine(Path.GetTempPath(), "mdviewer-vendor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Assert.False(VendorAssets.IsUsable(dir));
                Assert.False(VendorAssets.IsUsable(null));
                Assert.False(VendorAssets.IsUsable(string.Empty));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* 临时目录删不掉无所谓 */ }
            }
        }

        [Fact]
        public void 版本号变化后跟着变()
        {
            Assert.False(string.IsNullOrWhiteSpace(VendorAssets.Version));
            Assert.Contains("katex", VendorAssets.Version);
            Assert.Contains("mermaid", VendorAssets.Version);
        }
    }
}
