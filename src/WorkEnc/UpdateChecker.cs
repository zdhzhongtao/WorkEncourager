using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace WorkEncourager
{
    /// <summary>
    /// 版本更新检查：读 GitHub 仓库的最新 Release，和当前程序集版本比。
    /// 网络不通 / 仓库没发 Release 都只是"检查失败"，绝不弹窗打扰。
    /// </summary>
    public static class UpdateChecker
    {
        private const string ApiUrl =
            "https://api.github.com/repos/zdhzhongtao/WorkEncourager/releases/latest";

        /// <summary>当前程序版本（如 2.0.2）。</summary>
        public static string CurrentVersion
        {
            get
            {
                var v = typeof(UpdateChecker).Assembly.GetName().Version;
                return v == null ? "0.0.0" : Normalize(v).ToString(3);
            }
        }

        /// <summary>仓库主页，检查失败时给用户一个手动入口。</summary>
        public const string RepoUrl = "https://github.com/zdhzhongtao/WorkEncourager/releases";

        /// <summary>
        /// 取最新 Release 的 tag 和页面地址。
        /// 返回 tag 为 null 表示失败，error 里是原因。
        /// </summary>
        public static async Task<(string tag, string url, string error)> GetLatestAsync(int timeoutSeconds = 8)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("WorkEncourager-Updater");
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                string json = await http.GetStringAsync(ApiUrl);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                string url = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;

                if (string.IsNullOrWhiteSpace(tag)) return (null, null, "Release 里没有 tag_name");
                return (tag, url ?? RepoUrl, null);
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

        /// <summary>tag 是否比当前版本新。tag 可以是 v2.1.0 或 2.1.0。</summary>
        public static bool IsNewer(string tag, string current)
        {
            var a = ParseVersion(tag);
            var b = ParseVersion(current);
            if (a == null || b == null) return false;
            return a > b;
        }

        private static Version ParseVersion(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim().TrimStart('v', 'V');

            // 去掉 -beta / +build 之类的后缀
            int cut = s.IndexOfAny(new[] { '-', '+' });
            if (cut > 0) s = s.Substring(0, cut);

            return Version.TryParse(s, out var v) ? Normalize(v) : null;
        }

        /// <summary>补齐到四段，避免 2.0.2 和 2.0.2.0 被当成不同版本。</summary>
        private static Version Normalize(Version v) =>
            new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
    }
}
