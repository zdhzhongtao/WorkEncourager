using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MdViewer.Core;

namespace MdViewer
{
    /// <summary>
    /// 窗口骨架：侧栏页签、工具栏上那几个新按钮、多文件拖入、定时换肤。
    /// </summary>
    public partial class MainWindow
    {
        private bool _suppressSidebarTab;

        // ───────────────────────── 侧栏页签 ─────────────────────────

        private void SidebarTab_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressSidebarTab) return;

            string tab = "toc";
            if (ReferenceEquals(sender, TabFiles)) tab = "files";
            else if (ReferenceEquals(sender, TabNotes)) tab = "notes";
            SwitchSidebarTab(tab);
        }

        /// <summary>切换侧栏内容：大纲 / 文件 / 笔记。三种面板都常驻，只切可见性。</summary>
        private void SwitchSidebarTab(string tab)
        {
            if (tab != "files" && tab != "notes") tab = "toc";

            _suppressSidebarTab = true;
            TabToc.IsChecked = tab == "toc";
            TabFiles.IsChecked = tab == "files";
            TabNotes.IsChecked = tab == "notes";
            _suppressSidebarTab = false;

            TocBody.Visibility = tab == "toc" ? Visibility.Visible : Visibility.Collapsed;
            FolderPanel.Visibility = tab == "files" ? Visibility.Visible : Visibility.Collapsed;
            NotesPanel.Visibility = tab == "notes" ? Visibility.Visible : Visibility.Collapsed;
            TocCount.Visibility = tab == "toc" ? Visibility.Visible : Visibility.Collapsed;

            TocTitle.Text = tab == "files"
                ? FolderTitleText()
                : (tab == "notes" ? "笔记" : (_currentKind == KindEpub ? "章节" : "大纲"));

            if (App.Settings.SidebarTab != tab)
            {
                App.Settings.SidebarTab = tab;
                App.SaveSettings();
            }
        }

        private void PickFolder_Click(object sender, RoutedEventArgs e)
        {
            PickFolder();
        }        private void ReloadFolder_Click(object sender, RoutedEventArgs e)
        {
            ReloadFolderTree();
        }

        // ───────────────────────── 目录过滤 ─────────────────────────

        private void TocFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyTocFilter();
        }

        /// <summary>
        /// 目录里敲关键字只留匹配项。长文档一章几十个小节时，靠滚动找标题太费劲。
        /// 过滤只改显示，不动正文；清空就还原全部。
        /// </summary>
        private void ApplyTocFilter()
        {
            if (_tocRows == null || TocList == null) return;

            string keyword = TocFilterBox != null ? (TocFilterBox.Text ?? string.Empty).Trim() : string.Empty;

            if (keyword.Length == 0)
            {
                _tocView = _tocRows;
            }
            else
            {
                var hits = new List<TocRow>();
                foreach (var row in _tocRows)
                {
                    if (row == null || row.Text == null) continue;
                    if (row.Text.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) >= 0)
                        hits.Add(row);
                }
                _tocView = hits;
            }

            TocList.ItemsSource = null;
            TocList.ItemsSource = _tocView;

            if (TocCount != null)
            {
                if (_tocRows.Count == 0) TocCount.Text = string.Empty;
                else if (keyword.Length == 0) TocCount.Text = _tocRows.Count + " 项";
                else TocCount.Text = _tocView.Count + " / " + _tocRows.Count;
            }
        }

        /// <summary>过滤框里回车：跳到第一条匹配的标题；Esc 清空。</summary>
        private void TocFilter_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                TocFilterBox.Text = string.Empty;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                var first = TocList.Items.Count > 0 ? TocList.Items[0] as TocRow : null;
                if (first != null && !string.IsNullOrEmpty(first.Id) && _webReady)
                    PostToPage("scrollTo", first.Id);
                e.Handled = true;
            }
        }

        // ───────────────────────── 工具栏按钮 ─────────────────────────

        private void Speak_Click(object sender, RoutedEventArgs e)
        {
            ToggleSpeak();
        }

        private void Bookmark_Click(object sender, RoutedEventArgs e)
        {
            AddBookmark();
        }

        private void Zen_Click(object sender, RoutedEventArgs e)
        {
            ToggleZenMode();
        }

        // ───────────────────────── 多文件拖入 ─────────────────────────

        /// <summary>
        /// 一次拖进来一堆文件时：第一个就地打开，其余进标签栏排队。
        /// 以前只取第一个，剩下的直接丢掉，拖一个文件夹进去等于什么都没发生。
        /// </summary>
        private async void OpenMany(string[] files)
        {
            if (files == null || files.Length == 0) return;

            string first = null;
            var rest = new System.Collections.Generic.List<string>();

            foreach (string file in files)
            {
                if (string.IsNullOrWhiteSpace(file)) continue;
                if (Directory.Exists(file))
                {
                    // 拖进来的是文件夹：当书库打开
                    App.Settings.FolderRoot = file;
                    App.SaveSettings();
                    ReloadFolderTree();
                    SwitchSidebarTab("files");
                    if (first == null) StatusRight.Text = "已打开文件夹：" + Path.GetFileName(file);
                    continue;
                }
                if (!File.Exists(file) || !IsReadableDocument(file)) continue;
                if (first == null) first = file;
                else rest.Add(file);
            }

            if (first != null) OpenFile(first, true);

            // 其余的不直接渲染（每个都要一次完整加载），只是登记成标签
            foreach (string path in rest) TouchTab(path, EpubReader.IsEpubFile(path));

            if (rest.Count > 0)
            {
                int already = 0;
                foreach (string path in rest)
                {
                    if (NoteStore.SamePath(path, _currentPath)) already++;
                }
                StatusRight.Text = string.Format("已打开 {0} 个文档（另有 {1} 个在标签栏）",
                    1 + already, rest.Count - already);
            }
        }

        // ───────────────────────── 定时换肤 ─────────────────────────

        private System.Windows.Threading.DispatcherTimer _autoDimTimer;

        /// <summary>
        /// 定时换肤：每 30 秒看一次表，到点就切。比「跟随系统」准 ——
        /// Windows 的应用模式很多人设成浅色就再也没改过。
        /// </summary>
        private void StartAutoDimTimer()
        {
            if (_autoDimTimer != null) return;

            _autoDimTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _autoDimTimer.Tick += (s, e) => ApplyAutoDim();
            _autoDimTimer.Start();
        }

        /// <summary>设置里改了开关或时间段，立刻重算一次，不等下一个 30 秒。</summary>
        public void RefreshAutoDim()
        {
            ApplyAutoDim();
        }

        private void ApplyAutoDim()
        {
            if (App.Settings == null) return;

            // 用户手动固定了浅色 / 深色时不去抢方向盘
            string mode = (App.Settings.Theme ?? "system").Trim().ToLowerInvariant();
            if (!App.Settings.AutoDim || mode != "system")
            {
                if (!App.DarkOverride.HasValue) return;
                App.DarkOverride = null;
                RefreshThemeEverywhere();
                return;
            }

            bool wantDark = IsWithinDimWindow(DateTime.Now, App.Settings.AutoDimFrom, App.Settings.AutoDimTo);
            if (App.DarkOverride.HasValue && App.DarkOverride.Value == wantDark) return;
            if (!App.DarkOverride.HasValue && wantDark == App.SystemPrefersDark()) return;

            App.DarkOverride = wantDark;
            RefreshThemeEverywhere();
            StatusRight.Text = wantDark ? "定时换肤：已切到深色" : "定时换肤：已切回浅色";
        }

        /// <summary>配色变了之后要刷新三处：WPF 资源、界面图标、页面 css 变量。</summary>
        private void RefreshThemeEverywhere()
        {
            App.ApplyTheme(Theme.ForMarkdownTheme(App.EffectiveMarkdownTheme));
            UpdateChrome();
            ApplyViewBackground();
            PostThemeToPage();
        }

        /// <summary>
        /// 现在是不是在「深色时段」里。支持跨零点（19:00 → 07:00 这种）。
        /// </summary>
        public static bool IsWithinDimWindow(DateTime now, string from, string to)
        {
            TimeSpan start, end;
            if (!TryParseClock(from, out start) || !TryParseClock(to, out end)) return false;

            TimeSpan current = now.TimeOfDay;
            if (start == end) return false;
            return start < end
                ? current >= start && current < end
                : current >= start || current < end;
        }

        private static bool TryParseClock(string text, out TimeSpan value)
        {
            value = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string[] parts = text.Trim().Split(':');
            int hour, minute;
            if (parts.Length != 2) return false;
            if (!int.TryParse(parts[0], out hour) || !int.TryParse(parts[1], out minute)) return false;
            if (hour < 0 || hour > 23 || minute < 0 || minute > 59) return false;

            value = new TimeSpan(hour, minute, 0);
            return true;
        }
    }
}
