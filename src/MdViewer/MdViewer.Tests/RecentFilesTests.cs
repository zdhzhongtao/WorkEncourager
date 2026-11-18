using System;
using System.Collections.Generic;
using System.IO;
using MdViewer.Core;

namespace MdViewer.Tests
{
    public class RecentFilesTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.FromHours(8));

        [Fact]
        public void 新加的文件排第一且去重()
        {
            using (var dir = new TempDir())
            {
                string a = dir.File("a.md", "# a");
                string b = dir.File("b.md", "# b");

                var list = RecentFiles.Add(new List<RecentEntry>(), a, Now);
                list = RecentFiles.Add(list, b, Now.AddMinutes(1));
                Assert.Equal(b, list[0].Path);
                Assert.Equal(a, list[1].Path);

                // 再打开 a，a 回到第一位，且不出现两次
                list = RecentFiles.Add(list, a, Now.AddMinutes(2));
                Assert.Equal(a, list[0].Path);
                Assert.Equal(b, list[1].Path);
                Assert.Equal(2, list.Count);
            }
        }

        [Fact]
        public void 忽略大小写去重()
        {
            using (var dir = new TempDir())
            {
                string a = dir.File("Report.md", "# a");
                var list = RecentFiles.Add(new List<RecentEntry>(), a, Now);
                list = RecentFiles.Add(list, a.ToUpperInvariant(), Now.AddMinutes(1));
                Assert.Single(list);
            }
        }

        [Fact]
        public void 最多十二条()
        {
            using (var dir = new TempDir())
            {
                var current = new List<RecentEntry>();
                for (int i = 0; i < 20; i++)
                {
                    string path = dir.File("f" + i + ".md", "# " + i);
                    current = RecentFiles.Add(current, path, Now.AddMinutes(i));
                }
                Assert.Equal(RecentFiles.MaxCount, current.Count);
                Assert.Equal(12, current.Count);
                Assert.EndsWith("f19.md", current[0].Path, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void 已删除的文件保留在列表里但标记为不可用()
        {
            using (var dir = new TempDir())
            {
                string real = dir.File("real.md", "# real");
                string fake = Path.Combine(dir.Path, "gone.md");

                var list = RecentFiles.Add(new List<RecentEntry>(), real, Now);
                list = RecentFiles.Add(list, fake, Now.AddMinutes(1));

                // 不剔除：用户要能看到「我读过这个，文件没了」
                Assert.Equal(2, list.Count);
                Assert.True(RecentFiles.Exists(real));
                Assert.False(RecentFiles.Exists(fake));
            }
        }

        [Fact]
        public void 可以从列表里移除单条()
        {
            using (var dir = new TempDir())
            {
                string a = dir.File("a.md", "# a");
                string b = dir.File("b.md", "# b");
                var list = RecentFiles.Add(new List<RecentEntry>(), a, Now);
                list = RecentFiles.Add(list, b, Now.AddMinutes(1));

                list = RecentFiles.Remove(list, a.ToUpperInvariant());
                Assert.Single(list);
                Assert.Equal(b, list[0].Path);
            }
        }

        [Fact]
        public void 旧配置的纯路径列表能迁移()
        {
            var migrated = RecentFiles.FromLegacy(new List<string> { @"C:\a\1.md", @"C:\a\2.md", @"C:\a\1.md", "", null });
            Assert.Equal(2, migrated.Count);
            Assert.Equal(@"C:\a\1.md", migrated[0].Path);
            Assert.Equal(string.Empty, migrated[0].OpenedAt ?? string.Empty);
        }

        [Fact]
        public void 空输入不炸()
        {
            Assert.Empty(RecentFiles.Add(null, null, Now));
            Assert.Empty(RecentFiles.Normalize(null));
            Assert.Empty(RecentFiles.Remove(null, "x"));
            Assert.Empty(RecentFiles.ToPaths(null));
        }

        [Fact]
        public void 相对时间说法()
        {
            var entry = new RecentEntry { Path = @"C:\a.md", OpenedAt = Now.AddSeconds(-30).ToString("o") };
            Assert.Equal("刚刚", RecentFiles.Describe(entry, Now));

            entry.OpenedAt = Now.AddMinutes(-5).ToString("o");
            Assert.Equal("5 分钟前", RecentFiles.Describe(entry, Now));

            entry.OpenedAt = Now.AddHours(-3).ToString("o");
            Assert.Equal("3 小时前", RecentFiles.Describe(entry, Now));

            entry.OpenedAt = Now.AddDays(-1).ToString("o");
            Assert.Equal("昨天", RecentFiles.Describe(entry, Now));

            entry.OpenedAt = Now.AddDays(-3).ToString("o");
            Assert.Equal("3 天前", RecentFiles.Describe(entry, Now));
        }

        [Fact]
        public void 相对时间只跟着传入的偏移走()
        {
            var entry = new RecentEntry { Path = @"C:\a.md", OpenedAt = Now.AddHours(-3).ToString("o") };

            // 同一条记录，换一种偏移表示 now：只要没跨日界，说法都该是「3 小时前」。
            // 这里曾经用 LocalDateTime 判断"同一天"，在 UTC 的 CI 上会变成「昨天」。
            Assert.Equal("3 小时前", RecentFiles.Describe(entry, Now));
            Assert.Equal("3 小时前", RecentFiles.Describe(entry, Now.ToOffset(TimeSpan.FromHours(-5))));

            // 换成跨过日界的视角就说「昨天」—— 这是有意的：凌晨两点说「昨天 23 点」比「3 小时前」清楚
            Assert.Equal("昨天", RecentFiles.Describe(entry, Now.ToOffset(TimeSpan.Zero)));
        }

        [Fact]
        public void 没有时间戳的条目不显示时间()
        {
            var entry = new RecentEntry { Path = @"C:\a.md", OpenedAt = null };
            Assert.Equal(string.Empty, RecentFiles.Describe(entry, Now));
            Assert.Equal(DateTimeOffset.MinValue, entry.OpenedTime);
        }
    }
}
