using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MdViewer.Core;

namespace MdViewer
{
    /// <summary>设置窗口。改动即时生效、即时落盘，没有「取消」这个概念。</summary>
    public partial class SettingsWindow : Window
    {
        public const string SectionUpdate = "update";

        private bool _loading = true;
        private string _requestedSection;
        private UpdateResult _pendingUpdate;

        public SettingsWindow()
        {
            InitializeComponent();
            Loaded += SettingsWindow_Loaded;
        }

        /// <summary>主窗口要求打开后直接滚到某一块（比如点「有新版本」徽章）。</summary>
        public void RequestSection(string section)
        {
            _requestedSection = section;
        }

        /// <summary>自检截图用：滚到底部，把「重置」「关于」两块拍进去。</summary>
        public void ScrollToBottom()
        {
            SettingsScroll.ScrollToEnd();
        }

        /// <summary>设置里没有需要整页重渲染的项，全部靠 CSS 变量即时生效。</summary>
        public bool NeedsRerender
        {
            get { return false; }
        }

        private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadValues();
            _loading = false;

            if (string.Equals(_requestedSection, SectionUpdate, StringComparison.Ordinal))
                SectionUpdateCard.BringIntoView();
        }

        private void LoadValues()
        {
            string theme = (App.Settings.Theme ?? "system").ToLowerInvariant();
            if (theme == "dark") ThemeDark.IsChecked = true;
            else if (theme == "light") ThemeLight.IsChecked = true;
            else ThemeSystem.IsChecked = true;

            // 两个下拉各装一组主题：浅色槽位只列浅色，深色槽位只列深色，避免选出「深底配深字」
            LightThemeBox.ItemsSource = MarkdownThemes.OfMode(false);
            LightThemeBox.SelectedItem = MarkdownThemes.Resolve(App.Settings.LightThemeId) ?? MarkdownThemes.DefaultLight;

            DarkThemeBox.ItemsSource = MarkdownThemes.OfMode(true);
            DarkThemeBox.SelectedItem = MarkdownThemes.Resolve(App.Settings.DarkThemeId) ?? MarkdownThemes.DefaultDark;

            FontValue.Text = App.Settings.FontSize.ToString();
            WidthSlider.Value = App.Settings.MaxContentWidth;
            WidthValue.Text = ((int)WidthSlider.Value) + " px";

            AutoReloadBox.IsChecked = App.Settings.AutoReload;
            RememberScrollBox.IsChecked = App.Settings.RememberScroll;
            TocVisibleBox.IsChecked = App.Settings.TocVisible;

            CheckOnStartBox.IsChecked = App.Settings.CheckUpdateOnStart;
            RepoBox.Text = App.Settings.UpdateRepo;

            VersionText.Text = App.VersionText;
            AboutText.Text = "Markdown 阅读器 " + App.VersionText + "\n"
                + ".md / .epub 本地阅读，纯离线，不发送任何数据。\n"
                + "数据目录：" + App.DataDir;

            RefreshAssoc();
            UpdateStatus.Text = string.IsNullOrWhiteSpace(App.Settings.LastUpdateCheck)
                ? "还没检查过。"
                : "上次检查：" + DescribeLastCheck(App.Settings.LastUpdateCheck);
        }

        private static string DescribeLastCheck(string iso)
        {
            DateTimeOffset time;
            if (!DateTimeOffset.TryParse(iso, out time)) return "未知时间";
            TimeSpan span = DateTimeOffset.Now - time;
            if (span.TotalMinutes < 2) return "刚刚";
            if (span.TotalHours < 1) return (int)span.TotalMinutes + " 分钟前";
            if (span.TotalDays < 1) return (int)span.TotalHours + " 小时前";
            return time.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        }

        private void RefreshAssoc()
        {
            bool associated = FileAssoc.IsAssociated(Environment.ProcessPath ?? string.Empty);
            AssocStatus.Text = associated
                ? "已经关联：双击 .md 会用本程序打开。"
                : "还没有关联：双击 .md 会交给 Windows 默认程序。";
            AssocButton.IsEnabled = !associated;
            UnassocButton.IsEnabled = associated;
        }

