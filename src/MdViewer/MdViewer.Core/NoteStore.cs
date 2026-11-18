using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MdViewer.Core
{
    /// <summary>一条标注：正文里 [Start,End) 这段文字被高亮或被批注过。</summary>
    public sealed class Annotation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>所属文档的完整路径（区分大小写无意义，统一转小写比较）。</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>在渲染后正文纯文本里的起始偏移。</summary>
        public int Start { get; set; }

        public int End { get; set; }

        /// <summary>被标注的原文，用来在偏移漂移后重新定位。</summary>
        public string Quote { get; set; } = string.Empty;

        /// <summary>highlight = 只高亮；note = 带批注。</summary>
        public string Kind { get; set; } = "highlight";

        /// <summary>批注内容（Kind=highlight 时为空）。</summary>
        public string Note { get; set; } = string.Empty;

        public string Created { get; set; } = DateTimeOffset.Now.ToString("o");
    }

    /// <summary>一条书签：记住「读到某个标题 / 某个位置」。</summary>
    public sealed class Bookmark
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Path { get; set; } = string.Empty;

        /// <summary>标题的锚点 id（优先；文档改结构后失效就退回 ScrollRatio）。</summary>
        public string Anchor { get; set; } = string.Empty;

        /// <summary>打书签时的滚动比例 0~1。</summary>
        public double ScrollRatio { get; set; }

        /// <summary>给人看的名字，默认取当时的章节标题。</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>附带的备注（可空）。</summary>
        public string Note { get; set; } = string.Empty;

        public string Created { get; set; } = DateTimeOffset.Now.ToString("o");
    }

    /// <summary>notes.json 的整体结构。</summary>
    public sealed class NoteBook
    {
        public int Version { get; set; } = 1;
        public List<Annotation> Annotations { get; set; } = new List<Annotation>();
        public List<Bookmark> Bookmarks { get; set; } = new List<Bookmark>();
    }

    /// <summary>
    /// 标注与书签的本地存储（%APPDATA%\MdViewer\notes.json）。
    ///
    /// 为什么放一个文件：两者都是「按文档聚在一起的小数据」，分两个文件只是多两份读写和迁移代码。
    /// 写入走原子替换（AtomicFile），程序崩了不会把笔记变成半截 JSON。
    /// </summary>
    public static class NoteStore
    {
        /// <summary>最多留多少条（防止几十年后这个文件变成几 MB）。超出时砍最老的。</summary>
        public const int MaxEntries = 2000;

        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        public static string PathIn(string dataDir)
        {
            return System.IO.Path.Combine(dataDir, "notes.json");
        }

        public static NoteBook Load(string dataDir)
        {
            try
            {
                var book = AtomicFile.ReadJson<NoteBook>(PathIn(dataDir), JsonOptions);
                if (book == null) return new NoteBook();
                if (book.Annotations == null) book.Annotations = new List<Annotation>();
                if (book.Bookmarks == null) book.Bookmarks = new List<Bookmark>();
                return book;
            }
            catch (Exception)
            {
                return new NoteBook();
            }
        }

        public static void Save(string dataDir, NoteBook book)
        {
            if (book == null) return;
            Trim(book);
            AtomicFile.WriteJson(PathIn(dataDir), book, JsonOptions);
        }

        private static void Trim(NoteBook book)
        {
            int total = book.Annotations.Count + book.Bookmarks.Count;
            if (total <= MaxEntries) return;

            // 先砍最老的标注、再砍最老的书签（书签更贵，留久一点）
            book.Annotations.Sort((a, b) => string.CompareOrdinal(a.Created, b.Created));
            int overflow = total - MaxEntries;
            if (overflow > 0 && book.Annotations.Count > 0)
            {
                int take = Math.Min(overflow, book.Annotations.Count);
                book.Annotations.RemoveRange(0, take);
                overflow -= take;
            }
            if (overflow > 0 && book.Bookmarks.Count > 0)
            {
                book.Bookmarks.Sort((a, b) => string.CompareOrdinal(a.Created, b.Created));
                int take = Math.Min(overflow, book.Bookmarks.Count);
                book.Bookmarks.RemoveRange(0, take);
            }
        }

        /// <summary>同一篇文档的标注（按位置排序）。路径比较忽略大小写。</summary>
        public static List<Annotation> AnnotationsFor(NoteBook book, string path)
        {
            var list = new List<Annotation>();
            if (book == null || string.IsNullOrEmpty(path)) return list;
            foreach (var item in book.Annotations)
            {
                if (SamePath(item.Path, path)) list.Add(item);
            }
            list.Sort((a, b) => a.Start.CompareTo(b.Start));
            return list;
        }

        public static List<Bookmark> BookmarksFor(NoteBook book, string path)
        {
            var list = new List<Bookmark>();
            if (book == null || string.IsNullOrEmpty(path)) return list;
            foreach (var item in book.Bookmarks)
            {
                if (SamePath(item.Path, path)) list.Add(item);
            }
            return list;
        }

        /// <summary>这本书里所有涉及过的文档（书签面板要跨文档列，先按路径聚合）。</summary>
        public static List<string> PathsIn(NoteBook book)
        {
            var seen = new List<string>();
            if (book == null) return seen;
            foreach (var item in book.Annotations)
            {
                if (!string.IsNullOrEmpty(item.Path) && !seen.Contains(item.Path)) seen.Add(item.Path);
            }
            foreach (var item in book.Bookmarks)
            {
                if (!string.IsNullOrEmpty(item.Path) && !seen.Contains(item.Path)) seen.Add(item.Path);
            }
            return seen;
        }

        public static bool RemoveAnnotation(NoteBook book, string id)
        {
            if (book == null || string.IsNullOrEmpty(id)) return false;
            return book.Annotations.RemoveAll(item => string.Equals(item.Id, id, StringComparison.Ordinal)) > 0;
        }

        public static bool RemoveBookmark(NoteBook book, string id)
        {
            if (book == null || string.IsNullOrEmpty(id)) return false;
            return book.Bookmarks.RemoveAll(item => string.Equals(item.Id, id, StringComparison.Ordinal)) > 0;
        }

        /// <summary>给页面用的精简结构（只有 id / 位置 / 原文 / 备注）。</summary>
        public static string ToPageJson(IEnumerable<Annotation> items)
        {
            var list = new List<object>();
            if (items != null)
            {
                foreach (var item in items)
                {
                    list.Add(new
                    {
                        id = item.Id,
                        start = item.Start,
                        end = item.End,
                        quote = item.Quote,
                        kind = item.Kind,
                        note = item.Note
                    });
                }
            }
            return JsonSerializer.Serialize(list, JsonOptions);
        }

        public static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(NormalizePath(a), NormalizePath(b), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 统一写法再比较。Windows 上 "C:/docs/a.md" 和 "C:\docs\a.md" 是同一个文件，
        /// 但字符串不相等；不统一的话，同一篇文档会被开出两个标签、两份标注。
        /// </summary>
        private static string NormalizePath(string path)
        {
            string text = (path ?? string.Empty).Trim();
            if (text.Length == 0) return text;

            text = text.Replace('/', '\\');
            // 尾部多余的反斜杠去掉（"C:\docs\" 与 "C:\docs" 指同一处），"C:\" 这种根保留
            while (text.Length > 3 && text.EndsWith("\\", StringComparison.Ordinal))
                text = text.Substring(0, text.Length - 1);
            return text;
        }
    }
}
