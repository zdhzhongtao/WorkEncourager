using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace WorkEnc.Core
{
    /// <summary>GitHub Releases 里的一条发布。</summary>
    public sealed class ReleaseEntry
    {
        /// <summary>tag，形如 workenc-v2.2.3。为空表示没找到本程序的发布。</summary>
        public string Tag { get; set; }

        /// <summary>tag 里抠出来的版本号（已归一化到三段）。</summary>
        public Version Version { get; set; }

        /// <summary>发布页地址，检查失败时也能给用户一个手动入口。</summary>
        public string PageUrl { get; set; }

        /// <summary>没找到 / 解析失败的原因，可直接显示给用户。</summary>
        public string Error { get; set; }
    }

    /// <summary>
    /// 从 GitHub Releases 的 atom 里挑「本程序」的发布。
    ///
    /// 全库多个工具共用一个仓库（WadeToolbox），最新一次发布很可能是别的工具的，
    /// 所以不能读 <c>/releases/latest</c>，只能按 tag 前缀筛。
    /// 地址走 github.com 的 releases.atom 而不是 api.github.com：匿名 API 按出口 IP
    /// 每小时只有 60 次，公司 / 机房 / 共用代理的 IP 很容易一早就被用光。
    /// </summary>
    public static class ReleaseFeed
    {
        /// <summary>本程序 Release 的 tag 前缀，必须和 .github/workflows/ci-workenc.yml 的触发条件一致。</summary>
        public const string TagPrefix = "workenc-v";

        public const string RepoPath = "zdhzhongtao/WadeToolbox";

        public const string AtomUrl = "https://github.com/" + RepoPath + "/releases.atom";

        /// <summary>tag 里的版本号：workenc-v2.2.3 / v1.2 / 0.9.1-beta 都能认。</summary>
        private static readonly Regex VersionInText = new Regex(
            @"(\d+(?:\.\d+){0,3})", RegexOptions.Compiled);

        /// <summary>
        /// 从 atom 里挑前缀匹配、版本最高的那条发布。纯函数，不发请求。
        /// atom 只给 tag、时间和发布页地址，没有附件清单 —— 本程序只打开发布页，不需要附件。
        /// </summary>
        public static ReleaseEntry PickLatest(string atomXml, string tagPrefix = TagPrefix)
        {
            var result = new ReleaseEntry();
            string prefix = string.IsNullOrWhiteSpace(tagPrefix) ? TagPrefix : tagPrefix;

            if (string.IsNullOrWhiteSpace(atomXml))
            {
                result.Error = "GitHub 返回的 RSS 是空的。";
                return result;
            }

            XDocument document;
            try
            {
                document = XDocument.Parse(atomXml);
            }
            catch (Exception)
            {
                result.Error = "GitHub 返回的 RSS 解析不了（可能被网络中间层改了）。";
                return result;
            }

            XNamespace atom = "http://www.w3.org/2005/Atom";
            XElement root = document.Root;
            if (root == null)
            {
                result.Error = "GitHub 返回的 RSS 是空的。";
                return result;
            }

            Version best = null;

            foreach (XElement entry in root.Elements(atom + "entry"))
            {
                string tag = TagOf(entry, atom, prefix);
                if (string.IsNullOrEmpty(tag)) continue;

                Version version;
                if (!TryParseVersion(tag, out version)) continue;

                if (best != null && version <= best) continue;

                best = version;
                result.Tag = tag;
                result.Version = version;
                result.PageUrl = PageUrlOf(entry, atom) ?? "https://github.com/" + RepoPath + "/releases/tag/" + tag;
            }

            if (result.Tag == null)
            {
                result.Error = "仓库里还没有本程序的发布（没有 " + prefix + "* 的 Release）。";
            }

            return result;
        }

        /// <summary>tag 是否比当前版本新。tag 可以带 workenc- 前缀。</summary>
        public static bool IsNewer(string tag, string current)
        {
            Version latest;
            Version now;
            if (!TryParseVersion(tag, out latest)) return false;
            if (!TryParseVersion(current, out now)) return false;
            return latest > now;
        }

        /// <summary>从任意文本里抠版本号，统一补成三段（2.2 → 2.2.0）。</summary>
        public static bool TryParseVersion(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            Match match = VersionInText.Match(text);
            if (!match.Success) return false;

            string digits = match.Groups[1].Value;
            int dots = digits.Count(c => c == '.');
            while (dots < 2)
            {
                digits += ".0";
                dots++;
            }

            Version parsed;
            if (!Version.TryParse(digits, out parsed)) return false;

            version = Normalize(parsed);
            return true;
        }

        /// <summary>只比 major.minor.build，避免 2.2.2 和 2.2.2.0 被当成不同版本。</summary>
        public static Version Normalize(Version value)
        {
            if (value == null) return null;
            return new Version(
                Math.Max(0, value.Major),
                Math.Max(0, value.Minor),
                Math.Max(0, Math.Max(0, value.Build)));
        }

        /// <summary>
        /// 取这条 entry 的 tag：标题一般就是 tag（CI 里 gh release create --title "$TAG"），
        /// 被改成人类可读的一句话时退回 id 的末段。不匹配前缀的一律当别的工具的发布。
        /// </summary>
        private static string TagOf(XElement entry, XNamespace atom, string prefix)
        {
            string title = ((string)entry.Element(atom + "title") ?? string.Empty).Trim();
            if (title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return title;

            string id = (string)entry.Element(atom + "id");
            if (string.IsNullOrWhiteSpace(id)) return null;

            int slash = id.LastIndexOf('/');
            if (slash < 0 || slash >= id.Length - 1) return null;

            string fromId = id.Substring(slash + 1).Trim();
            return fromId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? fromId : null;
        }

        /// <summary>atom 里 link rel=alternate 的地址就是发布页。</summary>
        private static string PageUrlOf(XElement entry, XNamespace atom)
        {
            XElement link = entry.Elements(atom + "link")
                .FirstOrDefault(l => string.Equals((string)l.Attribute("rel"), "alternate", StringComparison.OrdinalIgnoreCase));
            return link == null ? null : (string)link.Attribute("href");
        }
    }
}