        // ───────────────────────── 外观 ─────────────────────────

        private void Theme_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;

            if (ThemeDark.IsChecked == true) App.Settings.Theme = "dark";
            else if (ThemeLight.IsChecked == true) App.Settings.Theme = "light";
            else App.Settings.Theme = "system";

            App.ApplyTheme(Theme.ForMarkdownTheme(App.EffectiveMarkdownTheme));
            App.SaveSettings();
            NotifyOwner();
        }

        private void ThemeBox_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;

            var light = LightThemeBox.SelectedItem as MarkdownTheme;
            var dark = DarkThemeBox.SelectedItem as MarkdownTheme;

            bool changed = false;
            if (light != null && !string.Equals(light.Id, App.Settings.LightThemeId, StringComparison.OrdinalIgnoreCase))
            {
                App.Settings.LightThemeId = light.Id;
                changed = true;
            }
            if (dark != null && !string.Equals(dark.Id, App.Settings.DarkThemeId, StringComparison.OrdinalIgnoreCase))
            {
                App.Settings.DarkThemeId = dark.Id;
                changed = true;
            }
            if (!changed) return;

            App.ApplyTheme(Theme.ForMarkdownTheme(App.EffectiveMarkdownTheme));
            App.SaveSettings();
            NotifyOwner();
        }

        private void FontSmaller_Click(object sender, RoutedEventArgs e)
        {
            SetFontSize(App.Settings.FontSize - 1);
        }

        private void FontBigger_Click(object sender, RoutedEventArgs e)
        {
            SetFontSize(App.Settings.FontSize + 1);
        }

        private void SetFontSize(int size)
        {
            if (size < 14) size = 14;
            if (size > 22) size = 22;
            if (size == App.Settings.FontSize) return;

            App.Settings.FontSize = size;
            FontValue.Text = size.ToString();
            App.SaveSettings();
            NotifyOwner();
        }

        private void WidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (WidthValue == null) return;
            WidthValue.Text = ((int)e.NewValue) + " px";
            if (_loading) return;

            App.Settings.MaxContentWidth = (int)e.NewValue;
            // 正在拖这个滑杆说明用户想看宽度效果；宽屏开着的话先关掉，否则屏幕上一点变化都没有
            App.Settings.WideMode = false;
            App.SaveSettings();
            NotifyOwner();
        }

        private void Option_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;

            App.Settings.AutoReload = AutoReloadBox.IsChecked == true;
            App.Settings.RememberScroll = RememberScrollBox.IsChecked == true;
            App.Settings.TocVisible = TocVisibleBox.IsChecked == true;
            App.Settings.CheckUpdateOnStart = CheckOnStartBox.IsChecked == true;
            App.SaveSettings();
            NotifyOwner();
        }

        private void NotifyOwner()
        {
            var owner = Owner as MainWindow;
            if (owner != null) owner.ApplySettingsPreview();
        }

        // ───────────────────────── 文件关联 ─────────────────────────

        private void Assoc_Click(object sender, RoutedEventArgs e)
        {
            string exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
            {
                MessageBox.Show("拿不到本程序路径，关联失败。", "关联 .md 文件");
                return;
            }

            try
            {
                var plan = FileAssoc.Register(exe);
                App.Settings.AssocHinted = true;
                App.SaveSettings();
                RefreshAssoc();

                string extra = plan.SkippedDefaults.Count > 0
                    ? "\n\n这些扩展名被别的程序占着，只登记成「可选用本程序打开」：\n· " + string.Join("\n· ", plan.SkippedDefaults)
                    : string.Empty;

                MessageBox.Show("已注册到当前用户（HKCU），不需要管理员。\n\n"
                    + FileAssociation.UserHint + extra, "关联 .md 文件");
            }
            catch (Exception ex)
            {
                Log.Error("写注册表失败", ex);
                MessageBox.Show("关联失败：" + ex.Message, "出错了");
            }
        }

        private void Unassoc_Click(object sender, RoutedEventArgs e)
        {
            var answer = MessageBox.Show(
                "只删除本程序自己写的注册表键（MdViewer.md 与扩展名指向），不动其他程序的 ProgId。继续？",
                "取消关联", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            try
            {
                FileAssoc.Unregister();
                RefreshAssoc();
                MessageBox.Show("已经取消关联。", "取消关联");
            }
            catch (Exception ex)
            {
                Log.Error("取消关联失败", ex);
                MessageBox.Show("取消关联失败：" + ex.Message, "出错了");
            }
        }

        // ───────────────────────── 更新 ─────────────────────────

        private void RepoBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string repo = (RepoBox.Text ?? string.Empty).Trim().Trim('/');
            if (string.IsNullOrWhiteSpace(repo)) return;
            if (repo.IndexOf('/') <= 0)
            {
                UpdateStatus.Text = "仓库格式应该是 owner/repo，比如 zhangsan/WadeToolbox。";
                return;
            }

            App.Settings.UpdateRepo = repo;
            App.SaveSettings();
        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            RepoBox_LostFocus(sender, new RoutedEventArgs());

            CheckButton.IsEnabled = false;
            InstallButton.Visibility = Visibility.Collapsed;
            SkipButton.Visibility = Visibility.Collapsed;
            UpdateNotes.Visibility = Visibility.Collapsed;
            DownloadBar.Visibility = Visibility.Collapsed;
            UpdateStatus.Text = "正在检查…";

            UpdateResult result = await UpdateChecker.CheckAsync(
                App.Settings.UpdateRepo, App.VersionText, CancellationToken.None);

            CheckButton.IsEnabled = true;
            App.Settings.LastUpdateCheck = DateTimeOffset.Now.ToString("o");
            App.SaveSettings();

            if (!string.IsNullOrEmpty(result.Error))
            {
                UpdateStatus.Text = result.Error;
                return;
            }

            if (!result.HasUpdate)
            {
                UpdateStatus.Text = "已经是最新版本（" + App.VersionText + "）。";
                (Owner as MainWindow)?.HideUpdateBadge();
                return;
            }

            string date = result.PublishedAt.HasValue
                ? "　发布于 " + result.PublishedAt.Value.LocalDateTime.ToString("yyyy-MM-dd")
                : string.Empty;
            UpdateStatus.Text = "发现新版本 " + result.Latest + "（当前 " + App.VersionText + "）" + date;

            if (!string.IsNullOrWhiteSpace(result.Notes))
            {
                string notes = result.Notes.Trim();
                if (notes.Length > 800) notes = notes.Substring(0, 800) + "…";
                UpdateNotes.Text = notes;
                UpdateNotes.Visibility = Visibility.Visible;
            }

            if (!result.CanDownload)
            {
                UpdateStatus.Text += "\n这个 Release 里没有 .exe 附件，只能去页面手动下载。";
                if (!string.IsNullOrEmpty(result.PageUrl))
                    Process.Start(new ProcessStartInfo(result.PageUrl) { UseShellExecute = true });
                return;
            }

            _pendingUpdate = result;
            InstallButton.Visibility = Visibility.Visible;
            SkipButton.Visibility = Visibility.Visible;
        }

        private void SkipUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingUpdate == null) return;
            App.Settings.SkippedVersion = _pendingUpdate.Latest;
            App.SaveSettings();
            (Owner as MainWindow)?.HideUpdateBadge();
            UpdateStatus.Text = "已跳过 " + _pendingUpdate.Latest + "，下次检查不会再提示这个版本。";
            InstallButton.Visibility = Visibility.Collapsed;
            SkipButton.Visibility = Visibility.Collapsed;
        }

        private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingUpdate == null) return;

            string dir = Path.Combine(App.DataDir, "update");
            string staged;
            try
            {
                Directory.CreateDirectory(dir);
                staged = Path.Combine(dir, _pendingUpdate.AssetName);
            }
            catch (Exception ex)
            {
                UpdateStatus.Text = "建不了下载目录：" + ex.Message;
                return;
            }

            CheckButton.IsEnabled = false;
            InstallButton.IsEnabled = false;
            SkipButton.IsEnabled = false;
            DownloadBar.Value = 0;
            DownloadBar.Visibility = Visibility.Visible;
            UpdateStatus.Text = "正在下载 " + _pendingUpdate.AssetName + "…";

            var progress = new Progress<double>(value =>
            {
                DownloadBar.Value = value;
                UpdateStatus.Text = "正在下载… " + (int)Math.Round(value * 100) + "%";
            });

            string error = await UpdateChecker.DownloadAsync(
                _pendingUpdate.AssetUrl, staged, progress, CancellationToken.None);

            CheckButton.IsEnabled = true;
            InstallButton.IsEnabled = true;
            SkipButton.IsEnabled = true;
            DownloadBar.Visibility = Visibility.Collapsed;

            if (error != null)
            {
                UpdateStatus.Text = error;
                return;
            }

            ReplaceSelf(staged);
        }

        /// <summary>
        /// 把下载好的新 exe 换上去。运行中的 exe 允许重命名（同卷），
        /// 所以顺序是：复制成 .new → 老的改名 .old → 新的就位 → 重启自己。
        /// </summary>
        private void ReplaceSelf(string staged)
        {
            string target = Environment.ProcessPath;
            if (string.IsNullOrEmpty(target))
            {
                UpdateStatus.Text = "拿不到本程序路径，请手动把下载的文件替换过去：\n" + staged;
                return;
            }

            var confirm = MessageBox.Show(
                "新版本已下载好。\n\n会关闭本程序、替换文件，然后自动重新打开。继续吗？",
                "安装更新", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            string incoming = target + ".new";
            string backup = target + ".old";

            try
            {
                File.Copy(staged, incoming, true);

                if (File.Exists(backup))
                {
                    try { File.Delete(backup); }
                    catch (Exception) { backup = target + "." + DateTime.Now.Ticks + ".old"; }
                }

                File.Move(target, backup);
                File.Move(incoming, target);

                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Log.Error("替换程序文件失败", ex);
                UpdateStatus.Text = "替换失败：" + ex.Message +
                    "\n\n可以手动把下面这个文件复制过去覆盖（需要先关掉本程序）：\n" + staged;
                try { if (File.Exists(incoming)) File.Delete(incoming); } catch (Exception) { }
            }
        }

        // ───────────────────────── 重置 ─────────────────────────

        /// <summary>
        /// 恢复默认设置。改的是本窗口背后那一个 App.Settings 实例（整体替换），
        /// 所以重置完必须重新 LoadValues，否则界面上还是旧值。
        /// </summary>
        private void ResetSettings_Click(object sender, RoutedEventArgs e)
        {
            var answer = MessageBox.Show(
                "会把外观、阅读、更新相关的设置恢复成默认值。\n\n"
                + "保留：最近打开、每篇的阅读位置、窗口大小。\n"
                + "重置前会把当前设置备份到数据目录。\n\n继续吗？",
                "恢复默认设置", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            string backup = App.ResetSettings();

            _loading = true;
            LoadValues();
            _loading = false;
            NotifyOwner();

            // 更新结果、待装的那一版都是重置前那次检查的残留，一并清掉
            _pendingUpdate = null;
            InstallButton.Visibility = Visibility.Collapsed;
            SkipButton.Visibility = Visibility.Collapsed;
            UpdateNotes.Visibility = Visibility.Collapsed;

            var owner = Owner as MainWindow;
            if (owner != null)
                owner.ShowToast("已恢复默认设置",
                    backup == null ? "当前设置已重置" : "重置前的设置备份在 " + Path.GetFileName(backup),
                    App.DataDir);
        }

        // ───────────────────────── 关于 / 杂项 ─────────────────────────

        private void OpenDataDir_Click(object sender, RoutedEventArgs e)
        {
            OpenPath(App.DataDir);
        }

        private void OpenLog_Click(object sender, RoutedEventArgs e)
        {
            OpenPath(Log.FilePath);
        }

        private void OpenPath(string path)
        {
            try
            {
                if (File.Exists(path))
                    Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
                else
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("打开路径失败：" + path, ex);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }
    }
}
