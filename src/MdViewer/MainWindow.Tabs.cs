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

                // 老配置里可能存着 "C:/a/b.md" 这种正斜杠写法，统一成完整路径再收进来，
                // 否则加进来的是另一条字符串，同一篇文档会出现两个标签
                string full;
                try { full = Path.GetFullPath(path); }
                catch (Exception) { continue; }

                if (!File.Exists(full)) continue;
                if (_tabs.Exists(t => NoteStore.SamePath(t.Path, full))) continue;

                _tabs.Add(new TabEntry
                {
                    Path = full,
                    Title = Path.GetFileNameWithoutExtension(full),
                    IsEpub = EpubReader.IsEpubFile(full)
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

            bool prevActive = false;

            for (int i = 0; i < _tabs.Count; i++)
            {
                TabEntry tab = _tabs[i];
                bool active = NoteStore.SamePath(tab.Path, _currentPath);

                // 相邻两个都不是当前标签时夹一条细竖线。Chrome 用这条线区分相邻标签，
                // 而不是靠留缝；当前标签两侧不画，看上去才是「凸出来」的那一枚。
                if (i > 0 && !active && !prevActive)
                {
                    TabBarHost.Children.Add(new Border
                    {
                        Width = 1,
                        Height = 15,
                        VerticalAlignment = VerticalAlignment.Center,
                        Background = Res("ThemeDivider", Brushes.Gray)
                    });
                }
                prevActive = active;

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

                // 关闭按钮用两条线画出来，不用 "✕" 字符：字符的字形基线和字号跟正文对不上，
                // 视觉上总是偏上偏下，换字体还会变。矢量按 8×8 的方框居中，跟文字中心严格对齐。
                var closeIcon = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M 0,0 L 8,8 M 8,0 L 0,8"),
                    Stroke = Res("ThemeTextMuted", Brushes.Gray),
                    StrokeThickness = 1.3,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Stretch = Stretch.None,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var close = new Border
                {
                    Width = 18,
                    Height = 18,
                    Margin = new Thickness(6, 0, 0, 0),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "关闭这个标签",
                    Child = closeIcon
                };

                close.MouseEnter += (s, e) => closeIcon.Stroke = Res("ThemeTextPrimary", Brushes.Black);
                close.MouseLeave += (s, e) => closeIcon.Stroke = Res("ThemeTextMuted", Brushes.Gray);
                close.MouseLeftButtonDown += (s, e) =>
                {
                    // 别让这次点击继续冒泡到标签上，否则"关标签"会顺带触发"切到这个标签"
                    e.Handled = true;
                    CloseTab(tab.Path);
                };
                panel.Children.Add(close);

                var border = new Border
                {
                    Height = 34,
                    Padding = new Thickness(12, 0, 8, 0),
                    Margin = new Thickness(0),
                    // 只圆上面两角，底边是直角才能和下面的正文接成一片
                    CornerRadius = new CornerRadius(7, 7, 0, 0),
                    Cursor = Cursors.Hand,
                    Background = active ? Res("ThemeWindowBg", Brushes.White) : Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Child = panel,
                    Tag = tab.Path
                };

                if (!active)
                {
                    // 没被选中的标签只在鼠标路过时给一点底色，平时是透明的，
                    // 让它彻底融进标签栏，视觉重心只落在当前那一个上
                    border.MouseEnter += (s, e) => border.Background = Res("ThemeHoverBg", Brushes.Transparent);
                    border.MouseLeave += (s, e) => border.Background = Brushes.Transparent;
                }

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
                try { RevealInExplorer(path); }
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
            if (_tabs.Count == 0) return;   // 标签已全部关掉，别让在途的切换把正文又加载回来
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
            _tabs.Remove(tab);

            // 关掉的文档，阅读位置和标注留着（下次在最近列表里打开还在），只清标签
            PersistTabs();

            // 一个都不剩就回欢迎页。这个判断必须在 wasActive 之前：快速连点关闭时，
            // 切换标签是异步的，_currentPath 还停在上一页，wasActive 会误判成 false，
            // 导致视图留在刚关掉的文档上不回欢迎页。
            if (_tabs.Count == 0)
            {
                RefreshTabBar();
                ShowWelcome();
                return;
            }

            // 当前显示的文档已经不在标签里（异步切换还没跟上）时也得切走，
            // 否则关掉一个标签后视图仍停在已关闭的那一页。
            bool currentStillOpen = FindTab(_currentPath) != null;
            bool wasActive = NoteStore.SamePath(path, _currentPath) || !currentStillOpen;
            if (!wasActive)
            {
                RefreshTabBar();
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
