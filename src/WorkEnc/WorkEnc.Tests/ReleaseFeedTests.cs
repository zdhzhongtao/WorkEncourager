using System;
using WorkEnc.Core;
using Xunit;

namespace WorkEnc.Tests
{
    /// <summary>
    /// 更新检查的纯逻辑：从 releases.atom 里只挑 workenc-v* 的发布。
    ///
    /// 背景：仓库从单工具的 WorkEncourageur 改成多工具的 WadeToolbox 之后，
    /// `/releases/latest` 会返回别的工具（mdviewer）的发布，tag 解析不出本程序版本，
    /// 结果永远显示「已是最新」。所以这里覆盖「混着别的工具」「标题被改成人类可读」
    /// 「只有别的工具」「RSS 坏掉」几种情况。
    /// </summary>
    public class ReleaseFeedTests
    {
        private const string AtomHead =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<feed xmlns=\"http://www.w3.org/2005/Atom\">\n" +
            "  <title>Release notes from WadeToolbox</title>\n";

        private const string AtomTail = "</feed>";

        private static string Entry(string title, string tag, string updated = "2026-11-19T02:59:08Z")
        {
            return
                "  <entry>\n" +
                "    <title>" + title + "</title>\n" +
                "    <id>tag:github.com,2008:Repository/1444059723/" + tag + "</id>\n" +
                "    <updated>" + updated + "</updated>\n" +
                "    <link rel=\"alternate\" type=\"text/html\" " +
                "href=\"https://github.com/zdhzhongtao/WadeToolbox/releases/tag/" + tag + "\"/>\n" +
                "  </entry>\n";
        }

        [Fact]
        public void 混着别的工具时只挑workenc且取版本最高()
        {
            string xml = AtomHead
                + Entry("mdviewer-v0.5.3", "mdviewer-v0.5.3")
                + Entry("workenc-v2.2.2", "workenc-v2.2.2")
                + Entry("workenc-v2.2.3", "workenc-v2.2.3")
                + AtomTail;

            ReleaseEntry entry = ReleaseFeed.PickLatest(xml);

            Assert.Null(entry.Error);
            Assert.Equal("workenc-v2.2.3", entry.Tag);
            Assert.Equal(new Version(2, 2, 3), entry.Version);
            Assert.Equal("https://github.com/zdhzhongtao/WadeToolbox/releases/tag/workenc-v2.2.3",
                entry.PageUrl);
        }

        [Fact]
        public void 只有别的工具的发布时明确报没找到()
        {
            string xml = AtomHead
                + Entry("mdviewer-v0.5.3", "mdviewer-v0.5.3")
                + AtomTail;

            ReleaseEntry entry = ReleaseFeed.PickLatest(xml);

            Assert.Null(entry.Tag);
            Assert.Contains("workenc-v", entry.Error);
        }

        [Fact]
        public void 标题被改成人类可读时用id末段认tag()
        {
            string xml = AtomHead
                + Entry("上班鼓励器 2.2.3 修了个小问题", "workenc-v2.2.3")
                + AtomTail;

            ReleaseEntry entry = ReleaseFeed.PickLatest(xml);

            Assert.Equal("workenc-v2.2.3", entry.Tag);
        }

        [Fact]
        public void RSS是坏XML时报错而不是抛异常()
        {
            ReleaseEntry entry = ReleaseFeed.PickLatest("<feed><entry>");

            Assert.Null(entry.Tag);
            Assert.NotNull(entry.Error);
        }

        [Fact]
        public void 空RSS报错()
        {
            ReleaseEntry entry = ReleaseFeed.PickLatest("");

            Assert.Null(entry.Tag);
            Assert.NotNull(entry.Error);
        }

        [Theory]
        [InlineData("workenc-v2.2.3", "2.2.2", true)]
        [InlineData("workenc-v2.2.2", "2.2.2", false)]
        [InlineData("workenc-v2.2.1", "2.2.2", false)]
        [InlineData("workenc-v3.0.0", "2.9.9", true)]
        public void IsNewer按三段版本比较(string tag, string current, bool expected)
        {
            Assert.Equal(expected, ReleaseFeed.IsNewer(tag, current));
        }

        [Theory]
        [InlineData("workenc-v0.6.2.7", 0, 6, 2)]
        [InlineData("v2.2", 2, 2, 0)]
        [InlineData("2.2.2-beta1", 2, 2, 2)]
        public void TryParseVersion补成三段(string text, int major, int minor, int build)
        {
            Version version;
            Assert.True(ReleaseFeed.TryParseVersion(text, out version));
            Assert.Equal(new Version(major, minor, build), version);
        }
    }
}
