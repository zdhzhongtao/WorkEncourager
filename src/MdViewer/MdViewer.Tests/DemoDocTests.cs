using System;
using System.IO;
using MdViewer.Core;

namespace MdViewer.Tests
{
    public class DemoDocTests
    {
        [Fact]
        public void 生成样例文档与图片()
        {
            using (var dir = new TempDir())
            {
                string docPath = DemoDoc.Ensure(dir.Path);
                Assert.True(File.Exists(docPath));

                string imagePath = Path.Combine(dir.Path, DemoDoc.ImageFolderName, DemoDoc.ImageFileName);
                Assert.True(File.Exists(imagePath));
                Assert.Contains("<svg", File.ReadAllText(imagePath));
            }
        }

        [Fact]
        public void 样例文档覆盖验收要素()
        {
            string markdown = DemoDoc.MarkdownText();
            var doc = MarkdownRenderer.Render(markdown);

            Assert.True(doc.Toc.Count >= 4, "样例至少要有 4 个 h1~h3 标题");
            Assert.Contains("<table>", doc.BodyHtml);
            Assert.Contains("language-csharp", doc.BodyHtml);
            Assert.Contains("task-box", doc.BodyHtml);
            Assert.Contains("https://mdviewer.local/images/demo.svg",
                ImageRewriter.Rewrite(doc.BodyHtml, @"C:\demo", "mdviewer.local"));
            Assert.Contains("footnote", doc.BodyHtml, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void 再生成一次不会重复追加()
        {
            using (var dir = new TempDir())
            {
                string first = DemoDoc.Ensure(dir.Path);
                string second = DemoDoc.Ensure(dir.Path);
                Assert.Equal(first, second);
                Assert.Equal(DemoDoc.MarkdownText(), File.ReadAllText(first));
            }
        }
    }

    public class SettingsTests
    {
        [Fact]
        public void 设置能存能读()
        {
            using (var dir = new TempDir())
            {
                var settings = new AppSettings { Theme = "dark", FontSize = 20, TocVisible = false };
                settings.RecentEntries.Add(new RecentEntry
                {
                    Path = @"C:\a.md",
                    OpenedAt = new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.FromHours(8)).ToString("o")
                });
                SettingsStore.Save(dir.Path, settings);

                var loaded = SettingsStore.Load(dir.Path);
                Assert.Equal(SettingsStore.CurrentVersion, loaded.Version);
                Assert.Equal("dark", loaded.Theme);
                Assert.Equal(20, loaded.FontSize);
                Assert.False(loaded.TocVisible);
                Assert.Single(loaded.RecentFiles);
                Assert.Single(loaded.RecentEntries);
            }
        }

        [Fact]
        public void 配置文件坏了就用默认值()
        {
            using (var dir = new TempDir())
            {
                dir.File("settings.json", "{ 这不是 json ]");
                var loaded = SettingsStore.Load(dir.Path);
                Assert.Equal("system", loaded.Theme);
                Assert.Equal(16, loaded.FontSize);
            }
        }

        [Fact]
        public void 越界字号被拉回16()
        {
            using (var dir = new TempDir())
            {
                dir.File("settings.json", "{\"Version\":1,\"FontSize\":99}");
                Assert.Equal(16, SettingsStore.Load(dir.Path).FontSize);
            }
        }

        [Fact]
        public void 主题id能原样存取()
        {
            using (var dir = new TempDir())
            {
                SettingsStore.Save(dir.Path, new AppSettings { LightThemeId = "sepia", DarkThemeId = "dracula" });

                var loaded = SettingsStore.Load(dir.Path);
                Assert.Equal("sepia", loaded.LightThemeId);
                Assert.Equal("dracula", loaded.DarkThemeId);
            }
        }

        [Fact]
        public void 主题id写坏或明暗写反都回落到默认()
        {
            using (var dir = new TempDir())
            {
                SettingsStore.Save(dir.Path, new AppSettings
                {
                    LightThemeId = "不存在的主题",
                    DarkThemeId = "sepia"        // 浅色主题塞进深色槽位
                });

                var loaded = SettingsStore.Load(dir.Path);
                Assert.Equal(MarkdownThemes.DefaultLightId, loaded.LightThemeId);
                Assert.Equal(MarkdownThemes.DefaultDarkId, loaded.DarkThemeId);
            }
        }

        [Fact]
        public void 最近记录不会把派生时间字段写进文件()
        {
            using (var dir = new TempDir())
            {
                var settings = new AppSettings();
                settings.RecentEntries.Add(new RecentEntry
                {
                    Path = @"C:\a.md",
                    OpenedAt = new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.FromHours(8)).ToString("o")
                });
                SettingsStore.Save(dir.Path, settings);

                // OpenedTime 是只读计算属性，写进 json 就是纯噪音（而且永远不同步）
                string json = File.ReadAllText(SettingsStore.PathIn(dir.Path));
                Assert.DoesNotContain("OpenedTime", json);
                Assert.Contains("OpenedAt", json);
            }
        }

        [Fact]
        public void 老配置的最近列表能迁过来()
        {
            using (var dir = new TempDir())
            {
                dir.File("settings.json",
                    "{\"Version\":1,\"RecentFiles\":[\"C:\\\\a.md\",\"C:\\\\b.md\"]}");

                var loaded = SettingsStore.Load(dir.Path);

                Assert.Equal(2, loaded.RecentEntries.Count);
                Assert.Equal(@"C:\a.md", loaded.RecentEntries[0].Path);
                // 老配置没有时间，迁移后留空，界面上不显示「xx 前」
                Assert.Null(loaded.RecentEntries[0].OpenedAt);
                Assert.Equal(DateTimeOffset.MinValue, loaded.RecentEntries[0].OpenedTime);
            }
        }

        [Fact]
        public void 用户清空最近列表后老字段不会把它填回来()
        {
            using (var dir = new TempDir())
            {
                // 已经是 v2，条目被用户清空了，但老字段还有残留
                dir.File("settings.json",
                    "{\"Version\":2,\"RecentFiles\":[\"C:\\\\a.md\"],\"RecentEntries\":[]}");

                var loaded = SettingsStore.Load(dir.Path);

                Assert.Empty(loaded.RecentEntries);
            }
        }
    }
}
