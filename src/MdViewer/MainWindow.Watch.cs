using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using MdViewer.Core;
using Microsoft.Web.WebView2.Core;

namespace MdViewer
{
    public partial class MainWindow
    {
        // ───────────────────────── 文件监听 ─────────────────────────

        /// <summary>
        /// 盯住当前文档。别的程序（编辑器、git、同步盘）一改就自动重新渲染。
        /// 用目录 + Filter 而不是直接盯文件，因为很多编辑器保存时是「删了重建」，
        /// 盯 inode 的写法会立刻失效。
        /// </summary>
        private void StartWatching()
        {
            StopWatching();

            if (string.IsNullOrEmpty(_currentPath)) return;
            if (!App.Settings.AutoReload) return;

            string dir = Path.GetDirectoryName(_currentPath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            try
            {
                var watcher = new FileSystemWatcher(dir)
                {
                    Filter = Path.GetFileName(_currentPath),
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size |
                                   NotifyFilters.FileName | NotifyFilters.CreationTime,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = false
                };

                watcher.Changed += OnSourceTouched;
                watcher.Created += OnSourceTouched;
                watcher.Deleted += OnSourceGone;
                watcher.Renamed += OnSourceRenamed;
                watcher.Error += OnWatcherError;
                watcher.EnableRaisingEvents = true;

                _watcher = watcher;
            }
            catch (Exception ex)
            {
                Log.Error("启动文件监听失败", ex);
                _watcher = null;
            }

            UpdateWatchHint();
        }

        private void StopWatching()
        {
            if (_watcher == null) return;
            try
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnSourceTouched;
                _watcher.Created -= OnSourceTouched;
                _watcher.Deleted -= OnSourceGone;
                _watcher.Renamed -= OnSourceRenamed;
                _watcher.Error -= OnWatcherError;
                _watcher.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error("停止文件监听失败", ex);
            }
            _watcher = null;
            UpdateWatchHint();
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            // 缓冲区溢出之类的：重启监听，别让自动刷新就这么静默死掉
            Log.Error("文件监听出错", e.GetException());
            StartWatching();
        }

        /// <summary>文件被写入：防抖，等写入方彻底写完再读一次。</summary>
        private void OnSourceTouched(object sender, FileSystemEventArgs e)
        {
            if (!App.Settings.AutoReload) return;
            _reloadTimer.Stop();
            _reloadTimer.Start();
        }

