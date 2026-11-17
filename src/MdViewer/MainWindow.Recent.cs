using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MdViewer.Core;

namespace MdViewer
{
    /// <summary>最近列表 / 起始页里的一行。</summary>
    public sealed class RecentRow
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public string Dir { get; set; }
        public string When { get; set; }
        public bool Missing { get; set; }
        public Brush NameFg { get; set; }
    }

    public partial class MainWindow
    {
        private const int WelcomeRecentCount = 6;

        private static readonly DateTimeOffset Now = DateTimeOffset.Now;

        // ───────────────────────── 数据 ─────────────────────────

        private List<RecentRow> BuildRecentRows(int limit)
        {
            var rows = new List<RecentRow>();
            var entries = RecentFiles.Normalize(App.Settings.RecentEntries);
            App.Settings.RecentEntries = entries;

            Brush normal = Resolve("ThemeTextPrimary", Brushes.Black);
            Brush missing = Resolve("ThemeTextMuted", Brushes.Gray);
            int count = 0;

            foreach (var entry in entries)
            {
                if (limit > 0 && count >= limit) break;
                bool exists = RecentFiles.Exists(entry.Path);
                string name = System.IO.Path.GetFileName(entry.Path);
                string dir = System.IO.Path.GetDirectoryName(entry.Path) ?? string.Empty;

                rows.Add(new RecentRow
                {
                    Path = entry.Path,
                    Name = exists ? name : name + "（已删除）",
                    Dir = dir,
                    When = RecentFiles.Describe(entry, Now),
                    Missing = !exists,
                    NameFg = exists ? normal : missing
                });
                count++;
            }
            return rows;
        }

        private static Brush Resolve(string key, Brush fallback)
        {
            object value = Application.Current != null ? Application.Current.TryFindResource(key) : null;
            return value as Brush ?? fallback;
        }

        private void AddRecent(string path)
        {
            App.Settings.RecentEntries = RecentFiles.Add(App.Settings.RecentEntries, path, DateTimeOffset.Now);
            App.SaveSettings();
            UpdateWelcomeRecent();
            if (RecentPopup.IsOpen) RefreshRecentList();
        }

        /// <summary>文件打不开时顺手把它从最近列表里摘掉，不打扰用户。</summary>
        private void RemoveRecentSilently(string path)
        {
            int before = App.Settings.RecentEntries == null ? 0 : App.Settings.RecentEntries.Count;
            App.Settings.RecentEntries = RecentFiles.Remove(App.Settings.RecentEntries, path);
            if (App.Settings.RecentEntries.Count != before)
            {
                App.SaveSettings();
                UpdateWelcomeRecent();
            }
        }

        private void UpdateWelcomeRecent()
        {
            var rows = BuildRecentRows(WelcomeRecentCount);
            WelcomeRecentBox.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            WelcomeRecentList.ItemsSource = rows;
            WelcomeRecentHint.Text = rows.Count >= WelcomeRecentCount ? "最近 " + WelcomeRecentCount + " 份" : string.Empty;
        }

        // ───────────────────────── 下拉面板 ─────────────────────────

        private void Recent_Click(object sender, RoutedEventArgs e)
        {
            if (RecentPopup.IsOpen)
            {
                RecentPopup.IsOpen = false;
                return;
            }

            _suppressRecentFilter = true;
            RecentFilter.Text = string.Empty;
            RecentFilterHint.Visibility = Visibility.Visible;
            _suppressRecentFilter = false;

            RefreshRecentList();
            RecentPopup.IsOpen = true;
            RecentFilter.Focus();
        }

        private void RefreshRecentList()
        {
            string filter = (RecentFilter.Text ?? string.Empty).Trim();
            var rows = BuildRecentRows(0);

            if (filter.Length > 0)
            {
                var filtered = new List<RecentRow>();
                foreach (var row in rows)
                {
                    if (row.Path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        row.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        filtered.Add(row);
                }
                rows = filtered;
            }

            RecentList.ItemsSource = rows;
            RecentEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RecentEmpty.Text = filter.Length > 0 ? "没有匹配的文件" : "还没有打开过文件";

            if (rows.Count > 0)
            {
                RecentList.SelectedIndex = 0;
                RecentList.ScrollIntoView(rows[0]);
            }
        }

        private void RecentFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressRecentFilter) return;
            RecentFilterHint.Visibility = string.IsNullOrEmpty(RecentFilter.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            RefreshRecentList();
        }

        private void RecentFilter_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && RecentList.Items.Count > 0)
            {
                RecentList.Focus();
                if (RecentList.SelectedIndex < 0) RecentList.SelectedIndex = 0;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                OpenSelectedRecent(RecentList);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                RecentPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        private void RecentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 只做视觉选中，不自动打开
        }

        private void RecentList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenSelectedRecent(RecentList);
        }

        private void RecentList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OpenSelectedRecent(RecentList);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                RemoveSelectedRecent();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                RecentPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        private void RemoveSelectedRecent()
        {
            var row = RecentList.SelectedItem as RecentRow;
            if (row == null) return;

            int index = RecentList.SelectedIndex;
            App.Settings.RecentEntries = RecentFiles.Remove(App.Settings.RecentEntries, row.Path);
            App.SaveSettings();
            RefreshRecentList();
            UpdateWelcomeRecent();

            if (RecentList.Items.Count > 0)
                RecentList.SelectedIndex = Math.Min(index, RecentList.Items.Count - 1);
            StatusRight.Text = "已从最近列表移除：" + System.IO.Path.GetFileName(row.Path);
        }

        private void RecentClear_Click(object sender, RoutedEventArgs e)
        {
            App.Settings.RecentEntries = new List<RecentEntry>();
            App.SaveSettings();
            RefreshRecentList();
            UpdateWelcomeRecent();
            StatusRight.Text = "最近列表已清空";
        }

        private void OpenSelectedRecent(ListBox list)
        {
            var row = list == null ? null : list.SelectedItem as RecentRow;
            if (row == null) return;

            RecentPopup.IsOpen = false;
            if (row.Missing)
            {
                var answer = MessageBox.Show(
                    "这个文件已经不在原来的位置了：\n" + row.Path +
                    "\n\n要把它从最近列表里移除吗？", "文件找不到",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Yes)
                {
                    App.Settings.RecentEntries = RecentFiles.Remove(App.Settings.RecentEntries, row.Path);
                    App.SaveSettings();
                    UpdateWelcomeRecent();
                }
                return;
            }

            OpenFile(row.Path, true);
        }

        // ───────────────────────── 起始页的最近列表 ─────────────────────────

        private void WelcomeRecentList_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenSelectedRecent(WelcomeRecentList);
        }

        private void WelcomeRecentList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OpenSelectedRecent(WelcomeRecentList);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                var row = WelcomeRecentList.SelectedItem as RecentRow;
                if (row == null) return;
                App.Settings.RecentEntries = RecentFiles.Remove(App.Settings.RecentEntries, row.Path);
                App.SaveSettings();
                UpdateWelcomeRecent();
                e.Handled = true;
            }
        }

        // ───────────────────────── 前进 / 后退 ─────────────────────────

        private void PushHistory(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (_historyIndex >= 0 && _historyIndex < _history.Count &&
                string.Equals(_history[_historyIndex], path, StringComparison.OrdinalIgnoreCase))
                return;

            // 从中间开了新文档，后面的前进记录作废
            if (_historyIndex < _history.Count - 1)
                _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);

            _history.Add(path);
            _historyIndex = _history.Count - 1;
            UpdateNavButtons();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_historyIndex <= 0) return;
            _historyIndex--;
            string target = _history[_historyIndex];
            UpdateNavButtons();
            OpenFile(target, false);
        }

        private void Forward_Click(object sender, RoutedEventArgs e)
        {
            if (_historyIndex < 0 || _historyIndex >= _history.Count - 1) return;
            _historyIndex++;
            string target = _history[_historyIndex];
            UpdateNavButtons();
            OpenFile(target, false);
        }

        // ───────────────────────── 「更多」菜单 ─────────────────────────

        private void More_Click(object sender, RoutedEventArgs e)
        {
            var menu = BuildMoreMenu();
            menu.PlacementTarget = MoreButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private ContextMenu BuildMoreMenu()
        {
            var menu = new ContextMenu();
            bool hasDoc = !string.IsNullOrEmpty(_currentPath);

            var reload = new MenuItem { Header = "重新读取", InputGestureText = "F5", IsEnabled = hasDoc };
            reload.Click += async (s, args) => await ReloadCurrentAsync(false);
            menu.Items.Add(reload);

            var folder = new MenuItem { Header = "打开所在文件夹", IsEnabled = hasDoc };
            folder.Click += (s, args) => RevealInExplorer();
            menu.Items.Add(folder);

            var copy = new MenuItem { Header = "复制文件路径", IsEnabled = hasDoc };
            copy.Click += (s, args) => CopyPathToClipboard();
            menu.Items.Add(copy);

            menu.Items.Add(new Separator());

            var pdf = new MenuItem { Header = "导出 PDF", InputGestureText = "Ctrl+P", IsEnabled = hasDoc };
            pdf.Click += (s, args) => ExportPdf_Click(s, new RoutedEventArgs());
            menu.Items.Add(pdf);

            var html = new MenuItem { Header = "导出 HTML", IsEnabled = hasDoc };
            html.Click += (s, args) => ExportHtml_Click(s, new RoutedEventArgs());
            menu.Items.Add(html);

            menu.Items.Add(new Separator());

            bool associated = FileAssoc.IsAssociated(Environment.ProcessPath ?? string.Empty);
            var assoc = new MenuItem { Header = "关联 .md 文件", IsEnabled = !associated };
            assoc.Click += (s, args) => Assoc_Click(s, new RoutedEventArgs());
            menu.Items.Add(assoc);

            var unassoc = new MenuItem { Header = "取消关联", IsEnabled = associated };
            unassoc.Click += (s, args) => Unassoc_Click(s, new RoutedEventArgs());
            menu.Items.Add(unassoc);

            menu.Items.Add(new Separator());

            var settings = new MenuItem { Header = "设置…", InputGestureText = "Ctrl+," };
            settings.Click += (s, args) => Settings_Click(s, new RoutedEventArgs());
            menu.Items.Add(settings);

            var about = new MenuItem { Header = "关于 Markdown 阅读器" };
            about.Click += (s, args) => ShowAbout();
            menu.Items.Add(about);

            return menu;
        }

        private void RevealInExplorer()
        {
            if (string.IsNullOrEmpty(_currentPath)) return;
            try
            {
                OpenExternal("explorer.exe /select,\"" + _currentPath + "\"");
            }
            catch (Exception ex)
            {
                Log.Error("打开资源管理器失败", ex);
            }
        }

        private void CopyPathToClipboard()
        {
            if (string.IsNullOrEmpty(_currentPath)) return;
            try
            {
                Clipboard.SetText(_currentPath);
                StatusRight.Text = "路径已复制到剪贴板";
            }
            catch (Exception ex)
            {
                Log.Error("复制路径失败", ex);
                StatusRight.Text = "复制失败：" + ex.Message;
            }
        }
    }
}
