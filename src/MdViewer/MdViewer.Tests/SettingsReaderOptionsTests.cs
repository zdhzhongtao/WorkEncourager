using System;
using System.Collections.Generic;
using System.IO;
using MdViewer.Core;

namespace MdViewer.Tests
{
    /// <summary>0.5.0 新增的配置项：读老配置、手改配置、脏数据都不能把界面带崩。</summary>
    public class SettingsReaderOptionsTests
    {
        private static AppSettings New()
        {
            return new AppSettings();
        }

        [Fact]
        public void 字体名里的引号和逗号会被清掉()
        {
            AppSettings s = New();
            s.FontBody = " 'LXGW WenKai', ";
            s.FontCode = "\"JetBrains Mono\";";
            s.FontHeading = "   ";

            SettingsStore.Normalize(s);

            Assert.Equal("LXGW WenKai", s.FontBody);
            Assert.DoesNotContain("'", s.FontCode);
            Assert.DoesNotContain("\"", s.FontCode);
            Assert.Equal(string.Empty, s.FontHeading);
        }

        [Fact]
        public void 字体名过长会被截断()
        {
            AppSettings s = New();
            s.FontBody = new string('A', 500);

            SettingsStore.Normalize(s);

            Assert.True(s.FontBody.Length <= 120);
        }

        [Fact]
        public void 定时换肤的时间收敛成两位小时()
        {
            AppSettings s = New();
            s.AutoDimFrom = "9:5";
            s.AutoDimTo = "0700";

            SettingsStore.Normalize(s);

            Assert.Equal("09:05", s.AutoDimFrom);
            Assert.Equal("07:00", s.AutoDimTo);
        }

        [Fact]
        public void 认不出的时间回落到默认值()
        {
            AppSettings s = New();
            s.AutoDimFrom = "晚上七点";
            s.AutoDimTo = "99:99";

            SettingsStore.Normalize(s);

            Assert.Equal("19:00", s.AutoDimFrom);
            Assert.Equal("07:00", s.AutoDimTo);
        }

        [Fact]
        public void 侧栏页签只认三个值()
        {
            AppSettings s = New();
            s.SidebarTab = "files";
            SettingsStore.Normalize(s);
            Assert.Equal("files", s.SidebarTab);

            s.SidebarTab = "NOTES";
            SettingsStore.Normalize(s);
            Assert.Equal("notes", s.SidebarTab);

            // 早期版本叫 marks，老配置要能迁过来
            s.SidebarTab = "marks";
            SettingsStore.Normalize(s);
            Assert.Equal("notes", s.SidebarTab);

            s.SidebarTab = "什么鬼";
            SettingsStore.Normalize(s);
            Assert.Equal("toc", s.SidebarTab);
        }

        [Fact]
        public void 打开过的标签去空去重并限量()
        {
            AppSettings s = New();
            s.OpenTabs = new List<string> { @"c:\a.md", "", "  ", @"c:\a.md", @"c:\b.md" };

            SettingsStore.Normalize(s);

            Assert.Equal(2, s.OpenTabs.Count);
            Assert.Equal(@"c:\a.md", s.OpenTabs[0]);
            Assert.Equal(@"c:\b.md", s.OpenTabs[1]);
        }

        [Fact]
        public void 标签太多时只保留最近的二十个()
        {
            AppSettings s = New();
            s.OpenTabs = new List<string>();
            for (int i = 0; i < 30; i++) s.OpenTabs.Add(@"c:\doc" + i + ".md");

            SettingsStore.Normalize(s);

            Assert.Equal(20, s.OpenTabs.Count);
            Assert.Equal(@"c:\doc10.md", s.OpenTabs[0]);
            Assert.Equal(@"c:\doc29.md", s.OpenTabs[19]);
        }

        [Fact]
        public void 朗读速度限制在合理区间()
        {
            AppSettings s = New();
            s.ReadRate = 9;
            SettingsStore.Normalize(s);
            Assert.Equal(1.0, s.ReadRate, 3);

            s.ReadRate = double.NaN;
            SettingsStore.Normalize(s);
            Assert.Equal(1.0, s.ReadRate, 3);

            s.ReadRate = 1.5;
            SettingsStore.Normalize(s);
            Assert.Equal(1.5, s.ReadRate, 3);
        }

        [Fact]
        public void 默认值本身要能通过归一化()
        {
            // 免得改默认值时忘了同步 Normalize，一启动就被改成别的
            AppSettings s = New();
            SettingsStore.Normalize(s);

            Assert.Equal("toc", s.SidebarTab);
            Assert.Equal(1.0, s.ReadRate, 3);
            Assert.Equal("19:00", s.AutoDimFrom);
            Assert.Equal("07:00", s.AutoDimTo);
            Assert.Empty(s.FontBody);
            Assert.Empty(s.OpenTabs);
        }

        [Fact]
        public void 重置设置时保留阅读痕迹()
        {
            AppSettings current = New();
            current.OpenTabs = new List<string> { @"c:\a.md" };
            current.LastDocument = @"c:\a.md";
            current.FolderRoot = @"c:\docs";
            current.SidebarTab = "files";
            current.FontBody = "LXGW WenKai";

            AppSettings defaults = SettingsStore.Reset(current);

            // 痕迹保留
            Assert.Single(defaults.OpenTabs);
            Assert.Equal(@"c:\a.md", defaults.LastDocument);
            Assert.Equal(@"c:\docs", defaults.FolderRoot);
            Assert.Equal("files", defaults.SidebarTab);

            // 真正的设置回到默认
            Assert.Empty(defaults.FontBody);
        }
    }
}
