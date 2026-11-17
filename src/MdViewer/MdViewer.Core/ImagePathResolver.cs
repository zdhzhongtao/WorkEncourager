using System;
using System.Collections.Generic;
using System.IO;

namespace MdViewer.Core
{
    /// <summary>
    /// md 里图片路径的处理：判断是不是远程地址、把相对路径算成绝对路径。
    /// 不联网、不下载，只做字符串与路径运算。
    /// </summary>
    public static class ImagePathResolver
    {
        private static readonly string[] RemoteSchemes =
        {
            "http://", "https://", "data:", "mailto:", "file:", "ftp://", "//"
        };

        /// <summary>是不是不需要落到本地磁盘的地址（http / https / data / 协议相对 …）。</summary>
        public static bool IsRemote(string src)
        {
            if (string.IsNullOrWhiteSpace(src)) return true;
            string text = src.Trim();
            foreach (var scheme in RemoteSchemes)
            {
                if (text.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>是不是"绝对路径"（C:\a\b.png 或 /a/b.png）。</summary>
        public static bool IsAbsoluteLocal(string src)
        {
            if (string.IsNullOrWhiteSpace(src)) return false;
            string text = src.Trim();
            if (text.Length >= 2 && text[1] == ':') return true;
            return text.StartsWith("\\\\", StringComparison.Ordinal) || text.StartsWith("/", StringComparison.Ordinal);
        }

        /// <summary>
        /// 把路径规整成 URL 友好的形式：反斜杠转斜杠、去掉 ./、折叠 a/../。
        /// 远程地址原样返回。
        /// </summary>
        public static string Normalize(string src)
        {
            if (string.IsNullOrWhiteSpace(src)) return string.Empty;
            string text = src.Trim();
            if (IsRemote(text)) return text;

            text = text.Replace('\\', '/');
            bool rooted = text.StartsWith("/", StringComparison.Ordinal);
            string prefix = string.Empty;
            if (text.Length >= 2 && text[1] == ':')
            {
                prefix = text.Substring(0, 2);           // C:
                text = text.Substring(2);
            }

            var parts = new List<string>();
            foreach (var piece in text.Split('/'))
            {
                if (piece.Length == 0 || piece == ".") continue;
                if (piece == ".." && parts.Count > 0 && parts[parts.Count - 1] != "..")
                {
                    parts.RemoveAt(parts.Count - 1);
                    continue;
                }
                parts.Add(piece);
            }

            string joined = string.Join("/", parts);
            if (prefix.Length > 0) return prefix + "/" + joined;
            return rooted ? "/" + joined : joined;
        }

        /// <summary>
        /// 相对路径 → 绝对本地路径。远程地址、空路径返回 null。
        /// </summary>
        public static string Resolve(string src, string baseDir)
        {
            if (string.IsNullOrWhiteSpace(src)) return null;
            if (IsRemote(src)) return null;

            string text = src.Trim();
            if (text.IndexOf('%') >= 0)
            {
                try { text = Uri.UnescapeDataString(text); }
                catch (UriFormatException) { /* 转义不合法就按原样处理 */ }
            }

            try
            {
                if (IsAbsoluteLocal(text))
                {
                    if (text.StartsWith("/", StringComparison.Ordinal) && !text.StartsWith("//", StringComparison.Ordinal))
                    {
                        string root = Path.GetPathRoot(baseDir) ?? "C:\\";
                        text = root + text.TrimStart('/').Replace('/', '\\');
                    }
                    return Path.GetFullPath(text);
                }

                if (string.IsNullOrWhiteSpace(baseDir)) return Path.GetFullPath(text);
                return Path.GetFullPath(Path.Combine(baseDir, text.Replace('/', '\\')));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
            }
        }

        /// <summary>fullPath 是否落在 rootDir 之内（同盘、按目录前缀比较）。</summary>
        public static bool IsUnderRoot(string fullPath, string rootDir)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(rootDir)) return false;
            try
            {
                string root = Path.GetFullPath(rootDir).TrimEnd('\\', '/');
                string file = Path.GetFullPath(fullPath);
                if (string.Equals(file, root, StringComparison.OrdinalIgnoreCase)) return true;
                return file.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)
                    || file.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return false;
            }
        }

        /// <summary>
        /// 相对路径 → 相对虚拟主机的路径（如 img/a.png）。落在根外或远程地址返回 null。
        /// </summary>
        public static string ToVirtualRelative(string src, string baseDir)
        {
            string full = Resolve(src, baseDir);
            if (full == null) return null;
            if (!IsUnderRoot(full, baseDir)) return null;

            string root = Path.GetFullPath(baseDir).TrimEnd('\\', '/');
            string rest = full.Substring(root.Length).TrimStart('\\', '/');
            return rest.Replace('\\', '/');
        }
    }
}
