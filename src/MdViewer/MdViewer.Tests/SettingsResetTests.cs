using System;
using System.Collections.Generic;
using System.IO;
using MdViewer.Core;

namespace MdViewer.Tests
{
    public class SettingsResetTests
    {
        private static AppSettings DirtySettings()
        {
            return new AppSettings
            {
                Theme = "dark",
                LightThemeId = "sepia",
                DarkThemeId = "nord",
                FontSize = 21,
                MaxContentWidth = 1400,
                WideMode = true,
                AutoReload = false,
                RememberScroll = false,
                TocVisible = false,
                UpdateRepo = "someone/elsewhere",
                CheckUpdateOnStart = true,
                SkippedVersion = "9.9.9",
                LastUpdateCheck = "2026-11-16T10:00:00+08:00",
                WindowWidth = 1640,
                WindowHeight = 980,
                WindowMaximized = true,
                TocWidth = 320,
                LastDir = @"D:\docs",
                AssocHinted = true,
                RecentEntries = new List<RecentEntry>
                {
                    new RecentEntry { Path = @"D:\docs\a.md", OpenedAt = "2026-11-15T09:00:00+08:00" }
                },
                ScrollPositions = new Dictionary<string, double> { { @"D:\docs\a.md", 0.42 } }
            };
        }

        [Fact]
        public void 重置把外观阅读更新配置恢复默认值()
        {
            AppSettings reset = SettingsStore.Reset(DirtySettings());

            Assert.Equal("system", reset.Theme);
            Assert.Equal(MarkdownThemes.DefaultLightId, reset.LightThemeId);
            Assert.Equal(MarkdownThemes.DefaultDarkId, reset.DarkThemeId);
            Assert.Equal(16, reset.FontSize);
            Assert.Equal(880, reset.MaxContentWidth);
            Assert.False(reset.WideMode);
            Assert.True(reset.AutoReload);
            Assert.True(reset.RememberScroll);
            Assert.True(reset.TocVisible);
            Assert.Equal("zdhzhongtao/WorkEncourager", reset.UpdateRepo);
            Assert.False(reset.CheckUpdateOnStart);
            Assert.Equal(string.Empty, reset.SkippedVersion);
            Assert.Equal(string.Empty, reset.LastUpdateCheck);
            Assert.Equal(SettingsStore.CurrentVersion, reset.Version);
        }

        [Fact]
        public void 重置保留最近打开和阅读位置()
        {
            AppSettings reset = SettingsStore.Reset(DirtySettings());

            Assert.Single(reset.RecentEntries);
            Assert.Equal(@"D:\docs\a.md", reset.RecentEntries[0].Path);
            Assert.Single(reset.RecentFiles);
            Assert.Equal(0.42, reset.ScrollPositions[@"D:\docs\a.md"], 3);
            Assert.Equal(@"D:\docs", reset.LastDir);
            Assert.True(reset.AssocHinted);
        }

        [Fact]
        public void 重置保留窗口大小和目录宽度()
        {
            AppSettings reset = SettingsStore.Reset(DirtySettings());

            Assert.Equal(1640, reset.WindowWidth);
            Assert.Equal(980, reset.WindowHeight);
            Assert.True(reset.WindowMaximized);
            Assert.Equal(320, reset.TocWidth);
        }

        [Fact]
        public void 传null也能拿到一份合法默认设置()
        {
            AppSettings reset = SettingsStore.Reset(null);

            Assert.NotNull(reset);
            Assert.Equal(880, reset.MaxContentWidth);
            Assert.Empty(reset.RecentEntries);
        }

        [Fact]
        public void 备份会复制一份带时间戳的副本()
        {
            using (var dir = new TempDir())
            {
                SettingsStore.Save(dir.Path, DirtySettings());
                string source = File.ReadAllText(SettingsStore.PathIn(dir.Path));

                string backup = SettingsStore.Backup(dir.Path);

                Assert.NotNull(backup);
                Assert.True(File.Exists(backup));
                Assert.Contains("settings-", Path.GetFileName(backup));
                Assert.EndsWith(".bak.json", Path.GetFileName(backup), StringComparison.Ordinal);
                Assert.Equal(source, File.ReadAllText(backup));
            }
        }

        [Fact]
        public void 没有设置文件时备份返回null()
        {
            using (var dir = new TempDir())
            {
                Assert.Null(SettingsStore.Backup(dir.Path));
            }
        }

        [Fact]
        public void 备份最多留五份()
        {
            using (var dir = new TempDir())
            {
                SettingsStore.Save(dir.Path, DirtySettings());

                // 先铺六份旧备份，再备份一次，应该只留最新五份
                for (int i = 1; i <= 6; i++)
                    dir.File("settings-20200101-00000" + i + "-000.bak.json", "{}");

                string newest = SettingsStore.Backup(dir.Path);
                Assert.NotNull(newest);

                string[] left = Directory.GetFiles(dir.Path, "settings-*.bak.json");
                Assert.Equal(5, left.Length);
                Assert.Contains(newest, left);

                // 最早那份被清掉了
                Assert.DoesNotContain(
                    Path.Combine(dir.Path, "settings-20200101-000001-000.bak.json"), left);
            }
        }
    }
}
