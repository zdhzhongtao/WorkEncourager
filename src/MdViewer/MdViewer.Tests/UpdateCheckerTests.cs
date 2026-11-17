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

        // ---------- 从 /releases 列表里挑（本仓库和别的工具共用，必须挑对） ----------

        /// <summary>造一条 release JSON，assets 用 "名字:大小" 描述。</summary>
        private static string Release(string tag, bool draft = false, bool prerelease = false,
            params string[] assets)
        {
            var items = new System.Collections.Generic.List<string>();
            foreach (string asset in assets)
            {
                string name = asset;
                long size = 1024;
                int colon = asset.LastIndexOf(':');
                if (colon > 0)
                {
                    name = asset.Substring(0, colon);
                    size = long.Parse(asset.Substring(colon + 1));
                }
                items.Add("{\"name\":\"" + name + "\",\"size\":" + size
                          + ",\"browser_download_url\":\"https://example.com/" + name + "\"}");
            }

            return "{\"tag_name\":\"" + tag + "\","
                   + "\"draft\":" + (draft ? "true" : "false") + ","
                   + "\"prerelease\":" + (prerelease ? "true" : "false") + ","
                   + "\"body\":\"更新说明 " + tag + "\","
                   + "\"html_url\":\"https://github.com/x/y/releases/tag/" + tag + "\","
                   + "\"published_at\":\"2026-01-02T03:04:05Z\","
                   + "\"assets\":[" + string.Join(",", items) + "]}";
        }

        private static string List(params string[] releases)
        {
            return "[" + string.Join(",", releases) + "]";
        }

        [Fact]
        public void 别的工具的发布不会被当成本程序的更新()
        {
            // 最新一次发布是 WorkEnc 的，没挂 MdViewer 的 exe
            string json = List(
                Release("workenc-v0.9.9", false, false, "WorkEncourager-0.9.9.exe"),
                Release("mdviewer-v0.3.0", false, false,
                    "MdViewer-0.3.0-local.exe", "MdViewer-0.3.0-portable.exe"));

            var result = UpdateChecker.PickBest(json, "0.2.0", "MdViewer");

            Assert.Null(result.Error);
            Assert.Equal("mdviewer-v0.3.0", result.Tag);
            Assert.Equal("0.3.0", result.Latest);
            Assert.True(result.HasUpdate);
            Assert.Equal("MdViewer-0.3.0-portable.exe", result.AssetName);
        }

        [Fact]
        public void 自动更新优先拿portable版()
        {
            // 故意把 local 放在前面，避免「取第一个」的写法蒙对
            string json = List(Release("mdviewer-v0.4.0", false, false,
                "MdViewer-0.4.0-local.exe:2200000",
                "MdViewer-0.4.0-portable.exe:62000000"));

            var result = UpdateChecker.PickBest(json, "0.3.0", "MdViewer");

            Assert.Equal("MdViewer-0.4.0-portable.exe", result.AssetName);
            Assert.Equal(62000000, result.AssetSize);
            Assert.True(result.CanDownload);
        }

        [Fact]
        public void 挑版本最高的那次而不是列表里第一条()
        {
            string json = List(
                Release("mdviewer-v0.3.5", false, false, "MdViewer-0.3.5-portable.exe"),
                Release("mdviewer-v0.4.0", false, false, "MdViewer-0.4.0-portable.exe"));

            var result = UpdateChecker.PickBest(json, "0.3.0", "MdViewer");

            Assert.Equal("0.4.0", result.Latest);
            Assert.Equal("MdViewer-0.4.0-portable.exe", result.AssetName);
        }

        [Fact]
        public void 草稿和预发布不参与自动更新()
        {
            string json = List(
                Release("mdviewer-v0.9.0", true, false, "MdViewer-0.9.0-portable.exe"),   // 草稿
                Release("mdviewer-v0.8.0", false, true, "MdViewer-0.8.0-portable.exe"),    // 预发布
                Release("mdviewer-v0.3.1", false, false, "MdViewer-0.3.1-portable.exe"));

            var result = UpdateChecker.PickBest(json, "0.3.0", "MdViewer");

            Assert.Equal("0.3.1", result.Latest);
        }

        [Fact]
        public void 版本没更新时HasUpdate是false但仍给出信息()
        {
            string json = List(Release("mdviewer-v0.3.0", false, false, "MdViewer-0.3.0-portable.exe"));

            var result = UpdateChecker.PickBest(json, "0.3.0", "MdViewer");

            Assert.False(result.HasUpdate);
            Assert.False(result.CanDownload);
            Assert.Equal("mdviewer-v0.3.0", result.Tag);
            Assert.NotNull(result.PageUrl);
        }

        [Fact]
        public void 只有说明没有附件时不让下载但提示有新版本()
        {
            string json = List(Release("mdviewer-v0.5.0"));

            var result = UpdateChecker.PickBest(json, "0.3.0", "MdViewer");

            Assert.True(result.HasUpdate);
            Assert.Null(result.AssetUrl);
            Assert.False(result.CanDownload);
            Assert.Equal("mdviewer-v0.5.0", result.Tag);
        }

        [Fact]
        public void 仓库里全是别的工具的发布就报人话错误()
        {
            string json = List(Release("workenc-v1.0.0", false, false, "WorkEncourager-1.0.0.exe"));

            var result = UpdateChecker.PickBest(json, "0.3.0", "MdViewer");

            Assert.NotNull(result.Error);
            Assert.False(result.HasUpdate);
        }

        [Fact]
        public void 空列表和烂json都不抛异常()
        {
            var empty = UpdateChecker.PickBest("[]", "0.3.0", "MdViewer");
            Assert.NotNull(empty.Error);

            var broken = UpdateChecker.PickBest("<html>502</html>", "0.3.0", "MdViewer");
            Assert.NotNull(broken.Error);

            var nullJson = UpdateChecker.PickBest(null, "0.3.0", "MdViewer");
            Assert.NotNull(nullJson.Error);
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
    }
}
