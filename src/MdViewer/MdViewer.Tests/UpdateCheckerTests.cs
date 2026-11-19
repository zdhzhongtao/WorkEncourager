using System;
using System.IO;
using MdViewer.Core;

namespace MdViewer.Tests
{
    public class UpdateCheckerTests
    {
        [Theory]
        [InlineData("mdviewer-v0.3.0", "0.3.0")]
        [InlineData("v1.2.3", "1.2.3")]
        [InlineData("0.9.1-beta", "0.9.1")]
        [InlineData("release-2.0", "2.0.0")]
        [InlineData("v1", "1.0.0")]
        public void 从tag里抠出版本号(string tag, string expected)
        {
            Version version;
            Assert.True(UpdateChecker.TryParseVersion(tag, out version));
            Assert.Equal(expected, version.ToString());
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("nightly")]
        [InlineData("latest")]
        public void 认不出的tag返回false(string tag)
        {
            Version version;
            Assert.False(UpdateChecker.TryParseVersion(tag, out version));
        }

        [Fact]
        public void 版本比较只看三段()
        {
            Assert.True(UpdateChecker.IsNewer(new Version(0, 3, 1), new Version(0, 3, 0)));
            Assert.True(UpdateChecker.IsNewer(new Version(0, 4, 0), new Version(0, 3, 9)));
            Assert.True(UpdateChecker.IsNewer(new Version(1, 0, 0), new Version(0, 99, 99)));
            Assert.False(UpdateChecker.IsNewer(new Version(0, 3, 0), new Version(0, 3, 0)));
            Assert.False(UpdateChecker.IsNewer(new Version(0, 3, 0), new Version(0, 3, 1)));
            Assert.False(UpdateChecker.IsNewer(null, new Version(0, 3, 0)));
        }

        [Fact]
        public void 程序集版本带第四段也能比()
        {
            // 0.3.0.0（程序集默认格式）应该等于 0.3.0
            Version current = UpdateChecker.ParseCurrent("0.3.0.0");
            Assert.False(UpdateChecker.IsNewer(new Version(0, 3, 0), current));
            Assert.True(UpdateChecker.IsNewer(new Version(0, 3, 1), current));
        }

        [Fact]
        public void 乱七八糟的当前版本当成零()
        {
            Version current = UpdateChecker.ParseCurrent("dev");
            Assert.Equal(new Version(0, 0, 0), current);
        }

