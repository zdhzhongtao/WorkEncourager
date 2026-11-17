using System;
using System.Collections.Generic;
using System.IO;

namespace SheetSplitter.Core
{
    /// <summary>
    /// 按列值拆出来的文件名要能存盘：Windows 不允许 `\ / : * ? " &lt; &gt; |`，
    /// 太长会踩到 260 字符路径上限，所以统一洗一遍。
    /// </summary>
    public static class FileNameSanitizer
    {
        public const int MaxLength = 60;

        private static readonly char[] Invalid = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

        /// <summary>
        /// Windows 的设备名。这些名字（带不带扩展名都一样）会被系统当成设备，
        /// 写进去"成功"但磁盘上什么都没有——按列值拆出个「CON」组就会静默丢文件。
        /// </summary>
        private static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        /// <summary>清洗规则：非法字符换成 `_`，去掉首尾空格与点，空值用「空白」，超过 60 字截断。</summary>
        public static string Clean(string value, string fallback = "空白")
        {
            string text = value ?? string.Empty;
            foreach (var ch in Invalid) text = text.Replace(ch, '_');

            // 换行、制表符也一起处理（Excel 单元格里真有人这么写）
            text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

            text = text.Trim(' ', '.', '_');
            while (text.IndexOf("  ", StringComparison.Ordinal) >= 0) text = text.Replace("  ", " ");

            if (text.Length == 0) text = fallback ?? "空白";
            if (text.Length > MaxLength) text = text.Substring(0, MaxLength).TrimEnd(' ', '.');
            if (text.Length == 0) text = fallback ?? "空白";

            // 「CON」「nul」这类得躲开，加个下划线既保留可读性又不再是设备名
            if (Reserved.Contains(text)) text += "_";
            return text;
        }

        /// <summary>拼出「原文件名_值.xlsx」，整体再兜一次长度。</summary>
        public static string BuildFileName(string baseName, string value, string extension = ".xlsx")
        {
            string cleanValue = Clean(value);
            string name = (baseName ?? "输出") + "_" + cleanValue;
            if (name.Length > MaxLength) name = name.Substring(0, MaxLength).TrimEnd(' ', '.');
            return name + extension;
        }

        /// <summary>重名不覆盖：自动加 (1) (2)。already 里已经用过的名字也算占用。</summary>
        public static string ResolveUnique(string directory, string fileName, ISet<string> already = null)
        {
            string name = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);

            string candidate = name + ext;
            int n = 1;
            while (true)
            {
                bool used = already != null && already.Contains(candidate);
                bool exists = false;
                if (!used && !string.IsNullOrEmpty(directory))
                {
                    try { exists = File.Exists(Path.Combine(directory, candidate)); } catch { exists = false; }
                }
                if (!used && !exists) break;
                candidate = name + " (" + n++ + ")" + ext;
            }

            already?.Add(candidate);
            return candidate;
        }
    }
}
