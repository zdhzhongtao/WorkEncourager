using System;
using System.Net.Http;
using System.Threading.Tasks;
using WorkEnc.Core;

namespace WorkEncourager
{
    /// <summary>
    /// 版本更新检查：读 GitHub 仓库的 releases.atom，筛出本程序的发布（workenc-v*）。
    ///
    /// 全库多个工具共用一个仓库（WadeToolbox），最新一次发布可能是别的工具的，
    /// 所以不能读 /releases/latest —— 那样会拿到 mdviewer 的 tag，版本号解析失败，
    /// 于是永远显示「已是最新」，既不报错也不再提示。
    /// 网络不通 / 仓库没发 Release 都只是"检查失败"，绝不弹窗打扰。
    /// </summary>
    public static class UpdateChecker
    {
        /// <summary>当前程序版本（如 2.0.2）。</summary>
        public static string CurrentVersion
        {
            get
            {
                var v = typeof(UpdateChecker).Assembly.GetName().Version;
                if (v == null) return "0.0.0";

                Version normalized = ReleaseFeed.Normalize(v);
                return normalized.Major + "." + normalized.Minor + "." + normalized.Build;
            }
        }

        /// <summary>仓库发布页，检查失败时给用户一个手动入口。</summary>
        public const string RepoUrl = "https://github.com/" + ReleaseFeed.RepoPath + "/releases";

        /// <summary>
        /// 取本程序最新 Release 的 tag 和页面地址。
        /// 返回 tag 为 null 表示失败，error 里是原因。
        /// </summary>
        public static async Task<(string tag, string url, string error)> GetLatestAsync(int timeoutSeconds = 8)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("WorkEncourager-Updater");

                string xml = await http.GetStringAsync(ReleaseFeed.AtomUrl).ConfigureAwait(false);

                ReleaseEntry entry = ReleaseFeed.PickLatest(xml);
                if (entry.Tag == null)
                {
                    return (null, null, entry.Error ?? "没找到本程序的发布");
                }

                return (entry.Tag, entry.PageUrl, null);
            }
            catch (TaskCanceledException)
            {
                return (null, null, "请求超时（国内访问 GitHub 可能较慢）");
            }
            catch (Exception ex)
            {
                return (null, null, ex.Message);
            }
        }

        /// <summary>tag 是否比当前版本新。tag 可以是 workenc-v2.1.0 或 2.1.0。</summary>
        public static bool IsNewer(string tag, string current) => ReleaseFeed.IsNewer(tag, current);

        /// <summary>给人看的版本名：workenc-v2.2.3 显示成 v2.2.3，认不出前缀就原样返回。</summary>
        public static string DisplayVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return tag;
            if (tag.StartsWith(ReleaseFeed.TagPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return "v" + tag.Substring(ReleaseFeed.TagPrefix.Length);
            }
            return tag;
        }
    }
}
