using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace MdViewer.Core
{
    /// <summary>一次「检查更新」的结果。</summary>
    public sealed class UpdateResult
    {
        /// <summary>出错时非空（网络不通、仓库没发版…），可直接显示给用户。</summary>
        public string Error { get; set; }

        public string Current { get; set; }
        public string Latest { get; set; }
        public string Tag { get; set; }
        public string Notes { get; set; }
        public string PageUrl { get; set; }
        public string AssetUrl { get; set; }
        public string AssetName { get; set; }
        public long AssetSize { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }

        public bool HasUpdate { get; set; }
        public bool CanDownload { get { return HasUpdate && !string.IsNullOrEmpty(AssetUrl); } }
    }

    /// <summary>
    /// 从 GitHub Releases 取最新版本。
    ///
    /// 本程序其它地方一律不联网，这里是唯一按需发请求的地方：只有用户点了
    /// 「检查更新」（或自己打开了启动时检查）才会调到这里。
    ///
    /// 全程只用一条固定地址：<c>https://github.com/{repo}/releases.atom</c>。
    /// 不碰 api.github.com —— 那个匿名配额按出口 IP 每小时只有 60 次，
    /// 公司、机房、共用代理的 IP 很容易一早就被用光；atom 走的是 github.com
    /// 网页边缘，不占这个额度，而且地址是拼出来的，没有「先问接口要地址」这一步。
    /// </summary>
    public static class UpdateChecker
    {
        /// <summary>从 tag 里抠版本号：mdviewer-v0.3.0 / v1.2 / 0.9.1-beta 都能认。</summary>
        private static readonly Regex VersionInTag = new Regex(
            @"(\d+(?:\.\d+){0,3})", RegexOptions.Compiled);

        public static bool TryParseVersion(string tag, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(tag)) return false;

            var match = VersionInTag.Match(tag);
            if (!match.Success) return false;

            string text = match.Groups[1].Value;
            int dots = text.Count('.');
            while (dots < 2)
            {
                text += ".0";
                dots++;
            }

            Version parsed;
            if (!Version.TryParse(text, out parsed)) return false;
            version = parsed;
            return true;
        }

        /// <summary>本地版本号 → Version。程序集版本可能是 0.3.0.0，取前三段比。</summary>
        public static Version ParseCurrent(string text)
        {
            Version parsed;
            if (!TryParseVersion(text, out parsed)) return new Version(0, 0, 0);
            return Normalize(parsed);
        }

        private static Version Normalize(Version version)
        {
            return new Version(
                Math.Max(0, version.Major),
                Math.Max(0, version.Minor),
                Math.Max(0, Math.Max(0, version.Build)));
        }

        /// <summary>latest 是否比 current 新（只比 major.minor.build）。</summary>
        public static bool IsNewer(Version latest, Version current)
        {
            if (latest == null || current == null) return false;
            return Normalize(latest) > Normalize(current);
        }

        /// <summary>
        /// 查 <paramref name="repo"/>（形如 owner/name）的最新 Release。
        /// 不抛异常：所有失败都塞进 <see cref="UpdateResult.Error"/>。
        /// </summary>
        public static async Task<UpdateResult> CheckAsync(string repo, string currentVersion,
            CancellationToken cancellationToken, string assetHint = "MdViewer")
        {
            var result = new UpdateResult { Current = currentVersion };

            string path = (repo ?? string.Empty).Trim().Trim('/');
            if (path.IndexOf('/') <= 0)
            {
                result.Error = "仓库地址没配置好，格式应该是 用户名/仓库名。";
                return result;
            }

            // 不能改用 /releases/latest：本仓库多个工具共用，最新一次发布
            // 可能是别的工具的。atom 里能按 tag 前缀筛出本程序那几条，所以还是读它。
            string url = "https://github.com/" + path + "/releases.atom";

            try
            {
                using (var client = CreateClient())
                using (var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false))
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        result.Error = "这个仓库还没有发布过 Release（或者仓库名不对）。";
                        return result;
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        result.Error = "GitHub 返回 " + (int)response.StatusCode + "，暂时查不了。";
                        return result;
                    }

                    string xml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    return PickFromAtom(xml, path, currentVersion, assetHint);
                }
            }
            catch (OperationCanceledException)
            {
                result.Error = "检查更新超时了，网络可能不太顺。";
            }
            catch (HttpRequestException ex)
            {
                result.Error = "连不上 GitHub：" + ex.Message;
            }
            catch (Exception ex)
            {
                result.Error = "检查更新失败：" + ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 从 releases.atom 里挑本程序版本最高的那次发布。纯函数，不发请求。
        ///
        /// atom 只给 tag、时间、发布页地址，没有附件清单，所以下载地址按发布约定拼：
        /// <c>{repo}/releases/download/{tag}/{程序名}-{版本}-portable.exe</c>。
        /// 这个约定由 CI 保证（见 .github/workflows/ci-mdviewer.yml 的「整理产物文件名」），
        /// 拼出来的地址直接用，不需要再问一次接口。
        /// </summary>
        public static UpdateResult PickFromAtom(string atomXml, string repo, string currentVersion,
            string assetHint)
        {
            var failed = new UpdateResult { Current = currentVersion };
            Version current = ParseCurrent(currentVersion);

            XDocument document;
            try
            {
                document = XDocument.Parse(atomXml ?? string.Empty);
            }
            catch (Exception)
            {
                failed.Error = "GitHub 返回的 RSS 解析不了（可能被网络中间层改了）。";
                return failed;
            }

            XNamespace atom = "http://www.w3.org/2005/Atom";
            XElement root = document.Root;
            if (root == null)
            {
                failed.Error = "GitHub 返回的 RSS 是空的。";
                return failed;
            }

            Version bestVersion = null;
            string bestTag = null;
            string bestVersionText = null;
            DateTimeOffset? bestPublished = null;

            foreach (XElement entry in root.Elements(atom + "entry"))
            {
                string title = ((string)entry.Element(atom + "title") ?? string.Empty).Trim();

                // title 一般就是 tag（CI 里 gh release create --title "$TAG"）；
                // 万一被改成人类可读的一句话，就退回 id 的末段。
                string tag = LooksLikeOurTag(title, assetHint)
                    ? title
                    : TagFromEntryId((string)entry.Element(atom + "id"));
                if (string.IsNullOrEmpty(tag)) continue;

                // 一个仓库挂着好几个工具的发布，只认标题里带本程序名的
                if (!string.IsNullOrEmpty(assetHint) &&
                    tag.IndexOf(assetHint, StringComparison.OrdinalIgnoreCase) < 0) continue;

                Version version;
                if (!TryParseVersion(tag, out version)) continue;
                version = Normalize(version);

                if (bestVersion != null && version <= bestVersion) continue;

                bestVersion = version;
                bestTag = tag;
                bestVersionText = VersionTextInTag(tag) ?? version.ToString();
                bestPublished = ReadEntryTime(entry, atom);
            }

            if (bestVersion == null)
            {
                failed.Error = "这个仓库里没找到本程序的发布（RSS 只列最近 10 次，可能被别的工具的发布挤出去了）。";
                return failed;
            }

            string repoPath = (repo ?? string.Empty).Trim().Trim('/');
            string assetName = string.IsNullOrEmpty(assetHint)
                ? null
                : assetHint + "-" + bestVersionText + "-portable.exe";

            return new UpdateResult
            {
                Current = currentVersion,
                Tag = bestTag,
                Latest = bestVersion.ToString(),
                PageUrl = "https://github.com/" + repoPath + "/releases/tag/" + bestTag,
                AssetName = assetName,
                AssetUrl = assetName == null
                    ? null
                    : "https://github.com/" + repoPath + "/releases/download/" + bestTag + "/" + assetName,
                PublishedAt = bestPublished,
                HasUpdate = IsNewer(bestVersion, current)
            };
        }

        /// <summary>tag 里的原始版本串（四段也保留），拼附件名要用它，不能用归一化后的三段。</summary>
        private static string VersionTextInTag(string tag)
        {
            Match match = VersionInTag.Match(tag ?? string.Empty);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>这段文字像不像本程序的 tag：带程序名、带版本号。不像就当它是人类可读的标题。</summary>
        private static bool LooksLikeOurTag(string text, string assetHint)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (!string.IsNullOrEmpty(assetHint) &&
                text.IndexOf(assetHint, StringComparison.OrdinalIgnoreCase) < 0) return false;

            Version ignored;
            return TryParseVersion(text, out ignored);
        }

        /// <summary>atom 的 id 形如 tag:github.com,2008:Repository/123/mdviewer-v0.5.1，末段就是 tag。</summary>
        private static string TagFromEntryId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            int slash = id.LastIndexOf('/');
            return slash >= 0 && slash < id.Length - 1 ? id.Substring(slash + 1) : null;
        }

        private static DateTimeOffset? ReadEntryTime(XElement entry, XNamespace atom)
        {
            string text = (string)entry.Element(atom + "updated");
            DateTimeOffset parsed;
            if (!string.IsNullOrEmpty(text) &&
                DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out parsed))
                return parsed;
            return null;
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MdViewer", "1.0"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/atom+xml"));
            return client;
        }

        /// <summary>下载新版本到本地文件。返回 null 表示成功，否则是错误说明。</summary>
        public static async Task<string> DownloadAsync(string url, string destPath,
            IProgress<double> progress, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url)) return "没有可下载的地址。";

            try
            {
                string dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
                using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead,
                           cancellationToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        return "下载失败：HTTP " + (int)response.StatusCode;

                    long total = response.Content.Headers.ContentLength.HasValue
                        ? response.Content.Headers.ContentLength.Value
                        : -1;

                    using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                    using (var output = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[81920];
                        long done = 0;
                        int read;
                        while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                            done += read;
                            if (progress != null && total > 0) progress.Report((double)done / total);
                        }
                    }
                }

                var info = new FileInfo(destPath);
                if (!info.Exists || info.Length < 64 * 1024)
                    return "下载下来的文件不对劲（太小），已放弃。";
                if (!LooksLikeExe(destPath))
                    return "下载下来的文件不是可执行程序，已放弃。";

                return null;
            }
            catch (OperationCanceledException)
            {
                return "下载被取消或超时。";
            }
            catch (Exception ex)
            {
                return "下载失败：" + ex.Message;
            }
        }

        /// <summary>看头两个字节是不是 MZ，避免把网页错误页当成 exe 存下来。</summary>
        public static bool LooksLikeExe(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    int first = stream.ReadByte();
                    int second = stream.ReadByte();
                    return first == 'M' && second == 'Z';
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
