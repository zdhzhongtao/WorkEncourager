using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace MdViewer.Core
{
    /// <summary>
    /// 第三方渲染库（KaTeX / Mermaid / highlight.js）的落地与版本管理。
    ///
    /// 为什么不直接内联进 HTML：这三个加起来 3.2MB，内联会让**每一篇**文档的页面都背上 3MB，
    /// 而绝大多数文档里既没有公式也没有图表。所以走「内嵌资源 → 首次运行释放到缓存目录 →
    /// 页面按 <c>&lt;script src&gt;</c> 按需加载」，整个过程仍然零联网。
    ///
    /// 缓存目录已经映射到 <c>https://mdviewer.tmp</c>，所以释放出来的文件同源可用，
    /// 不用再开第二个虚拟域名，CSP 也只需要放行这一个源。
    /// </summary>
    public static class VendorAssets
    {
        /// <summary>
        /// 资源的版本标识。内容有更新就改这个值，下次启动会自动重写一遍缓存目录。
        /// （改版本号是唯一的失效手段 —— 比逐个文件比对哈希便宜得多。）
        /// </summary>
        public const string Version = "katex-0.16.11_mermaid-11.4.1_hljs-11.10.0";

        private const string ResourcePrefix = "vendor:";

        /// <summary>
        /// 把内嵌的第三方资源释放到 <c>&lt;cacheDir&gt;\vendor</c>，返回该目录；失败返回 null。
        /// 已经释放过（版本一致）就直接返回，代价是一次 20 字节的文件读取。
        /// </summary>
        public static string Ensure(string cacheDir)
        {
            if (string.IsNullOrWhiteSpace(cacheDir)) return null;

            string target = Path.Combine(cacheDir, "vendor");
            string marker = Path.Combine(target, "version.txt");

            try
            {
                if (File.Exists(marker) && File.ReadAllText(marker).Trim() == Version)
                    return target;

                Directory.CreateDirectory(target);

                var assembly = typeof(VendorAssets).GetTypeInfo().Assembly;
                int written = 0;

                foreach (var resource in assembly.GetManifestResourceNames())
                {
                    if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal)) continue;

                    string relative = resource.Substring(ResourcePrefix.Length)
                        .Replace('\\', Path.DirectorySeparatorChar)
                        .Replace('/', Path.DirectorySeparatorChar);
                    if (relative.Length == 0) continue;

                    string dest = Path.Combine(target, relative);
                    string dir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    using (var stream = assembly.GetManifestResourceStream(resource))
                    {
                        if (stream == null) continue;
                        using (var file = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                            stream.CopyTo(file);
                    }
                    written++;
                }

                if (written == 0) return null;

                File.WriteAllText(marker, Version, new UTF8Encoding(false));
                return target;
            }
            catch (Exception)
            {
                // 释放失败不是致命错误：页面会退化成「公式显示原始 TeX、图表显示代码块」，
                // 而不是打不开文档。不弹窗，日志里 App 那层会记。
                return null;
            }
        }

        /// <summary>校验释放是否完整（少一个文件就让调用方别声称支持数学/图表）。</summary>
        public static bool IsUsable(string vendorDir)
        {
            if (string.IsNullOrWhiteSpace(vendorDir) || !Directory.Exists(vendorDir)) return false;
            try
            {
                return File.Exists(Path.Combine(vendorDir, "katex", "katex.min.js"))
                    && File.Exists(Path.Combine(vendorDir, "katex", "katex.min.css"))
                    && File.Exists(Path.Combine(vendorDir, "mermaid", "mermaid.min.js"))
                    && File.Exists(Path.Combine(vendorDir, "hljs", "highlight.min.js"));
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>内嵌资源清单（自检与测试用）。</summary>
        public static List<string> ListResources()
        {
            var names = new List<string>();
            var assembly = typeof(VendorAssets).GetTypeInfo().Assembly;
            foreach (var resource in assembly.GetManifestResourceNames())
            {
                if (resource.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                    names.Add(resource.Substring(ResourcePrefix.Length).Replace('\\', '/'));
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }
    }
}
