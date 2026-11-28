using System;
using System.Collections.Generic;
using System.IO;

namespace MdViewer.Core
{
    /// <summary>
    /// 渲染页面的缓存目录管理。每打开一次文档就会写一个 view-N.html，
    /// 用得久了会把 %APPDATA% 撑大，所以启动时只留最近若干个。
    /// </summary>
    public static class CacheStore
    {
        public const string Pattern = "view-*.html";

        /// <summary>
        /// 只保留最新的 keep 个 view-*.html，其余删掉；顺带清理超过 maxAgeDays 天的。
        /// 返回删掉的文件数。删不掉的不报错（可能正被占用）。
        /// </summary>
        public static int Trim(string directory, int keep = 20, int maxAgeDays = 7, DateTime? now = null)
        {
            if (string.IsNullOrWhiteSpace(directory)) return 0;

            string[] files;
            try
            {
                if (!Directory.Exists(directory)) return 0;
                files = Directory.GetFiles(directory, Pattern, SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                return 0;
            }
            if (files.Length == 0) return 0;

            var items = new List<FileInfo>(files.Length);
            foreach (var file in files)
            {
                try { items.Add(new FileInfo(file)); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            }

            // 新的在前
            items.Sort((x, y) => y.LastWriteTimeUtc.CompareTo(x.LastWriteTimeUtc));

            var today = now ?? DateTime.UtcNow;
            int removed = 0;
            for (int i = 0; i < items.Count; i++)
            {
                bool tooMany = i >= keep;
                bool tooOld = (today - items[i].LastWriteTimeUtc).TotalDays > maxAgeDays;

                // 最新的那几个无论多旧都留着，否则刚打开的文件下次启动就找不到缓存了
                if (i >= 3 && tooOld) tooMany = true;
                if (!tooMany) continue;

                try
                {
                    items[i].Delete();
                    removed++;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // 被占用就下次再说
                }
            }

            TrimBackups(directory, today, maxAgeDays);
            return removed;
        }

        /// <summary>
        /// 渲染页每次写入都会留一份 `view-N.html.bak`（原子写的备份），但清理只匹配
        /// `view-*.html`，于是 .bak 会永久堆积。这里删掉「对应 html 已经不在了」或太旧的 .bak。
        /// </summary>
        private static void TrimBackups(string directory, DateTime today, int maxAgeDays)
        {
            try
            {
                foreach (string bak in Directory.GetFiles(directory, "view-*.html.bak", SearchOption.TopDirectoryOnly))
                {
                    string html = bak.Substring(0, bak.Length - 4);   // 去掉 ".bak"
                    bool orphan = !File.Exists(html);
                    bool tooOld = (today - File.GetLastWriteTimeUtc(bak)).TotalDays > maxAgeDays;
                    if (!orphan && !tooOld) continue;

                    try { File.Delete(bak); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
            }
        }
    }
}
