using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MdViewer.Core;

namespace MdViewer
{
    /// <summary>侧栏「笔记」页签里的一行。</summary>
    public sealed class NoteRow
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }

        /// <summary>bookmark / highlight / note。</summary>
        public string Kind { get; set; }

        public string Anchor { get; set; }
        public double Ratio { get; set; }
        public int Start { get; set; }
    }

    /// <summary>
    /// 标注与书签：数据在 notes.json，位置在渲染后的正文偏移。
    ///
    /// 取舍：偏移会随文档修改而漂移，所以每条都存了原文（Quote），
    /// 对不上时用原文在全文里搜一次 —— 大部分「改了两个字」的情况都能救回来，
    /// 真找不到就跳过那一条，不破坏排版。
    /// </summary>
    public partial class MainWindow
    {
        private NoteBook _notes;

        private NoteBook Notes
        {
            get
            {
                if (_notes == null)
                {
                    _notes = NoteStore.Load(App.DataDir);
                }
                return _notes;
            }
        }

        private void SaveNotes()
        {
            try
            {
                NoteStore.Save(App.DataDir, Notes);
            }
            catch (Exception ex)
            {
                Log.Error("保存标注失败", ex);
                StatusRight.Text = "保存标注失败：" + ex.Message;
            }
        }

        /// <summary>页面里选中文字后点了「高亮 / 批注」。</summary>
        private void HandleAnnotateRequest(string payloadJson)
        {
            if (string.IsNullOrEmpty(_currentPath) || string.IsNullOrWhiteSpace(payloadJson)) return;

            try
            {
                using (JsonDocument doc = JsonDocument.Parse(payloadJson))
                {
                    JsonElement root = doc.RootElement;
                    string act = Text(root, "act");
                    string quote = Text(root, "text");
                    int start = Int(root, "start");
                    int end = Int(root, "end");
                    if (end <= start || string.IsNullOrEmpty(quote)) return;

                    string note = string.Empty;
                    string kind = "highlight";

                    if (act == "note")
                    {
                        var dialog = new NoteDialog(quote) { Owner = this };
                        if (dialog.ShowDialog() != true) return;
                        note = dialog.NoteText;
                        if (string.IsNullOrWhiteSpace(note)) return;
                        kind = "note";
                    }

                    var item = new Annotation
                    {
                        Path = _currentPath,
                        Start = start,
                        End = end,
                        Quote = quote,
                        Kind = kind,
                        Note = note ?? string.Empty
                    };

                    Notes.Annotations.Add(item);
                    SaveNotes();
                    PostAnnotationsToPage();
                    FillNotesPanel();

                    StatusRight.Text = kind == "note"
                        ? "已添加批注（" + Shorten(quote, 18) + "）"
                        : "已高亮：" + Shorten(quote, 24);
                }
            }
            catch (Exception ex)
            {
                Log.Error("添加标注失败", ex);
            }
        }

        private static string Text(JsonElement root, string name)
        {
            JsonElement value;
            return root.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : string.Empty;
        }

        private static int Int(JsonElement root, string name)
        {
            JsonElement value;
            if (!root.TryGetProperty(name, out value)) return 0;
            if (value.ValueKind == JsonValueKind.Number) return value.GetInt32();
            int parsed;
            return int.TryParse(value.ToString(), out parsed) ? parsed : 0;
        }

        private static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return flat.Length <= max ? flat : flat.Substring(0, max) + "…";
        }

        /// <summary>把当前文档的标注推给页面重画。</summary>
        private void PostAnnotationsToPage()
        {
            if (!_webReady || string.IsNullOrEmpty(_currentPath)) return;
            try
            {
                var items = NoteStore.AnnotationsFor(Notes, _currentPath);
                string json = NoteStore.ToPageJson(items);

                // payload 是数组，不能走 PostToPage（那个只发字符串）
                string message = "{\"kind\":\"annotations\",\"payload\":" + json + "}";
                View.CoreWebView2.PostWebMessageAsJson(message);
            }
            catch (Exception ex)
            {
                Log.Error("推送标注失败", ex);
            }
        }

        /// <summary>把当前位置加成书签（记锚点 + 比例，标题当名字）。</summary>
        private async void AddBookmark()
        {
            if (string.IsNullOrEmpty(_currentPath) || !_webReady)
            {
                StatusRight.Text = "先打开一篇文档再打书签";
                return;
            }

            try
            {
                string raw = await View.CoreWebView2.ExecuteScriptAsync(
                    "window.__mdviewer ? window.__mdviewer.getScroll() : ''");
                if (string.IsNullOrEmpty(raw) || raw == "\"\"") return;

                string inner = JsonSerializer.Deserialize<string>(raw);
                if (string.IsNullOrWhiteSpace(inner)) return;

                string anchor = string.Empty;
                double ratio = 0;
                using (JsonDocument doc = JsonDocument.Parse(inner))
                {
                    JsonElement el;
                    if (doc.RootElement.TryGetProperty("ratio", out el)) ratio = el.GetDouble();
                    if (doc.RootElement.TryGetProperty("anchor", out el) && el.ValueKind == JsonValueKind.String)
                        anchor = el.GetString();
                }

                string label = string.IsNullOrEmpty(anchor) ? string.Empty : FindHeadingText(anchor);
                if (string.IsNullOrWhiteSpace(label))
                    label = string.Format("第 {0:0}% 处", Math.Round(ratio * 100));

                var item = new Bookmark
                {
                    Path = _currentPath,
                    Anchor = anchor ?? string.Empty,
                    ScrollRatio = Math.Round(ratio, 4),
                    Label = label
                };

                Notes.Bookmarks.Add(item);
                SaveNotes();
                FillNotesPanel();
                StatusRight.Text = "已加书签：" + label;
            }
            catch (Exception ex)
            {
                Log.Error("添加书签失败", ex);
            }
        }

        /// <summary>从目录里找锚点对应的标题文字。</summary>
        private string FindHeadingText(string anchor)
        {
            if (string.IsNullOrEmpty(anchor) || _doc == null || _doc.Toc == null) return string.Empty;
            foreach (var item in _doc.Toc)
            {
                if (string.Equals(item.Id, anchor, StringComparison.Ordinal)) return item.Text;
            }
            return string.Empty;
        }

        /// <summary>重建侧栏「笔记」页签的内容。</summary>
        private void FillNotesPanel()
        {
            if (BookmarkList == null) return;

            var bookmarks = new List<NoteRow>();
            var annotations = new List<NoteRow>();

            if (!string.IsNullOrEmpty(_currentPath))
            {
                foreach (var item in NoteStore.BookmarksFor(Notes, _currentPath))
                {
                    bookmarks.Add(new NoteRow
                    {
                        Id = item.Id,
                        Kind = "bookmark",
                        Title = item.Label,
                        Detail = item.Anchor,
                        Anchor = item.Anchor,
                        Ratio = item.ScrollRatio
                    });
                }

                foreach (var item in NoteStore.AnnotationsFor(Notes, _currentPath))
                {
                    annotations.Add(new NoteRow
                    {
                        Id = item.Id,
                        Kind = item.Kind == "note" ? "note" : "highlight",
                        Title = Shorten(item.Quote, 40),
                        Detail = item.Kind == "note" ? Shorten(item.Note, 60) : "高亮",
                        Start = item.Start
                    });
                }
            }

            BookmarkList.ItemsSource = bookmarks;
            AnnotationList.ItemsSource = annotations;
            BookmarkEmpty.Visibility = bookmarks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            AnnotationEmpty.Visibility = annotations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoteCount.Text = (bookmarks.Count + annotations.Count) == 0
                ? string.Empty
                : (bookmarks.Count + annotations.Count).ToString();
        }

        private void BookmarkList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var row = BookmarkList.SelectedItem as NoteRow;
            if (row == null || !_webReady) return;

            if (!string.IsNullOrEmpty(row.Anchor) && FindHeadingText(row.Anchor).Length > 0)
            {
                PostToPage("scrollTo", row.Anchor);
            }
            else
            {
                // 锚点找不到（文档结构改过）：退回按比例滚
                try
                {
                    // payload 必须是对象：页面 restore 分支直接读 data.y / data.ratio，
                    // 二次序列化成字符串会让 Number(undefined) 变成 0（滚到文档顶部）。
                    string json = JsonSerializer.Serialize(new RestoreMessage
                    {
                        kind = "restore",
                        payload = new ScrollPayload { y = 0, ratio = row.Ratio }
                    });
                    View.CoreWebView2.PostWebMessageAsJson(json);
                }
                catch (Exception ex)
                {
                    Log.Error("按比例跳转书签失败", ex);
                }
            }
        }

        private void AnnotationList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var row = AnnotationList.SelectedItem as NoteRow;
            if (row == null || !_webReady) return;

            // 标注没有锚点，按偏移估算比例不太靠谱，直接让页面滚到那条 mark 上
            try
            {
                string id = row.Id ?? string.Empty;
                string script = "(function(){var m=document.querySelector('mark.annot[data-annot=\"" +
                                id.Replace("'", "") + "\"]');if(m)m.scrollIntoView({block:'center',behavior:'smooth'});})()";
                View.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                Log.Error("跳转标注失败", ex);
            }
        }

        private void DeleteNote_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedNote(BookmarkList);
            DeleteSelectedNote(AnnotationList);
        }

        private void DeleteSelectedNote(ListBox list)
        {
            if (list == null) return;
            var row = list.SelectedItem as NoteRow;
            if (row == null) return;

            bool removed;
            if (row.Kind == "bookmark") removed = NoteStore.RemoveBookmark(Notes, row.Id);
            else removed = NoteStore.RemoveAnnotation(Notes, row.Id);

            if (!removed) return;
            SaveNotes();
            FillNotesPanel();
            PostAnnotationsToPage();
            StatusRight.Text = "已删除";
        }

        /// <summary>书签/标注列表上按 Delete 也能删。</summary>
        private void NoteList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Delete) return;
            e.Handled = true;
            DeleteSelectedNote(BookmarkList);
            DeleteSelectedNote(AnnotationList);
        }

        /// <summary>换文档时清空并重填。</summary>
        private void RefreshNotesForDocument()
        {
            FillNotesPanel();
            PostAnnotationsToPage();
        }
    }
}
