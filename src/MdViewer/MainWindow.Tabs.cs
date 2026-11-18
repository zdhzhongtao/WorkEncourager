using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MdViewer.Core;

namespace MdViewer
{
    /// <summary>标签栏里的一页。只存路径和标题，内容始终只有一份（单 WebView）。</summary>
    public sealed class TabEntry
    {
        public string Path { get; set; }
        public string Title { get; set; }
        public bool IsEpub { get; set; }
    }

    /// <summary>
    /// 多标签。
    ///
    /// 刻意**不是**「每页一个 WebView2」：一个视图就是一组 Chromium 渲染进程（实测 7 进程 ~420MB），
    /// 开五个标签直接 2GB。这里只有一份视图，切标签 = 换文档内容，滚动位置按路径各存各的
    /// （复用 settings 里的 ScrollPositions），切回来还在原处。
    /// </summary>
    public partial class MainWindow
    {
        private readonly List<TabEntry> _tabs = new List<TabEntry>();
        private bool _switchingTab;

        private TabEntry FindTab(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            foreach (var tab in _tabs)
            {
                if (NoteStore.SamePath(tab.Path, path)) return tab;
            }
            return null;
        }

        /// <summary>打开文档时登记一个标签（已存在就只更新标题）。</summary>
        private void TouchTab(string path, bool isEpub)
        {
            if (string.IsNullOrEmpty(path)) return;

            var tab = FindTab(path);
            if (tab == null)
            {
                tab = new TabEntry { Path = path, IsEpub = isEpub };
                _tabs.Add(tab);
                if (_tabs.Count > 20) _tabs.RemoveAt(0);
            }
            tab.Title = Path.GetFileNameWithoutExtension(path);
            tab.IsEpub = isEpub;

            PersistTabs();
            RefreshTabBar();
        }

        private void PersistTabs()
        {
            var paths = new List<string>();
            foreach (var tab in _tabs) paths.Add(tab.Path);
            App.Settings.OpenTabs = paths;
        }

        /// <summary>启动时按上次的标签列表铺好标签栏，并把上次看的那篇激活。</summary>
        private void RestoreTabsOnStartup()
        {
            _tabs.Clear();
            foreach (string path in App.Settings.OpenTabs)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (!File.Exists(path)) continue;
                _tabs.Add(new TabEntry
                {
                    Path = path,
                    Title = Path.GetFileNameWithoutExtension(path),
                    IsEpub = EpubReader.IsEpubFile(path)
                });
            }
            RefreshTabBar();
        }

        /// <summary>重建标签栏。数量一般不超过十个，直接整条重画比增量维护省心。</summary>
        private void RefreshTabBar()
        {
            if (TabBarHost == null) return;

            TabBarHost.Children.Clear();
            TabBar.Visibility = _tabs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

            foreach (var tab in _tabs)
            {
                bool active = NoteStore.SamePath(tab.Path, _currentPath);

                var panel = new StackPanel { Orientation = Orientation.Horizontal };

                var label = new TextBlock
                {
                    Text = tab.Title,
                    MaxWidth = 170,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12.5,
                    Foreground = Res(active ? "ThemeTextPrimary" : "ThemeTextSecondary", Brushes.Gray)
                };
                panel.Children.Add(label);

                var close = new Button
                {
                    Content = "✕",
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(7, 0, 0, 0),
                    Padding = new Thickness(0),
                    FontSize = 9,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = Res("ThemeTextMuted", Brushes.Gray),
                    Cursor = Cursors.Hand,
                    ToolTip = "关闭这个标签"
                };
                close.Click += (s, e) => CloseTab(tab.Path);
                panel.Children.Add(close);

                var border = new Border
                {
                    Padding = new Thickness(11, 5, 7, 5),
                    Margin = new Thickness(0, 0, 4, 0),
                    CornerRadius = new CornerRadius(7),
                    Cursor = Cursors.Hand,
                    Background = active
                        ? Res("ThemeCardBg", Brushes.White)
                        : Res("ThemeHoverBg", Brushes.Transparent),
                    BorderBrush = active ? Res("ThemeCardBorder", Brushes.Gray) : Brushes.Transparent,
                    BorderThickness = new Thickness(1),
                    Child = panel,
                    Tag = tab.Path
                };
                border.MouseLeftButtonDown += Tab_Click;
                border.MouseDown += Tab_MouseDown;
                border.ContextMenu = BuildTabMenu(tab.Path);
                TabBarHost.Children.Add(border);
            }

            if (_tabs.Count > 0) AutoScrollTabBar();
        }

