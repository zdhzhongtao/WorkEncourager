using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json.Serialization;

namespace MdViewer.Core
{
    /// <summary>
    /// 「最近打开」里的一条记录。带打开时间，界面才能显示「3 分钟前」这类提示。
    /// </summary>
    public sealed class RecentEntry
    {
        public string Path { get; set; }

        /// <summary>ISO 8601（round-trip）格式，跨时区也不会错。</summary>
        public string OpenedAt { get; set; }

        /// <summary>
        /// 解析好的时间。这是给人用的只读视图，别写进 settings.json ——
        /// 不加 JsonIgnore 的话每条记录会多存一个永不同步的冗余字段。
        /// </summary>
        [JsonIgnore]
        public DateTimeOffset OpenedTime
        {
            get
            {
                if (string.IsNullOrWhiteSpace(OpenedAt)) return DateTimeOffset.MinValue;
                DateTimeOffset parsed;
                return DateTimeOffset.TryParse(OpenedAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out parsed) ? parsed : DateTimeOffset.MinValue;
            }
        }
    }

    /// <summary>
    /// 最近文件列表的纯逻辑：去重（忽略大小写）、最新排第一、上限 12。
    ///
    /// 与旧版的区别：**不再剔除已经不存在的文件**。文件被删/被移走以后，
    /// 用户更希望在列表里看到一个灰掉的条目知道自己读过什么，而不是它无声消失。
    /// 是否可用由 <see cref="Exists"/> 单独判断，界面上置灰即可。
    /// </summary>
    public static class RecentFiles
    {
        public const int MaxCount = 12;

        public static List<RecentEntry> Add(IEnumerable<RecentEntry> current, string path, DateTimeOffset now)
        {
            var list = new List<RecentEntry>();
            if (string.IsNullOrWhiteSpace(path)) return Normalize(current);

            string full;
            try
            {
                full = Path.GetFullPath(path.Trim().Trim('"'));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return Normalize(current);
            }

            list.Add(new RecentEntry
            {
                Path = full,
                OpenedAt = now.ToString("o", CultureInfo.InvariantCulture)
            });

            if (current != null)
            {
                foreach (var item in current)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.Path)) continue;
                    if (string.Equals(item.Path, full, StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(item);
                    if (list.Count >= MaxCount) break;
                }
            }
            return list;
        }

        /// <summary>从列表里去掉一条（界面上右键「从列表移除」）。</summary>
        public static List<RecentEntry> Remove(IEnumerable<RecentEntry> current, string path)
        {
            var list = new List<RecentEntry>();
            if (current == null) return list;
            foreach (var item in current)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Path)) continue;
                if (string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(item);
            }
            return Normalize(list);
        }

        /// <summary>去空、去重、截断到上限，保持原顺序。不碰磁盘。</summary>
        public static List<RecentEntry> Normalize(IEnumerable<RecentEntry> current)
        {
            var list = new List<RecentEntry>();
            if (current == null) return list;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in current)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Path)) continue;
                string text = item.Path.Trim();
                if (!seen.Add(text)) continue;
                list.Add(new RecentEntry { Path = text, OpenedAt = item.OpenedAt });
                if (list.Count >= MaxCount) break;
            }
            return list;
        }

        /// <summary>旧版 settings.json 里只有一串路径，迁移成带时间的条目。</summary>
        public static List<RecentEntry> FromLegacy(IEnumerable<string> paths)
        {
            var list = new List<RecentEntry>();
            if (paths == null) return list;
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                list.Add(new RecentEntry { Path = path.Trim(), OpenedAt = null });
                if (list.Count >= MaxCount) break;
            }
            return Normalize(list);
        }

        /// <summary>取纯路径列表（写回旧字段、做存在性批量判断时用）。</summary>
        public static List<string> ToPaths(IEnumerable<RecentEntry> entries)
        {
            var list = new List<string>();
            if (entries == null) return list;
            foreach (var item in entries)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Path)) continue;
                list.Add(item.Path);
            }
            return list;
        }

        /// <summary>界面上灰显用：这个条目对应的文件还在不在（不在的不删除，只置灰）。</summary>
        public static bool Exists(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try { return File.Exists(path); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is IOException)
            {
                return false;
            }
        }

        /// <summary>「3 分钟前」「昨天 14:20」这种人话时间。</summary>
        public static string Describe(RecentEntry entry, DateTimeOffset now)
        {
            if (entry == null) return string.Empty;
            var time = entry.OpenedTime;
            if (time == DateTimeOffset.MinValue) return string.Empty;

            TimeSpan span = now - time;
            if (span.TotalSeconds < 60) return "刚刚";
            if (span.TotalMinutes < 60) return (int)span.TotalMinutes + " 分钟前";
            if (span.TotalHours < 24 && time.LocalDateTime.Date == now.LocalDateTime.Date)
                return (int)span.TotalHours + " 小时前";
            if (time.LocalDateTime.Date == now.LocalDateTime.Date.AddDays(-1)) return "昨天";
            if (span.TotalDays < 7) return (int)span.TotalDays + " 天前";
            if (time.Year == now.Year) return time.LocalDateTime.ToString("M月d日", CultureInfo.InvariantCulture);
            return time.LocalDateTime.ToString("yyyy年M月d日", CultureInfo.InvariantCulture);
        }
    }
}
