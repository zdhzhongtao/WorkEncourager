using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MdViewer.Core
{
    /// <summary>一次「检查更新」的结果。</summary>
    public sealed class UpdateResult
    {
        /// <summary>出错时非空（网络不通、仓库没发版、限流…），可直接显示给用户。</summary>
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

            if (string.IsNullOrWhiteSpace(repo) || repo.IndexOf('/') <= 0)
            {
                result.Error = "仓库地址没配置好，格式应该是 用户名/仓库名。";
                return result;
            }

            // 不能查 /releases/latest：本仓库是多个工具共用的，最新一次发布
            // 可能是别的工具的，那个 Release 里没有 MdViewer 的 exe。
            // 所以拉一页列表自己挑。
            string url = "https://api.github.com/repos/" + repo.Trim().Trim('/')
                         + "/releases?per_page=30";

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
                    if ((int)response.StatusCode == 403)
                    {
                        result.Error = "GitHub 拒绝了请求（可能是查询太频繁被限流），过一会儿再试。";
                        return result;
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        result.Error = "GitHub 返回 " + (int)response.StatusCode + "，暂时查不了。";
                        return result;
                    }

                    string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    return PickBest(json, currentVersion, assetHint);
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
        /// 从 /releases 返回的 JSON 数组里挑出属于本程序、版本最高的那次发布。
        ///
        /// 「属于本程序」的判据：tag 名或附件名里带 <paramref name="assetHint"/>。
        /// 纯函数，不发请求，方便测。任何问题都塞进 Error，不抛异常。
        /// </summary>
        public static UpdateResult PickBest(string releasesJson, string currentVersion, string assetHint)
        {
            var failed = new UpdateResult { Current = currentVersion };
            Version current = ParseCurrent(currentVersion);

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(releasesJson ?? string.Empty);
            }
            catch (JsonException)
            {
                failed.Error = "GitHub 返回的内容看不懂（可能被网络中间层改了）。";
                return failed;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Array)
                {
                    failed.Error = "GitHub 返回的内容格式不对。";
                    return failed;
                }

                UpdateResult best = null;
                Version bestVersion = null;
                UpdateResult bestNoAsset = null;
                Version bestNoAssetVersion = null;

                foreach (var release in root.EnumerateArray())
                {
                    // 草稿和预发布不参与自动更新，跟 /releases/latest 的口径一致
                    if (ReadBool(release, "draft") || ReadBool(release, "prerelease")) continue;

                    string tag = ReadString(release, "tag_name");
                    Version version;
                    if (!TryParseVersion(tag, out version)) continue;
                    version = Normalize(version);

                    bool tagMatched = !string.IsNullOrEmpty(assetHint) && !string.IsNullOrEmpty(tag)
                        && tag.IndexOf(assetHint, StringComparison.OrdinalIgnoreCase) >= 0;

                    JsonElement? asset;
                    string assetName;
                    int score;
                    bool hasAsset = TryPickAsset(release, assetHint, out asset, out assetName, out score);

                    if (!tagMatched && !hasAsset) continue;   // 别的工具的发布，跳过

                    var candidate = Describe(release, currentVersion, current, version, tag, asset, assetName, hasAsset);

                    if (hasAsset)
                    {
                        if (bestVersion == null || version > bestVersion)
                        {
                            best = candidate;
                            bestVersion = version;
                        }
                    }
                    else if (bestNoAssetVersion == null || version > bestNoAssetVersion)
                    {
                        bestNoAsset = candidate;
                        bestNoAssetVersion = version;
                    }
                }

                if (best != null) return best;
                if (bestNoAsset != null) return bestNoAsset;

                failed.Error = "这个仓库里没找到本程序的发布包（看 tag 或附件名都对不上）。";
                return failed;
            }
        }

        private static UpdateResult Describe(JsonElement release, string currentText, Version current,
            Version version, string tag, JsonElement? asset, string assetName, bool hasAsset)
        {
            var result = new UpdateResult
            {
                Current = currentText,
                Tag = tag,
                Latest = version.ToString(),
                Notes = ReadString(release, "body"),
                PageUrl = ReadString(release, "html_url"),
                HasUpdate = IsNewer(version, current)
            };

            string published = ReadString(release, "published_at");
            DateTimeOffset publishedAt;
            if (!string.IsNullOrEmpty(published) &&
                DateTimeOffset.TryParse(published, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out publishedAt))
                result.PublishedAt = publishedAt;

            if (hasAsset && asset.HasValue) Fill(result, asset.Value, assetName);
            return result;
        }

        /// <summary>
        /// 在 release 的附件里挑一个能用的 exe。
        /// 同一个 Release 可能同时挂 local（2MB，依赖运行时）和 portable（60MB，自带运行时），
        /// 自动更新要挑 portable —— 否则装到没运行时的机器上就起不来。
        ///
        /// 返回 false 表示这个 Release 里没有本程序的 exe。
        /// </summary>
        private static bool TryPickAsset(JsonElement release, string hint,
            out JsonElement? best, out string bestName, out int bestScore)
        {
            best = null;
            bestName = null;
            bestScore = -1;

            JsonElement assets;
            if (!release.TryGetProperty("assets", out assets) || assets.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var asset in assets.EnumerateArray())
            {
                string name = ReadString(asset, "name");
                if (string.IsNullOrEmpty(name)) continue;
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                int score = 0;
                if (!string.IsNullOrEmpty(hint) && name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0) score += 2;
                if (name.IndexOf("portable", StringComparison.OrdinalIgnoreCase) >= 0) score += 4;
                if (name.IndexOf("selfcontained", StringComparison.OrdinalIgnoreCase) >= 0) score += 3;

                // 跟本程序完全无关的 exe（别的工具挂在这儿的）不参与
                if (score == 0) continue;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = asset;
                    bestName = name;
                }
            }

            return best.HasValue;
        }

        private static void Fill(UpdateResult result, JsonElement asset, string name)
        {
            result.AssetName = name;
            result.AssetUrl = ReadString(asset, "browser_download_url");
            long size;
            if (asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out size))
                result.AssetSize = size;
        }

        private static string ReadString(JsonElement element, string name)
        {
            JsonElement value;
            if (!element.TryGetProperty(name, out value)) return null;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        private static bool ReadBool(JsonElement element, string name)
        {
            JsonElement value;
            if (!element.TryGetProperty(name, out value)) return false;
            return value.ValueKind == JsonValueKind.True;
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            // GitHub API 强制要求 User-Agent
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MdViewer", "1.0"));
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
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