        private ContextMenu BuildTabMenu(string path)
        {
            var menu = new ContextMenu();

            var close = new MenuItem { Header = "关闭" };
            close.Click += (s, e) => CloseTab(path);
            menu.Items.Add(close);

            var others = new MenuItem { Header = "关闭其他", IsEnabled = _tabs.Count > 1 };
            others.Click += (s, e) => CloseOtherTabs(path);
            menu.Items.Add(others);

            menu.Items.Add(new Separator());

            var reveal = new MenuItem { Header = "打开所在文件夹" };
            reveal.Click += (s, e) =>
            {
                try { OpenExternal("explorer.exe /select,\"" + path + "\""); }
                catch (Exception ex) { Log.Error("打开资源管理器失败", ex); }
            };
            menu.Items.Add(reveal);

            return menu;
        }

        private void Tab_Click(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;
            string path = border.Tag as string;
            if (string.IsNullOrEmpty(path)) return;
            if (NoteStore.SamePath(path, _currentPath)) return;

            e.Handled = true;
            _ = SwitchTabAsync(path);
        }

        /// <summary>中键关标签 —— 浏览器习惯，用户会下意识去点。</summary>
        private void Tab_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            var border = sender as Border;
            if (border == null) return;
            string path = border.Tag as string;
            if (string.IsNullOrEmpty(path)) return;
            e.Handled = true;
            CloseTab(path);
        }

        /// <summary>点标签栏空白处：没别的意思，只是别把点击漏到下面的工具栏。</summary>
        private void TabBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle) e.Handled = true;
        }

        private void AutoScrollTabBar()
        {
            try { TabScroller.ScrollToRightEnd(); }
            catch (Exception) { }
        }

        /// <summary>切到某个已打开的标签。</summary>
        private async Task SwitchTabAsync(string path)
        {
            if (string.IsNullOrEmpty(path) || _switchingTab) return;
            if (!File.Exists(path))
            {
                MessageBox.Show("这个文件已经不在了：\n" + path, "打不开");
                CloseTab(path);
                return;
            }

            _switchingTab = true;
            try
            {
                // 换文档前把当前这篇读到哪存下来（LoadDocumentAsync 内部会做），
                // 切回来时按 ScrollPositions 还原
                await LoadDocumentAsync(path, false);
            }
            finally
            {
                _switchingTab = false;
                RefreshTabBar();
            }
        }

        /// <summary>关掉一个标签；关的是当前页就切到邻居，一个不剩就回欢迎页。</summary>
        private void CloseTab(string path)
        {
            var tab = FindTab(path);
            if (tab == null) return;

            int index = _tabs.IndexOf(tab);
            bool wasActive = NoteStore.SamePath(path, _currentPath);
            _tabs.Remove(tab);

            // 关掉的文档，阅读位置和标注留着（下次在最近列表里打开还在），只清标签
            PersistTabs();

            if (!wasActive)
            {
                RefreshTabBar();
                return;
            }

            if (_tabs.Count == 0)
            {
                RefreshTabBar();
                ShowWelcome();
                return;
            }

            int next = Math.Min(index, _tabs.Count - 1);
            _ = SwitchTabAsync(_tabs[next].Path);
        }

        private void CloseOtherTabs(string path)
        {
            var keep = FindTab(path);
            if (keep == null) return;

            _tabs.Clear();
            _tabs.Add(keep);
            PersistTabs();
            RefreshTabBar();

            if (!NoteStore.SamePath(path, _currentPath)) _ = SwitchTabAsync(path);
        }

        private static Brush Res(string key, Brush fallback)
        {
            object value = null;
            try { value = Application.Current?.TryFindResource(key); }
            catch (Exception) { }
            return value as Brush ?? fallback;
        }
    }
}
