using System;
using System.IO;
using MdViewer.Core;

namespace MdViewer.Tests
{
    /// <summary>渲染缓存的清理规则。</summary>
    public class CacheStoreTests
    {
        private static string NewDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "mdv-cache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Touch(string dir, string name, DateTime utc)
        {
            string path = Path.Combine(dir, name);
            File.WriteAllText(path, "<html></html>");
            File.SetLastWriteTimeUtc(path, utc);
        }

        [Fact]
        public void 只留最近的若干个()
        {
            string dir = NewDir();
            try
            {
                var now = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
                for (int i = 1; i <= 10; i++) Touch(dir, "view-" + i + ".html", now.AddMinutes(-i));

                int removed = CacheStore.Trim(dir, keep: 3, maxAgeDays: 365, now: now);

                Assert.Equal(7, removed);
                Assert.Equal(3, Directory.GetFiles(dir, "view-*.html").Length);
                Assert.True(File.Exists(Path.Combine(dir, "view-1.html")));
                Assert.False(File.Exists(Path.Combine(dir, "view-10.html")));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void 太旧的也清掉但最新三个不动()
        {
            string dir = NewDir();
            try
            {
                var now = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
                Touch(dir, "view-1.html", now.AddDays(-1));
                Touch(dir, "view-2.html", now.AddDays(-2));
                Touch(dir, "view-3.html", now.AddDays(-3));
                Touch(dir, "view-4.html", now.AddDays(-20));
                Touch(dir, "view-5.html", now.AddDays(-21));

                CacheStore.Trim(dir, keep: 20, maxAgeDays: 7, now: now);

                Assert.Equal(3, Directory.GetFiles(dir, "view-*.html").Length);  // 最新三个受保护，更旧的被清
                Assert.True(File.Exists(Path.Combine(dir, "view-1.html")));
                Assert.False(File.Exists(Path.Combine(dir, "view-4.html")));
                Assert.False(File.Exists(Path.Combine(dir, "view-5.html")));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void 目录不存在或为空时安静返回()
        {
            Assert.Equal(0, CacheStore.Trim(null));
            Assert.Equal(0, CacheStore.Trim(string.Empty));
            Assert.Equal(0, CacheStore.Trim(Path.Combine(Path.GetTempPath(), "根本没有这个目录-" + Guid.NewGuid().ToString("N"))));

            string dir = NewDir();
            try { Assert.Equal(0, CacheStore.Trim(dir)); }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void 不碰非缓存的html()
        {
            string dir = NewDir();
            try
            {
                var now = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
                for (int i = 1; i <= 5; i++) Touch(dir, "view-" + i + ".html", now.AddDays(-40));
                Touch(dir, "keepme.html", now.AddDays(-40));

                CacheStore.Trim(dir, keep: 2, maxAgeDays: 7, now: now);

                Assert.True(File.Exists(Path.Combine(dir, "keepme.html")));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