        private void OnSourceGone(object sender, FileSystemEventArgs e)
        {
            _reloadTimer.Stop();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                StatusRight.Text = "文件被删除或移走了，内容还是上次读到的";
                ShowReloadHint("文件已不存在", false);
            }));
        }

        private void OnSourceRenamed(object sender, RenamedEventArgs e)
        {
            _reloadTimer.Stop();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                StatusRight.Text = "文件被重命名了：" + Path.GetFileName(e.FullPath);
                ShowReloadHint("文件已被重命名", false);
            }));
        }

        private void UpdateWatchHint()
        {
            bool on = _watcher != null;
            AutoWatchHint.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            ReloadButton.ToolTip = App.Settings.AutoReload
                ? "重新读取这个文件 (F5)　·　已开启自动刷新"
                : "重新读取这个文件 (F5)";
        }

        private void ShowReloadHint(string text, bool positive)
        {
            ReloadHint.Text = text;
            ReloadHint.Foreground = positive
                ? Resolve("ThemeSuccess", System.Windows.Media.Brushes.Green)
                : Resolve("ThemeWarning", System.Windows.Media.Brushes.Orange);
            ReloadHint.Visibility = Visibility.Visible;
            _reloadHintTimer.Stop();
            _reloadHintTimer.Start();
        }

        // ───────────────────────── 刷新 ─────────────────────────

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            _ = ReloadCurrentAsync(false);
        }

        /// <summary>
        /// 重新从磁盘读一遍。auto = 由文件监听触发，内容没变就什么都不做，
        /// 免得编辑器每次自动保存都闪一下。
        /// </summary>
        private async Task ReloadCurrentAsync(bool auto)
        {
            if (string.IsNullOrEmpty(_currentPath)) return;

            if (!File.Exists(_currentPath))
            {
                StatusRight.Text = "文件已经不在了，保留上次读到的内容";
                ShowReloadHint("文件已不存在", false);
                return;
            }

            // 先记住读到哪儿了，重渲染后要跳回来
            await CaptureScrollAsync();

            if (_currentKind == KindEpub)
            {
                await OpenEpubAsync(_currentPath, false, true);
                ShowReloadHint(auto ? "已自动重新载入" : "已重新载入", true);
                return;
            }

            string text = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    text = TextLoader.ReadAllText(_currentPath);
                    break;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // 别的程序正在写，等一下再试
                    await Task.Delay(180);
                }
                catch (Exception ex)
                {
                    Log.Error("重新读取失败", ex);
                    break;
                }
            }

            if (text == null)
            {
                _restorePending = false;
                StatusRight.Text = "读不出来，可能正被别的程序占用";
                ShowReloadHint("读取失败", false);
                return;
            }

            bool changed = !string.Equals(text, _currentText, StringComparison.Ordinal);
            if (auto && !changed)
            {
                _restorePending = false;
                return;
            }

            _currentText = text;
            Render();
            ShowReloadHint(auto ? "已自动重新载入" : "已重新载入", true);
        }

        // ───────────────────────── EPUB ─────────────────────────

        /// <summary>书的身份：路径 + 大小 + 修改时间。书没被动过就复用上次解压的目录。</summary>
        private static string EpubKey(string path)
        {
            string stamp = string.Empty;
            try
            {
                var info = new FileInfo(path);
                stamp = info.Length.ToString() + "|" + info.LastWriteTimeUtc.Ticks.ToString();
            }
            catch (Exception)
            {
                stamp = DateTime.UtcNow.Ticks.ToString();
            }

            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant() + "|" + stamp));
            var sb = new StringBuilder(16);
            for (int i = 0; i < 8; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }

        private async Task OpenEpubAsync(string full, bool addToRecent, bool keepScroll = false)
        {
            await EnsureWebViewAsync();
            if (_webFailed)
            {
                MessageBox.Show("这台机器上没有 WebView2 运行时，EPUB 的排版显示不了。\n" +
                    "可以点下面的按钮去装一个，装完重启本程序。", "打不开 EPUB");
                return;
            }

            if (!keepScroll) await CaptureScrollAsync();

            ShowBusy("正在解析 EPUB…");
            string host = "epub" + (_docSeq + 1) + ".local";
            string dir = Path.Combine(App.EpubCacheDir, EpubKey(full));

            try
            {
                var document = await Task.Run(() =>
                    EpubReader.Open(full, dir, "https://" + host, keepScroll));

                _epub = document;
                _epubHost = host;
                _currentKind = KindEpub;
                _currentPath = full;
                _currentText = string.Empty;
                App.Settings.LastDir = Path.GetDirectoryName(full) ?? string.Empty;

                View.CoreWebView2.SetVirtualHostNameToFolderMapping(host, document.RootDir,
                    CoreWebView2HostResourceAccessKind.DenyCors);

                string title = string.IsNullOrWhiteSpace(document.Title)
                    ? Path.GetFileName(full)
                    : document.Title;
                Title = title + " - Markdown 阅读器";

                WelcomePanel.Visibility = Visibility.Collapsed;
                ApplyTocVisibility();
                RenderEpubPage();

                if (addToRecent)
                {
                    AddRecent(full);
                    PushHistory(full);
                }

                if (document.SkippedChapters > 0)
                    StatusRight.Text = "有 " + document.SkippedChapters + " 章读不出来，已跳过";

                StartWatching();
            }
            catch (FriendlyException ex)
            {
                Log.Error("EPUB 解析失败：" + full, ex);
                MessageBox.Show(ex.Message, "打不开这本 EPUB");
                if (string.IsNullOrEmpty(_currentPath)) ShowWelcome();
            }
            catch (Exception ex)
            {
                Log.Error("EPUB 解析失败：" + full, ex);
                MessageBox.Show("这本 EPUB 打不开：\n" + ex.Message, "出错了");
                if (string.IsNullOrEmpty(_currentPath)) ShowWelcome();
            }
            finally
            {
                HideBusy();
            }
        }

        private void ShowBusy(string text)
        {
            BusyText.Text = text;
            BusyPanel.Visibility = Visibility.Visible;
        }

        private void HideBusy()
        {
            BusyPanel.Visibility = Visibility.Collapsed;
        }

        // ───────────────────────── 设置 / 更新 / 关于 ─────────────────────────

        /// <summary>设置窗口里改了东西，立刻把主窗口和页面同步过去（不等关窗口）。</summary>
        public void ApplySettingsPreview()
        {
            UpdateChrome();
            if (!_webReady) return;

            PostThemeToPage();
            PostToPage("fontSize", App.Settings.FontSize.ToString());
            PostFontsToPage();
            PostWidthToPage();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            ShowSettings(null);
        }

        private void UpdateBadge_Click(object sender, RoutedEventArgs e)
        {
            ShowSettings(SettingsWindow.SectionUpdate);
        }

        private void ShowSettings(string section)
        {
            // 比 id 而不是明暗：同明暗下换主题（github-light → sepia）也得重新推给页面
            string beforeThemeId = App.EffectiveMarkdownTheme.Id;
            int beforeFont = App.Settings.FontSize;
            double beforeWidth = App.Settings.MaxContentWidth;
            bool beforeWide = App.Settings.WideMode;
            bool beforeWatch = App.Settings.AutoReload;

            var dialog = new SettingsWindow { Owner = this };
            if (!string.IsNullOrEmpty(section)) dialog.RequestSection(section);
            dialog.ShowDialog();

            UpdateChrome();

            bool themeChanged = !string.Equals(beforeThemeId, App.EffectiveMarkdownTheme.Id, StringComparison.Ordinal);
            bool layoutChanged = beforeFont != App.Settings.FontSize ||
                                 beforeWide != App.Settings.WideMode ||
                                 Math.Abs(beforeWidth - App.Settings.MaxContentWidth) > 0.5;

            if (themeChanged)
            {
                ApplyViewBackground();
                PostThemeToPage();
            }

            if (layoutChanged && _webReady)
            {
                PostToPage("fontSize", App.Settings.FontSize.ToString());
                PostWidthToPage();
            }

            if (beforeWatch != App.Settings.AutoReload)
            {
                if (App.Settings.AutoReload) StartWatching();
                else StopWatching();
            }

            if (dialog.NeedsRerender && _webReady)
            {
                _restorePending = App.Settings.RememberScroll;
                Render();
            }
        }

        public void ShowUpdateBadge(UpdateResult result)
        {
            if (result == null || !result.CanDownload)
            {
                UpdateBadge.Visibility = Visibility.Collapsed;
                return;
            }
            UpdateBadgeText.Text = "新版本 " + result.Latest;
            UpdateBadge.ToolTip = "有新版本 " + result.Latest + "，点这里查看";
            UpdateBadge.Visibility = Visibility.Visible;
        }

        public void HideUpdateBadge()
        {
            UpdateBadge.Visibility = Visibility.Collapsed;
        }

        private void ShowAbout()
        {
            string version = App.VersionText;
            MessageBox.Show(
                "Markdown 阅读器 " + version + "\n"
                + ".md / .epub 本地阅读，纯离线\n\n"
                + "数据目录：" + App.DataDir + "\n"
                + "日志：" + Log.FilePath + "\n\n"
                + "按 F5 重新读取，设置里可以开启自动刷新。",
                "关于",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