        [Fact]
        public void 认得出exe文件头()
        {
            using (var dir = new TempDir())
            {
                string exe = dir.File("a.exe", new byte[] { (byte)'M', (byte)'Z', 0x90, 0x00 });
                Assert.True(UpdateChecker.LooksLikeExe(exe));

                string html = dir.File("b.exe", "<html>404</html>");
                Assert.False(UpdateChecker.LooksLikeExe(html));

                Assert.False(UpdateChecker.LooksLikeExe(Path.Combine(dir.Path, "missing.exe")));
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task 仓库地址没填好直接给人话错误()
        {
            var result = await UpdateChecker.CheckAsync("", "0.3.0", System.Threading.CancellationToken.None);
            Assert.NotNull(result.Error);
            Assert.False(result.HasUpdate);
        }

        [Fact]
        public void 从tag里抠版本号时不会把仓库名的数字当版本()
        {
            // 复用主逻辑：mdviewer 里没有数字，版本应该来自 v 后面那段
            Version version;
            Assert.True(UpdateChecker.TryParseVersion("mdviewer-v0.3.0", out version));
            Assert.Equal("0.3.0", version.ToString());
        }

        [Fact]
        public async System.Threading.Tasks.Task 下载空地址不抛异常()
        {
            using (var dir = new TempDir())
            {
                string target = Path.Combine(dir.Path, "x.exe");
                string error = await UpdateChecker.DownloadAsync(null, target, null,
                    System.Threading.CancellationToken.None);
                Assert.NotNull(error);
                Assert.False(File.Exists(target));
            }
        }

        // ───────────────────────── atom 备用通道 ─────────────────────────
        // API 匿名配额只有 60 次/小时，共用出口 IP 一早就用光。这时改读 releases.atom，
        // 它不占配额，代价是只有 tag、没有附件列表，文件名要按发布约定拼。

        private const string AtomSample = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<feed xmlns=""http://www.w3.org/2005/Atom"" xml:lang=""en-US"">
  <title>Release notes from WorkEncourager</title>
  <updated>2026-11-18T09:31:47Z</updated>
  <entry>
    <id>tag:github.com,2008:Repository/1444059723/mdviewer-v0.5.1</id>
    <updated>2026-11-18T09:33:31Z</updated>
    <link rel=""alternate"" type=""text/html"" href=""https://github.com/z/WorkEncourager/releases/tag/mdviewer-v0.5.1""/>
    <title>mdviewer-v0.5.1</title>
  </entry>
  <entry>
    <id>tag:github.com,2008:Repository/1444059723/mdviewer-v0.5.0</id>
    <updated>2026-11-18T07:16:07Z</updated>
    <title>mdviewer-v0.5.0</title>
  </entry>
  <entry>
    <id>tag:github.com,2008:Repository/1444059723/moneycalc-v0.1.1</id>
    <updated>2026-11-10T03:00:00Z</updated>
    <title>moneycalc-v0.1.1</title>
  </entry>
</feed>";

        [Fact]
        public void atom通道能挑出更高版本并拼出下载地址()
        {
            UpdateResult result = UpdateChecker.PickFromAtom(AtomSample, "z/WorkEncourager", "0.5.0", "MdViewer");

            Assert.Null(result.Error);
            Assert.True(result.HasUpdate);
            Assert.Equal("0.5.1", result.Latest);
            Assert.Equal("mdviewer-v0.5.1", result.Tag);
            Assert.Equal("MdViewer-0.5.1-portable.exe", result.AssetName);
            Assert.Equal(
                "https://github.com/z/WorkEncourager/releases/download/mdviewer-v0.5.1/MdViewer-0.5.1-portable.exe",
                result.AssetUrl);
            Assert.Equal("https://github.com/z/WorkEncourager/releases/tag/mdviewer-v0.5.1", result.PageUrl);
            Assert.NotNull(result.PublishedAt);
            Assert.True(result.CanDownload);
        }

        [Fact]
        public void atom通道已经是最新版就不提示更新()
        {
            UpdateResult result = UpdateChecker.PickFromAtom(AtomSample, "z/WorkEncourager", "0.5.1", "MdViewer");

            Assert.Null(result.Error);
            Assert.False(result.HasUpdate);
            Assert.False(result.CanDownload);
            Assert.Equal("0.5.1", result.Latest);
        }

        [Fact]
        public void atom通道不认别的工具的发布()
        {
            // 列表里只有别的工具的 tag 时，不能把它的版本当成本程序的更新
            UpdateResult result = UpdateChecker.PickFromAtom(AtomSample, "z/WorkEncourager", "0.5.0", "SheetSplitter");

            Assert.NotNull(result.Error);
            Assert.False(result.HasUpdate);
        }

        [Fact]
        public void atom通道标题不是tag时用id末段()
        {
            const string xml = @"<feed xmlns=""http://www.w3.org/2005/Atom"">
  <entry>
    <id>tag:github.com,2008:Repository/1/mdviewer-v0.6.2</id>
    <updated>2026-12-01T00:00:00Z</updated>
    <title>改善了朗读暂停</title>
  </entry>
</feed>";

            UpdateResult result = UpdateChecker.PickFromAtom(xml, "z/WorkEncourager", "0.5.1", "MdViewer");

            Assert.Null(result.Error);
            Assert.Equal("0.6.2", result.Latest);
            Assert.Equal("mdviewer-v0.6.2", result.Tag);
        }

        [Fact]
        public void atom通道的附件名保留四段版本()
        {
            const string xml = @"<feed xmlns=""http://www.w3.org/2005/Atom"">
  <entry>
    <id>tag:github.com,2008:Repository/1/mdviewer-v0.6.2.7</id>
    <updated>2026-12-01T00:00:00Z</updated>
    <title>mdviewer-v0.6.2.7</title>
  </entry>
</feed>";

            UpdateResult result = UpdateChecker.PickFromAtom(xml, "z/WorkEncourager", "0.5.1", "MdViewer");

            Assert.Null(result.Error);
            // 比较只看三段，附件名要用 tag 里的原始写法，否则拼出来的文件名不存在
            Assert.Equal("MdViewer-0.6.2.7-portable.exe", result.AssetName);
            Assert.Contains("/releases/download/mdviewer-v0.6.2.7/", result.AssetUrl);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("<feed xmlns=\"http://www.w3.org/2005/Atom\"></feed>")]
        [InlineData("这不是 xml")]
        public void atom通道遇到空内容或坏内容不抛异常(string xml)
        {
            UpdateResult result = UpdateChecker.PickFromAtom(xml, "z/WorkEncourager", "0.5.0", "MdViewer");
            Assert.NotNull(result.Error);
            Assert.False(result.HasUpdate);
        }
    }
}
