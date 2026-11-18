using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MdViewer.Core;

namespace MdViewer.Tests
{
    public class NoteStoreTests
    {
        private static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "mdviewer-notes-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static Annotation Mark(string path, int start, int end, string quote, string kind = "highlight")
        {
            return new Annotation
            {
                Path = path,
                Start = start,
                End = end,
                Quote = quote,
                Kind = kind,
                Note = kind == "note" ? "批注内容" : string.Empty
            };
        }

        [Fact]
        public void 存下去再读回来内容一致()
        {
            string dir = TempDir();
            try
            {
                var book = new NoteBook();
                book.Annotations.Add(Mark(@"C:\a.md", 10, 20, "一段原文", "note"));
                book.Bookmarks.Add(new Bookmark
                {
                    Path = @"C:\a.md",
                    Anchor = "sec-1",
                    ScrollRatio = 0.42,
                    Label = "第一节"
                });

                NoteStore.Save(dir, book);
                Assert.True(File.Exists(NoteStore.PathIn(dir)));

                NoteBook loaded = NoteStore.Load(dir);
                Assert.Single(loaded.Annotations);
                Assert.Single(loaded.Bookmarks);
                Assert.Equal("一段原文", loaded.Annotations[0].Quote);
                Assert.Equal("批注内容", loaded.Annotations[0].Note);
                Assert.Equal("sec-1", loaded.Bookmarks[0].Anchor);
                Assert.Equal(0.42, loaded.Bookmarks[0].ScrollRatio, 3);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void 文件坏掉时返回空笔记本而不是抛()
        {
            string dir = TempDir();
            try
            {
                File.WriteAllText(NoteStore.PathIn(dir), "{ 这不是 json");

                NoteBook book = NoteStore.Load(dir);
                Assert.NotNull(book);
                Assert.Empty(book.Annotations);
                Assert.Empty(book.Bookmarks);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void 没有文件时也是空笔记本()
        {
            string dir = TempDir();
            try
            {
                NoteBook book = NoteStore.Load(dir);
                Assert.Empty(book.Annotations);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void 路径比较忽略大小写()
        {
            Assert.True(NoteStore.SamePath(@"C:\Docs\A.md", @"c:\docs\a.md"));
            Assert.False(NoteStore.SamePath(@"C:\Docs\A.md", @"C:\Docs\B.md"));

            // 空路径不代表任何文档。以前两个空串会被当成"相等"，
            // 导致拿一个空路径去查标注时可能撞上另一条同样为空的记录。
            Assert.False(NoteStore.SamePath(null, string.Empty));
            Assert.False(NoteStore.SamePath(string.Empty, string.Empty));
        }

        [Fact]
        public void 按文档取标注时按位置排序()
        {
            var book = new NoteBook();
            book.Annotations.Add(Mark(@"c:\d\a.md", 100, 110, "后"));
            book.Annotations.Add(Mark(@"C:\D\A.md", 5, 9, "前"));
            book.Annotations.Add(Mark(@"c:\d\b.md", 1, 2, "别的文档"));

            List<Annotation> mine = NoteStore.AnnotationsFor(book, @"C:\d\a.md");

            Assert.Equal(2, mine.Count);
            Assert.Equal("前", mine[0].Quote);
            Assert.Equal("后", mine[1].Quote);
        }

        [Fact]
        public void 标注太多时先砍最老的标注再砍书签()
        {
            string dir = TempDir();
            try
            {
                var book = new NoteBook();
                for (int i = 0; i < NoteStore.MaxEntries + 10; i++)
                {
                    var item = Mark(@"c:\a.md", i, i + 1, "q" + i);
                    item.Created = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)
                        .AddMinutes(i).ToString("o");
                    book.Annotations.Add(item);
                }

                book.Bookmarks.Add(new Bookmark { Path = @"c:\a.md", Label = "留着" });

                NoteStore.Save(dir, book);

                Assert.Equal(NoteStore.MaxEntries, book.Annotations.Count + book.Bookmarks.Count);
                Assert.Single(book.Bookmarks);
                // 2010 条标注 + 1 条书签超了 11 条，砍掉最老的 q0~q10
                Assert.Equal("q11", book.Annotations[0].Quote);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void 按id删标注与书签()
        {
            var book = new NoteBook();
            var note = Mark(@"c:\a.md", 1, 2, "x");
            var mark = new Bookmark { Path = @"c:\a.md" };
            book.Annotations.Add(note);
            book.Bookmarks.Add(mark);

            Assert.True(NoteStore.RemoveAnnotation(book, note.Id));
            Assert.False(NoteStore.RemoveAnnotation(book, note.Id));
            Assert.True(NoteStore.RemoveBookmark(book, mark.Id));
            Assert.Empty(book.Annotations);
            Assert.Empty(book.Bookmarks);
        }

        [Fact]
        public void 给页面的json只带渲染需要的字段()
        {
            var list = new List<Annotation>
            {
                Mark(@"c:\a.md", 3, 8, "选中", "note")
            };

            string json = NoteStore.ToPageJson(list);
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;
                Assert.Equal(JsonValueKind.Array, root.ValueKind);
                Assert.Equal(1, root.GetArrayLength());

                JsonElement item = root[0];
                Assert.Equal(3, item.GetProperty("start").GetInt32());
                Assert.Equal(8, item.GetProperty("end").GetInt32());
                Assert.Equal("选中", item.GetProperty("quote").GetString());
                Assert.Equal("note", item.GetProperty("kind").GetString());

                // 路径、创建时间这些宿主自己知道，不必塞给页面
                Assert.False(item.TryGetProperty("Path", out _));
                Assert.False(item.TryGetProperty("created", out _));
            }
        }

        [Fact]
        public void 聚合出所有涉及过的文档()
        {
            var book = new NoteBook();
            book.Annotations.Add(Mark(@"c:\a.md", 1, 2, "x"));
            book.Bookmarks.Add(new Bookmark { Path = @"c:\b.md" });
            book.Bookmarks.Add(new Bookmark { Path = @"c:\a.md" });

            List<string> paths = NoteStore.PathsIn(book);

            Assert.Equal(2, paths.Count);
            Assert.Contains(@"c:\a.md", paths);
            Assert.Contains(@"c:\b.md", paths);
        }

        [Fact]
        public void 正斜杠和反斜杠算同一个路径()
        {
            // 同一次启动里，命令行/拖放给的可能是 "C:/docs/a.md"，双击给的是 "C:\docs\a.md"。
            // 不认成同一个就会给同一篇文档开两个标签。
            Assert.True(NoteStore.SamePath(@"C:/docs/a.md", @"C:\docs\a.md"));
            Assert.True(NoteStore.SamePath(@"C:\docs\a.md", @"C:/docs/a.md"));
            Assert.True(NoteStore.SamePath(@"C:\docs\a.md", @"c:\DOCS\A.MD"));
            Assert.True(NoteStore.SamePath(@"C:\docs\", @"C:\docs"));
        }

        [Fact]
        public void 不同文件不会因为归一化被判成同一个()
        {
            Assert.False(NoteStore.SamePath(@"C:\docs\a.md", @"C:\docs\b.md"));
            Assert.False(NoteStore.SamePath(@"C:\docs\a.md", @"D:\docs\a.md"));
            Assert.False(NoteStore.SamePath(@"C:\docs", @"C:\docs2"));
            Assert.False(NoteStore.SamePath(null, @"C:\docs\a.md"));
        }
    }
}
