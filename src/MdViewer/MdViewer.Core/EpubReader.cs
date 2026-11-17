using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace MdViewer.Core
{
    /// <summary>一本已解析完的 EPUB：正文已经拼成一段 html，目录也整理好了。</summary>
    public sealed class EpubDocument
    {
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;

        /// <summary>整本书的正文 html（每章一个 &lt;section id="cN"&gt;）。</summary>
        public string BodyHtml { get; set; } = string.Empty;

        public List<TocItem> Toc { get; set; } = new List<TocItem>();

        /// <summary>解压出来的资源目录，图片从这里加载。</summary>
        public string RootDir { get; set; } = string.Empty;

        public int ChapterCount { get; set; }

        /// <summary>解析失败被跳过的章节数（格式有问题的书会有）。</summary>
        public int SkippedChapters { get; set; }
    }

    /// <summary>
    /// 极简 EPUB 2/3 阅读支持：解压 → 读 OPF → 按 spine 顺序把每章 body 抠出来 → 拼成一整页。
    ///
    /// 为什么不保留书里的 CSS：绝大多数 epub 是给 6 寸电子墨水屏做的，字体、行高、缩进
    /// 全是死值，套到桌面窗口里往往更难读。统一用本站的排版规则，观感和 md 保持一致。
    /// 代价是出版社的花哨版式会丢，这是有意的取舍。
    /// </summary>
    public static class EpubReader
    {
        /// <summary>单个条目上限，防止一个塞满料的 zip 把内存吃穿。</summary>
        private const long MaxEntryBytes = 96L * 1024 * 1024;

        /// <summary>解压后的总量上限。</summary>
        private const long MaxTotalBytes = 512L * 1024 * 1024;

        private static readonly Regex BodyBlock = new Regex(
            @"<body\b[^>]*>(.*?)</body\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex HeadBlock = new Regex(
            @"<head\b[^>]*>.*?</head\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex StyleBlock = new Regex(
            @"<(style|link|base|meta|title)\b[^>]*?(/>|>.*?</\1\s*>)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex UrlAttribute = new Regex(
            @"\b(src|href|xlink:href|poster)\s*=\s*(?:""([^""]*)""|'([^']*)')",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex IdAttribute = new Regex(
            @"\bid\s*=\s*(?:""([^""]*)""|'([^']*)')",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex NameAnchor = new Regex(
            @"<a\b([^>]*?)\bname\s*=\s*(?:""([^""]*)""|'([^']*)')",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex HasHeading = new Regex(
            @"<h[1-3]\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private sealed class ManifestItem
        {
            public string Id { get; set; }
            public string Href { get; set; }
            public string MediaType { get; set; }
            public string Properties { get; set; }

            /// <summary>相对 zip 根的路径（已规范化）。</summary>
            public string ZipPath { get; set; }
        }

        private sealed class TocLink
        {
            public string Text { get; set; }
            public string Href { get; set; }
            public int Level { get; set; }
        }

        public static bool IsEpubFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string ext = Path.GetExtension(path) ?? string.Empty;
            return ext.Equals(".epub", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 解析一本 epub。资源解压到 <paramref name="extractDir"/>，
        /// 正文里的图片写成 <paramref name="contentOrigin"/> 开头的绝对地址
        /// （主程序会给这个域名做本地目录映射）。
        ///
        /// <paramref name="reuseExtracted"/> 为 true 且目录里已经有东西时跳过解压，
        /// 用于同一本书反复打开（切主题、刷新）不重复解压。
        /// </summary>
        public static EpubDocument Open(string epubPath, string extractDir, string contentOrigin,
            bool reuseExtracted = false)
        {
            if (!File.Exists(epubPath))
                throw new FriendlyException("找不到这个 EPUB：" + epubPath);

            string root = Path.GetFullPath(extractDir);
            Directory.CreateDirectory(root);

            bool alreadyThere = reuseExtracted && HasContent(root);
            if (!alreadyThere)
            {
                ClearDirectory(root);
            }

            using (var stream = new FileStream(epubPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var entries = IndexEntries(zip);
                if (!alreadyThere) ExtractAll(entries, root);

                string opfPath = FindPackagePath(entries);
                if (opfPath == null)
                    throw new FriendlyException("这个 EPUB 结构不完整：找不到 container.xml 或 .opf 文件。");

                XDocument opf = LoadXml(ReadEntry(entries, opfPath));
                string opfDir = DirOf(opfPath);

                var document = new EpubDocument { RootDir = root };
                ReadMetadata(opf, document);

                var manifest = ReadManifest(opf, opfDir);
                var spine = ReadSpine(opf, manifest);
                if (spine.Count == 0)
                    throw new FriendlyException("这个 EPUB 没有可读的章节（spine 是空的）。");

                var chapterOfFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < spine.Count; i++) chapterOfFile[spine[i].ZipPath] = i + 1;

                var links = ReadToc(opf, opfDir, entries, manifest, opfPath);

                var body = new StringBuilder(64 * 1024);
                var chapters = new List<string>(spine.Count);

                for (int i = 0; i < spine.Count; i++)
                {
                    ManifestItem item = spine[i];
                    int number = i + 1;
                    string title = TitleFor(links, item.ZipPath, number);

                    string html;
                    try
                    {
                        html = ChapterHtml(entries, item.ZipPath, number, chapterOfFile, contentOrigin);
                    }
                    catch (Exception)
                    {
                        document.SkippedChapters++;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(html)) continue;

                    body.Append("<section class=\"epub-chapter\" id=\"c").Append(number).Append("\">\r\n");
                    if (!HasHeading.IsMatch(html))
                        body.Append("<h2 class=\"epub-chapter-title\">").Append(WebUtilityEncode(title)).Append("</h2>\r\n");
                    body.Append(html).Append("\r\n</section>\r\n");

                    chapters.Add(title);
                }

                document.BodyHtml = body.ToString();
                document.ChapterCount = chapters.Count;
                document.Toc = BuildToc(links, chapterOfFile, chapters);

                if (document.ChapterCount == 0)
                    throw new FriendlyException("这个 EPUB 的章节都读不出内容，可能用了不支持的排版格式。");

                return document;
            }
        }

        // ───────────────────────── zip ─────────────────────────

        private static Dictionary<string, ZipArchiveEntry> IndexEntries(ZipArchive zip)
        {
            var map = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                string name = (entry.FullName ?? string.Empty).Replace('\\', '/').TrimStart('/');
                if (name.Length == 0) continue;
                if (name.EndsWith("/", StringComparison.Ordinal)) continue;
                if (!map.ContainsKey(name)) map[name] = entry;
            }
            return map;
        }

        private static void ExtractAll(Dictionary<string, ZipArchiveEntry> entries, string root)
        {
            long total = 0;
            string prefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root
                : root + Path.DirectorySeparatorChar;

            foreach (var pair in entries)
            {
                var entry = pair.Value;
                if (entry.Length > MaxEntryBytes) continue;

                string target;
                try { target = Path.GetFullPath(Path.Combine(root, pair.Key.Replace('/', Path.DirectorySeparatorChar))); }
                catch (Exception) { continue; }

                // 防止 ../../ 把文件写到缓存目录外面
                if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                total += entry.Length;
                if (total > MaxTotalBytes) break;

                string dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                try
                {
                    using (var input = entry.Open())
                    using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                        input.CopyTo(output, 64 * 1024);
                }
                catch (Exception)
                {
                    // 单张图或单个字体坏了不该毁掉整本书
                }
            }
        }

        private static void ClearDirectory(string dir)
        {
            try
            {
                foreach (var file in Directory.GetFiles(dir)) File.Delete(file);
                foreach (var sub in Directory.GetDirectories(dir)) Directory.Delete(sub, true);
            }
            catch (Exception)
            {
                // 清不掉就算了，文件会被同名覆盖
            }
        }

        private static bool HasContent(string dir)
        {
            try
            {
                foreach (var item in Directory.EnumerateFileSystemEntries(dir)) return true;
            }
            catch (Exception)
            {
                return false;
            }
            return false;
        }

        private static string FindPackagePath(Dictionary<string, ZipArchiveEntry> entries)
        {
            ZipArchiveEntry container;
            if (entries.TryGetValue("META-INF/container.xml", out container))
            {
                try
                {
                    XDocument doc = LoadXml(ReadEntryBytes(container));
                    foreach (var element in doc.Descendants())
                    {
                        if (!string.Equals(element.Name.LocalName, "rootfile", StringComparison.OrdinalIgnoreCase)) continue;
                        var attribute = element.Attribute("full-path");
                        if (attribute != null && !string.IsNullOrWhiteSpace(attribute.Value))
                            return NormalizeZipPath(attribute.Value);
                    }
                }
                catch (Exception)
                {
                    // 落到下面的兜底逻辑
                }
            }

            foreach (var pair in entries)
            {
                if (pair.Key.EndsWith(".opf", StringComparison.OrdinalIgnoreCase)) return pair.Key;
            }
            return null;
        }

        private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
        {
            using (var input = entry.Open())
            using (var memory = new MemoryStream())
            {
                input.CopyTo(memory);
                return memory.ToArray();
            }
        }

        private static byte[] ReadEntry(Dictionary<string, ZipArchiveEntry> entries, string path)
        {
            ZipArchiveEntry entry;
            if (!entries.TryGetValue(path, out entry))
                throw new FriendlyException("EPUB 里缺少文件：" + path);
            return ReadEntryBytes(entry);
        }

        private static XDocument LoadXml(byte[] bytes)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                IgnoreWhitespace = false,
                CheckCharacters = false
            };
            using (var memory = new MemoryStream(bytes))
            using (var reader = XmlReader.Create(memory, settings))
                return XDocument.Load(reader, LoadOptions.None);
        }

        // ───────────────────────── OPF ─────────────────────────

        private static void ReadMetadata(XDocument opf, EpubDocument document)
        {
            foreach (var element in opf.Descendants())
            {
                string name = element.Name.LocalName;
                if (string.Equals(name, "title", StringComparison.OrdinalIgnoreCase) && document.Title.Length == 0)
                    document.Title = (element.Value ?? string.Empty).Trim();
                else if (string.Equals(name, "creator", StringComparison.OrdinalIgnoreCase) && document.Author.Length == 0)
                    document.Author = (element.Value ?? string.Empty).Trim();
                else if (string.Equals(name, "language", StringComparison.OrdinalIgnoreCase) && document.Language.Length == 0)
                    document.Language = (element.Value ?? string.Empty).Trim();
            }
        }

        private static Dictionary<string, ManifestItem> ReadManifest(XDocument opf, string opfDir)
        {
            var map = new Dictionary<string, ManifestItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var element in opf.Descendants())
            {
                if (!string.Equals(element.Name.LocalName, "item", StringComparison.OrdinalIgnoreCase)) continue;

                string id = Attribute(element, "id");
                string href = Attribute(element, "href");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(href)) continue;

                string zipPath = NormalizeZipPath(Combine(opfDir, Unescape(href)));
                map[id] = new ManifestItem
                {
                    Id = id,
                    Href = href,
                    MediaType = Attribute(element, "media-type") ?? string.Empty,
                    Properties = Attribute(element, "properties") ?? string.Empty,
                    ZipPath = zipPath
                };
            }
            return map;
        }

        private static List<ManifestItem> ReadSpine(XDocument opf, Dictionary<string, ManifestItem> manifest)
        {
            var list = new List<ManifestItem>();
            foreach (var element in opf.Descendants())
            {
                if (!string.Equals(element.Name.LocalName, "itemref", StringComparison.OrdinalIgnoreCase)) continue;
                string idref = Attribute(element, "idref");
                if (string.IsNullOrWhiteSpace(idref)) continue;

                ManifestItem item;
                if (!manifest.TryGetValue(idref, out item)) continue;
                if (!IsDocument(item)) continue;
                list.Add(item);
            }
            return list;
        }

        private static bool IsDocument(ManifestItem item)
        {
            string type = item.MediaType ?? string.Empty;
            if (type.IndexOf("xhtml", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (type.IndexOf("html", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            string path = item.ZipPath ?? string.Empty;
            return path.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);
        }

        // ───────────────────────── 目录 ─────────────────────────

        private static List<TocLink> ReadToc(XDocument opf, string opfDir,
            Dictionary<string, ZipArchiveEntry> entries, Dictionary<string, ManifestItem> manifest, string opfPath)
        {
            // EPUB3：manifest 里 properties 含 nav 的 xhtml
            foreach (var item in manifest.Values)
            {
                if ((item.Properties ?? string.Empty).IndexOf("nav", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var links = ReadNavDocument(entries, item.ZipPath);
                if (links.Count > 0) return links;
            }

            // EPUB2：toc 属性指向 ncx
            string ncxId = Attribute(FindElement(opf, "spine"), "toc");
            ManifestItem ncx = null;
            if (!string.IsNullOrWhiteSpace(ncxId)) manifest.TryGetValue(ncxId, out ncx);
            if (ncx == null)
            {
                foreach (var item in manifest.Values)
                {
                    if ((item.MediaType ?? string.Empty).IndexOf("dtbncx", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        ncx = item;
                        break;
                    }
                }
            }
            if (ncx != null)
            {
                var links = ReadNcx(entries, ncx.ZipPath);
                if (links.Count > 0) return links;
            }

            return new List<TocLink>();
        }

        private static List<TocLink> ReadNavDocument(Dictionary<string, ZipArchiveEntry> entries, string path)
        {
            var links = new List<TocLink>();
            if (string.IsNullOrEmpty(path) || !entries.ContainsKey(path)) return links;

            XDocument doc;
            try { doc = LoadXml(ReadEntry(entries, path)); }
            catch (Exception) { return links; }

            XElement nav = null;
            foreach (var element in doc.Descendants())
            {
                if (!string.Equals(element.Name.LocalName, "nav", StringComparison.OrdinalIgnoreCase)) continue;
                string type = Attribute(element, "type") ?? string.Empty;
                if (type.IndexOf("toc", StringComparison.OrdinalIgnoreCase) >= 0) { nav = element; break; }
                if (nav == null) nav = element;
            }
            if (nav == null) return links;

            string baseDir = DirOf(path);
            foreach (var anchor in nav.Descendants())
            {
                if (!string.Equals(anchor.Name.LocalName, "a", StringComparison.OrdinalIgnoreCase)) continue;
                string href = Attribute(anchor, "href");
                if (string.IsNullOrWhiteSpace(href)) continue;

                links.Add(new TocLink
                {
                    Text = Collapse(anchor.Value),
                    Href = ResolveHref(baseDir, href),
                    Level = Math.Max(1, DepthOf(anchor, "ol"))
                });
            }
            return links;
        }

        private static List<TocLink> ReadNcx(Dictionary<string, ZipArchiveEntry> entries, string path)
        {
            var links = new List<TocLink>();
            if (string.IsNullOrEmpty(path) || !entries.ContainsKey(path)) return links;

            XDocument doc;
            try { doc = LoadXml(ReadEntry(entries, path)); }
            catch (Exception) { return links; }

            string baseDir = DirOf(path);
            WalkNavPoints(doc.Root, 1, baseDir, links);
            return links;
        }

        private static void WalkNavPoints(XElement element, int level, string baseDir, List<TocLink> links)
        {
            if (element == null || links.Count > 2000) return;
            foreach (var child in element.Elements())
            {
                if (string.Equals(child.Name.LocalName, "navPoint", StringComparison.OrdinalIgnoreCase))
                {
                    string text = string.Empty;
                    string src = string.Empty;
                    foreach (var node in child.Descendants())
                    {
                        string name = node.Name.LocalName;
                        if (string.Equals(name, "text", StringComparison.OrdinalIgnoreCase) && text.Length == 0)
                            text = Collapse(node.Value);
                        else if (string.Equals(name, "content", StringComparison.OrdinalIgnoreCase) && src.Length == 0)
                            src = Attribute(node, "src") ?? string.Empty;
                    }

                    if (!string.IsNullOrWhiteSpace(src))
                    {
                        links.Add(new TocLink
                        {
                            Text = text,
                            Href = ResolveHref(baseDir, src),
                            Level = level
                        });
                    }
                    WalkNavPoints(child, level + 1, baseDir, links);
                }
                else
                {
                    WalkNavPoints(child, level, baseDir, links);
                }
            }
        }

        private static List<TocItem> BuildToc(List<TocLink> links, Dictionary<string, int> chapterOfFile, List<string> chapterTitles)
        {
            var toc = new List<TocItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var link in links)
            {
                if (string.IsNullOrWhiteSpace(link.Href)) continue;
                string file = link.Href;
                string fragment = string.Empty;
                int hash = file.IndexOf('#');
                if (hash >= 0)
                {
                    fragment = file.Substring(hash + 1);
                    file = file.Substring(0, hash);
                }

                int number;
                if (!chapterOfFile.TryGetValue(file, out number))
                {
                    if (string.IsNullOrEmpty(file))
                    {
                        // 只有 #锚点，指向本书开头，当作第一章
                        number = chapterOfFile.Count > 0 ? 1 : 0;
                    }
                    else continue;
                }
                if (number <= 0) continue;

                string id = "c" + number.ToString(CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(fragment)) id += "-" + fragment;
                if (!seen.Add(id)) continue;

                string text = link.Text;
                if (string.IsNullOrWhiteSpace(text) && number - 1 < chapterTitles.Count)
                    text = chapterTitles[number - 1];

                toc.Add(new TocItem
                {
                    Level = Math.Max(1, link.Level),
                    Text = string.IsNullOrWhiteSpace(text) ? "第 " + number + " 章" : text,
                    Id = id
                });
            }

            if (toc.Count > 0) return toc;

            // 书里没提供目录：拿每章标题凑一个
            for (int i = 0; i < chapterTitles.Count; i++)
            {
                toc.Add(new TocItem
                {
                    Level = 1,
                    Text = string.IsNullOrWhiteSpace(chapterTitles[i]) ? "第 " + (i + 1) + " 章" : chapterTitles[i],
                    Id = "c" + (i + 1).ToString(CultureInfo.InvariantCulture)
                });
            }
            return toc;
        }

        private static string TitleFor(List<TocLink> links, string zipPath, int number)
        {
            foreach (var link in links)
            {
                string file = link.Href ?? string.Empty;
                int hash = file.IndexOf('#');
                if (hash >= 0) file = file.Substring(0, hash);
                if (string.Equals(file, zipPath, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(link.Text))
                    return link.Text;
            }
            return "第 " + number + " 章";
        }

        // ───────────────────────── 章节正文 ─────────────────────────

        private static string ChapterHtml(Dictionary<string, ZipArchiveEntry> entries, string zipPath,
            int number, Dictionary<string, int> chapterOfFile, string contentOrigin)
        {
            ZipArchiveEntry entry;
            if (!entries.TryGetValue(zipPath, out entry)) return null;

            string raw = Decode(ReadEntryBytes(entry));
            if (string.IsNullOrWhiteSpace(raw)) return null;

            string body = raw;
            var match = BodyBlock.Match(raw);
            if (match.Success) body = match.Groups[1].Value;
            else body = HeadBlock.Replace(raw, string.Empty);

            // 书里的样式表和 <style> 一律丢掉，统一用本站的排版
            body = StyleBlock.Replace(body, string.Empty);
            body = HtmlSanitizer.Clean(body);

            string prefix = "c" + number.ToString(CultureInfo.InvariantCulture) + "-";

            // 章节合并到一页以后 id 会互相撞车，统一加章号前缀
            body = IdAttribute.Replace(body, m =>
            {
                string value = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                if (string.IsNullOrEmpty(value)) return m.Value;
                if (value.StartsWith(prefix, StringComparison.Ordinal)) return m.Value;
                return "id=\"" + prefix + value + "\"";
            });

            // <a name="x"> 这种旧式锚点改成 id，跳转才有效
            body = NameAnchor.Replace(body, m =>
            {
                string attrs = m.Groups[1].Value ?? string.Empty;
                string value = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
                string clean = Regex.Replace(attrs, @"\bname\s*=\s*(?:""[^""]*""|'[^']*')", string.Empty).Trim();
                string id = string.IsNullOrEmpty(value) ? string.Empty : " id=\"" + prefix + value + "\"";
                return "<a" + (clean.Length > 0 ? " " + clean : string.Empty) + id;
            });

            string chapterDir = DirOf(zipPath);

            body = UrlAttribute.Replace(body, m =>
            {
                string name = m.Groups[1].Value;
                string value = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
                string rewritten = RewriteUrl(name, value, chapterDir, prefix, chapterOfFile, contentOrigin);
                return name + "=\"" + rewritten + "\"";
            });

            return body.Trim();
        }

        /// <summary>把书里的相对地址换算成本机能取到的地址。</summary>
        private static string RewriteUrl(string name, string value, string chapterDir, string idPrefix,
            Dictionary<string, int> chapterOfFile, string contentOrigin)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            string trimmed = value.Trim();

            if (trimmed.StartsWith("#", StringComparison.Ordinal))
                return "#" + idPrefix + trimmed.Substring(1);

            if (trimmed.StartsWith("//", StringComparison.Ordinal)) return value;
            if (trimmed.IndexOf(':') >= 0 && trimmed.IndexOf(':') < 8)
            {
                string scheme = trimmed.Substring(0, trimmed.IndexOf(':')).ToLowerInvariant();
                if (scheme == "http" || scheme == "https" || scheme == "data" ||
                    scheme == "mailto" || scheme == "tel" || scheme == "ftp") return value;
            }

            string path = trimmed;
            string fragment = string.Empty;
            int hash = path.IndexOf('#');
            if (hash >= 0)
            {
                fragment = path.Substring(hash);
                path = path.Substring(0, hash);
            }

            if (path.Length == 0) return "#" + idPrefix + fragment.TrimStart('#');

            string target = NormalizeZipPath(Combine(chapterDir, Unescape(path)));

            // 指向另一章：转成页内锚点，整本已经拼在一页里了
            int number;
            if (chapterOfFile.TryGetValue(target, out number))
            {
                string anchor = "#c" + number.ToString(CultureInfo.InvariantCulture);
                if (fragment.Length > 1) anchor += "-" + fragment.Substring(1);
                return anchor;
            }

            bool isLink = string.Equals(name, "href", StringComparison.OrdinalIgnoreCase);
            if (isLink && !LooksLikeResource(target)) return value;

            if (string.IsNullOrEmpty(contentOrigin)) return value;
            return contentOrigin.TrimEnd('/') + "/" + EscapePath(target);
        }

        private static bool LooksLikeResource(string path)
        {
            string ext = Path.GetExtension(path) ?? string.Empty;
            switch (ext.ToLowerInvariant())
            {
                case ".png": case ".jpg": case ".jpeg": case ".gif": case ".webp":
                case ".bmp": case ".svg": case ".css": case ".ttf": case ".otf":
                case ".woff": case ".woff2": case ".mp3": case ".mp4": case ".m4a":
                    return true;
                default:
                    return false;
            }
        }

        private static string EscapePath(string zipPath)
        {
            var sb = new StringBuilder(zipPath.Length + 8);
            string[] parts = zipPath.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append('/');
                sb.Append(Uri.EscapeDataString(parts[i]));
            }
            return sb.ToString();
        }

        private static string Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            return Encoding.UTF8.GetString(bytes);
        }

        // ───────────────────────── 小工具 ─────────────────────────

        private static XElement FindElement(XDocument doc, string localName)
        {
            if (doc == null) return null;
            foreach (var element in doc.Descendants())
            {
                if (string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase)) return element;
            }
            return null;
        }

        private static string Attribute(XElement element, string localName)
        {
            if (element == null) return null;
            foreach (var attribute in element.Attributes())
            {
                if (string.Equals(attribute.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
                    return attribute.Value;
            }
            return null;
        }

        private static int DepthOf(XElement element, string ancestorName)
        {
            int depth = 0;
            for (var parent = element.Parent; parent != null; parent = parent.Parent)
            {
                if (string.Equals(parent.Name.LocalName, ancestorName, StringComparison.OrdinalIgnoreCase)) depth++;
            }
            return depth;
        }

        private static string ResolveHref(string baseDir, string href)
        {
            string value = (href ?? string.Empty).Trim();
            if (value.Length == 0) return string.Empty;
            if (value.IndexOf(':') >= 0 && value.IndexOf(':') < 8) return value;

            string fragment = string.Empty;
            int hash = value.IndexOf('#');
            if (hash >= 0)
            {
                fragment = value.Substring(hash);
                value = value.Substring(0, hash);
            }
            if (value.Length == 0) return fragment;

            return NormalizeZipPath(Combine(baseDir, Unescape(value))) + fragment;
        }

        private static string Combine(string dir, string relative)
        {
            if (string.IsNullOrEmpty(dir)) return relative;
            return dir.TrimEnd('/') + "/" + relative.TrimStart('/');
        }

        private static string DirOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            int slash = path.LastIndexOf('/');
            return slash <= 0 ? string.Empty : path.Substring(0, slash);
        }

        private static string Unescape(string value)
        {
            try { return Uri.UnescapeDataString(value ?? string.Empty); }
            catch (Exception) { return value ?? string.Empty; }
        }

        /// <summary>把 a/./b/../c 收敛成 a/c，并去掉开头的斜杠。</summary>
        private static string NormalizeZipPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            string text = path.Replace('\\', '/');
            var parts = new List<string>();
            foreach (var segment in text.Split('/'))
            {
                if (segment.Length == 0 || segment == ".") continue;
                if (segment == "..")
                {
                    if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                    continue;
                }
                parts.Add(segment);
            }
            return string.Join("/", parts.ToArray());
        }

        private static string Collapse(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        private static string WebUtilityEncode(string text)
        {
            return System.Net.WebUtility.HtmlEncode(text ?? string.Empty);
        }
    }
}
