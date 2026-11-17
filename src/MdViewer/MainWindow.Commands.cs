using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MdViewer.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace MdViewer
{
    public partial class MainWindow
    {
        // ───────────────────────── 工具栏：打开 / 样例 ─────────────────────────

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "打开文档",
                Filter = "Markdown 与 EPUB|*.md;*.markdown;*.mdown;*.epub|"
                         + "Markdown 文件|*.md;*.markdown;*.mdown|"
                         + "EPUB 电子书|*.epub|"
                         + "文本文件|*.txt|所有文件|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (!string.IsNullOrWhiteSpace(App.Settings.LastDir) && Directory.Exists(App.Settings.LastDir))
                dialog.InitialDirectory = App.Settings.LastDir;

            if (dialog.ShowDialog(this) == true) OpenFile(dialog.FileName, true);
        }

        private void Demo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenFile(DemoDoc.Ensure(App.DataDir), true);
            }
            catch (Exception ex)
            {
                Log.Error("生成样例失败", ex);
                MessageBox.Show("生成样例失败：" + ex.Message, "出错了");
            }
        }

        // ───────────────────────── 目录 / 字号 / 主题 ─────────────────────────

        private void TocToggle_Click(object sender, RoutedEventArgs e)
        {
            UpdateChrome();
        }

        /// <summary>
        /// 宽屏开关：正文栏放弃宽度上限，铺满整个窗口。
        /// 只往页面推一条消息，不重渲染整页 —— 阅读位置不会跳，切来切去是瞬时的。
        /// 顺手把目录收起（退出时还原）：宽屏本来就是为了「一屏看下宽表格」，
        /// 左边还占 240px 就没意义了。
        /// </summary>
        private void WideToggle_Click(object sender, RoutedEventArgs e)
        {
            SetWideMode(WideToggle.IsChecked == true);
        }

        private void SetWideMode(bool on)
        {
            if (on)
            {
                if (TocToggle.IsChecked == true)
                {
                    _tocBeforeWide = true;
                    TocToggle.IsChecked = false;
                }
            }
            else if (_tocBeforeWide)
            {
                TocToggle.IsChecked = true;
                _tocBeforeWide = false;
            }

            App.Settings.WideMode = on;
            App.SaveSettings();
            UpdateChrome();
            PostWidthToPage();

            if (string.IsNullOrEmpty(_currentPath)) return;
            StatusRight.Text = on
                ? "宽屏：正文铺满窗口、目录已收起，宽表格改成左右滚动"
                : "正文宽度回到 " + ((int)App.Settings.MaxContentWidth).ToString() + " px";
        }

        private void FontSmaller_Click(object sender, RoutedEventArgs e)
        {
            ChangeFontSize(-1);
        }

        private void FontBigger_Click(object sender, RoutedEventArgs e)
        {
            ChangeFontSize(1);
        }

        private void ChangeFontSize(int delta)
        {
            // Ctrl+0 传的是 16 - 当前字号：本来就是 16 时 delta 为 0，
            // 那是「恢复默认」，不是「撞到边界」，别弹权限提示。
            if (delta == 0) return;

            int size = App.Settings.FontSize + delta;
            if (size < 14) size = 14;
            if (size > 22) size = 22;
            if (size == App.Settings.FontSize)
            {
                StatusRight.Text = "字号已经在边界上（14~22）";
                return;
            }

            App.Settings.FontSize = size;
            FontLabel.Text = size.ToString();
            App.SaveSettings();
            PostToPage("fontSize", size.ToString());
        }

        private void Theme_Click(object sender, RoutedEventArgs e)
        {
            // Ctrl+Shift+D：在同一套主题的浅 / 深版本之间切（github-light ↔ github-dark）
            bool wantDark = !IsDark;
            ApplyThemeChoice(MarkdownThemes.FamilyFallback(App.EffectiveMarkdownTheme.Id, wantDark));
        }

        private void ThemeMenu_Click(object sender, RoutedEventArgs e)
        {
            RefreshThemeMenu();
            ThemePopup.IsOpen = !ThemePopup.IsOpen;
        }

        private void ThemeRow_Click(object sender, MouseButtonEventArgs e)
        {
            var element = sender as FrameworkElement;
            var row = element == null ? null : element.DataContext as ThemeRow;
            if (row == null) return;

            ThemePopup.IsOpen = false;
            ApplyThemeChoice(row.Id);
        }

        /// <summary>
        /// 换主题：写配置 → 刷界面 → 推给页面（只换 CSS 变量，不重渲染）。
        ///
        /// 「跟随系统」的处理：只有明暗真的变了才改成固定浅 / 深；同一明暗下换风格，
        /// 仍然占用它那一组的槽位，跟随系统照旧生效。
        /// </summary>
        private void ApplyThemeChoice(string themeId)
        {
            MarkdownTheme theme = MarkdownThemes.Resolve(themeId);
            if (theme == null) return;

            if (theme.IsDark) App.Settings.DarkThemeId = theme.Id;
            else App.Settings.LightThemeId = theme.Id;

            if (theme.IsDark != App.IsDarkTheme)
                App.Settings.Theme = theme.IsDark ? "dark" : "light";

            App.ApplyTheme(Theme.ForMarkdownTheme(theme));
            App.SaveSettings();
            UpdateChrome();
            ApplyViewBackground();

            if (_webReady) PostThemeToPage();
            else if (!string.IsNullOrEmpty(_currentPath)) Render();
        }

        // ───────────────────────── 搜索 ─────────────────────────

        private void ShowFindBar()
        {
            FindBar.Visibility = Visibility.Visible;
            FindBox.Focus();
            FindBox.SelectAll();
        }

        private void HideFindBar()
        {
            FindBar.Visibility = Visibility.Collapsed;
            _findTimer.Stop();
            CancelFind();
            _suppressFind = true;          // 清空输入框会触发 TextChanged，这里不要再搜一遍
            FindBox.Text = string.Empty;
            _suppressFind = false;
            View.Focus();
        }

        /// <summary>停掉查找并把计数清零。</summary>
        private void CancelFind()
        {
            if (!_webReady) return;
            try { View.CoreWebView2.Find.Stop(); } catch (Exception ex) { Log.Error("结束查找失败", ex); }
            FindLabel.Text = "0/0";
        }

        private void FindBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressFind) return;

            // 每敲一个字就 StartAsync，中文输入法打拼音的过程会把页面重排十几次。
            // 停手 160ms 再搜，观感和原来一样，CPU 省一半。
            _findTimer.Stop();
            if (string.IsNullOrEmpty(FindBox.Text))
            {
                CancelFind();
                return;
            }
            _findTimer.Start();
        }

        /// <summary>搜索框里回车 = 下一个，Shift+回车 = 上一个，Esc = 关掉搜索条。</summary>
        private void FindBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) FindPrev_Click(sender, new RoutedEventArgs());
                else FindNext_Click(sender, new RoutedEventArgs());
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                HideFindBar();
            }
        }

        private async void StartFind()
        {
            if (!_webReady) return;

            string term = FindBox.Text ?? string.Empty;
            var find = View.CoreWebView2.Find;

            if (term.Length == 0)
            {
                CancelFind();
                return;
            }

            try
            {
                var options = View.CoreWebView2.Environment.CreateFindOptions();
                options.FindTerm = term;
                options.ShouldHighlightAllMatches = true;
                options.IsCaseSensitive = false;
                options.SuppressDefaultFindDialog = true;
                await find.StartAsync(options);
            }
            catch (Exception ex)
            {
                Log.Error("查找失败", ex);
            }
            UpdateFindLabel();
        }

        private void UpdateFindLabel()
        {
            if (!_webReady) return;
            try
            {
                var find = View.CoreWebView2.Find;
                int total = find.MatchCount;
                int index = find.ActiveMatchIndex;
                if (total <= 0) FindLabel.Text = "0/0";
                else
                {
                    // ActiveMatchIndex 是 0 基，显示给用户要 +1；还没定位到第一个就先显示 0
                    int human = index < 0 ? 0 : index + 1;
                    FindLabel.Text = human + "/" + total;
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取查找计数失败", ex);
            }
        }

        private void FindNext_Click(object sender, RoutedEventArgs e)
        {
            if (!_webReady) return;
            try { View.CoreWebView2.Find.FindNext(); UpdateFindLabel(); }
            catch (Exception ex) { Log.Error("下一个失败", ex); }
        }

        private void FindPrev_Click(object sender, RoutedEventArgs e)
        {
            if (!_webReady) return;
            try { View.CoreWebView2.Find.FindPrevious(); UpdateFindLabel(); }
            catch (Exception ex) { Log.Error("上一个失败", ex); }
        }

        private void FindClose_Click(object sender, RoutedEventArgs e)
        {
            HideFindBar();
        }

        // ───────────────────────── 导出 ─────────────────────────

        private async void ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            if (!_webReady)
            {
                MessageBox.Show("页面还没准备好（或者 WebView2 运行时缺失），暂时导不出 PDF。", "导出 PDF");
                return;
            }
            if (string.IsNullOrEmpty(_currentPath)) return;

            var dialog = new SaveFileDialog
            {
                Title = "导出 PDF",
                Filter = "PDF 文件|*.pdf",
                FileName = Path.GetFileNameWithoutExtension(_currentPath) + ".pdf",
                InitialDirectory = Directory.Exists(App.Settings.LastDir) ? App.Settings.LastDir : null
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                // PrintToPdfAsync 不会等 <img>：长文档 / 大图直接打会缺图
                await WaitForImagesAsync();

                var settings = View.CoreWebView2.Environment.CreatePrintSettings();

                // A4：宽 8.27 × 高 11.69 英寸；边距 12mm ≈ 0.472 英寸。
                // 宽屏模式下宽表本来就靠横向铺开，纸上也得横着 —— 否则 30 列又被挤成竖条。
                // 只改 Orientation，宽高一律按竖版 A4 写：WebView2 的 Landscape 自己会把宽高对调，
                // 再手写成 11.69×8.27 就被换两次又回到竖版（实测 MediaBox 594.96×841.92）。
                bool landscape = App.Settings.WideMode;
                settings.Orientation = landscape
                    ? CoreWebView2PrintOrientation.Landscape
                    : CoreWebView2PrintOrientation.Portrait;
                settings.PageWidth = 8.27;
                settings.PageHeight = 11.69;
                settings.MarginTop = 0.472;
                settings.MarginBottom = 0.472;
                settings.MarginLeft = 0.472;
                settings.MarginRight = 0.472;
                settings.ShouldPrintBackgrounds = true;
                settings.ShouldPrintHeaderAndFooter = false;

                bool ok = await View.CoreWebView2.PrintToPdfAsync(dialog.FileName, settings);
                if (ok)
                {
                    StatusRight.Text = "已导出 PDF" + (landscape ? "（A4 横向）" : string.Empty)
                                       + "：" + Path.GetFileName(dialog.FileName);
                    ShowToast("已导出 PDF" + (landscape ? "（A4 横向）" : "（A4 竖版）"),
                        Path.GetFileName(dialog.FileName), dialog.FileName);
                }
                else
                {
                    MessageBox.Show("导出 PDF 没成功，打印机/环境可能有限制。", "导出 PDF");
                }
            }
            catch (Exception ex)
            {
                Log.Error("导出 PDF 失败", ex);
                MessageBox.Show("导出 PDF 失败：" + ex.Message, "出错了");
            }
        }

        /// <summary>
        /// 等页面里的图片加载完（最多 4 秒）。导出 PDF 前调用 —— 浏览器打印不会替我们等，
        /// 图还没下来的位置会被打成空白。
        /// </summary>
        private async Task WaitForImagesAsync()
        {
            if (!_webReady) return;
            try
            {
                await View.CoreWebView2.ExecuteScriptAsync(
                    "(function () {"
                    + "var pending = [].slice.call(document.images).filter(function (i) { return !i.complete; });"
                    + "if (pending.length === 0) return Promise.resolve('ok');"
                    + "var loaded = Promise.all(pending.map(function (i) {"
                    + "  return new Promise(function (resolve) {"
                    + "    i.addEventListener('load', resolve);"
                    + "    i.addEventListener('error', resolve);"
                    + "  });"
                    + "}));"
                    + "var timeout = new Promise(function (resolve) { setTimeout(function () { resolve('timeout'); }, 4000); });"
                    + "return Promise.race([loaded.then(function () { return 'ok'; }), timeout]);"
                    + "})()");
            }
            catch (Exception ex)
            {
                Log.Error("等待图片加载失败", ex);
            }
        }

        private void ExportHtml_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentPath)) return;

            var dialog = new SaveFileDialog
            {
                Title = "导出 HTML",
                Filter = "HTML 文件|*.html",
                FileName = Path.GetFileNameWithoutExtension(_currentPath) + ".html",
                InitialDirectory = Directory.Exists(App.Settings.LastDir) ? App.Settings.LastDir : null
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                RenderedMarkdown doc;
                string note;

                if (_currentKind == KindEpub && _epub != null)
                {
                    string body = _epub.BodyHtml;
                    if (!string.IsNullOrEmpty(_epubHost))
                    {
                        string origin = "https://" + _epubHost + "/";
                        string local = "file:///" + (_epub.RootDir ?? string.Empty).Replace('\\', '/').TrimEnd('/') + "/";
                        body = body.Replace(origin, local);
                    }
                    doc = new RenderedMarkdown { BodyHtml = body, Toc = _epub.Toc };
                    note = "EPUB 里的图片指向本机缓存目录，只在这台机器上能看。";
                }
                else
                {
                    doc = MarkdownRenderer.Render(_currentText);
                    doc.BodyHtml = ImageRewriter.NormalizeRelative(doc.BodyHtml);
                    note = "图片仍是相对路径，记得把 images 之类的一起拷走。";
                }

                string page = MarkdownRenderer.BuildPage(
                    doc,
                    new PageOptions
                    {
                        Title = Path.GetFileNameWithoutExtension(_currentPath),
                        ThemeId = App.EffectiveMarkdownTheme.Id,
                        FontSize = App.Settings.FontSize,
                        // 跟界面上看到的一致：宽屏导出铺满浏览器，普通模式导出还是 880 的阅读栏。
                        // 以前这里写死 MaxContentWidth，宽屏下导出的 html 又被锁回中间那条窄缝。
                        ContentWidth = ContentWidthForHost,
                        ContentOrigin = null,
                        BodyClass = App.Settings.WideMode ? "wide" : null,
                        ForExport = true
                    });

                File.WriteAllText(dialog.FileName, page, new UTF8Encoding(true));
                StatusRight.Text = "已导出 HTML：" + Path.GetFileName(dialog.FileName);
                ShowToast("已导出 HTML",
                    (App.Settings.WideMode ? "宽屏模式：正文铺满浏览器窗口。" : string.Empty) + note,
                    dialog.FileName);
            }
            catch (Exception ex)
            {
                Log.Error("导出 HTML 失败", ex);
                MessageBox.Show("导出 HTML 失败：" + ex.Message, "出错了");
            }
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
                UpdateChrome();

                string extra = string.Empty;
                if (plan.SkippedDefaults.Count > 0)
                {
                    extra = "\n\n注意：下面这些扩展名目前被别的程序占着，我没有去抢，只登记成「可选用本程序打开」：\n· "
                        + string.Join("\n· ", plan.SkippedDefaults);
                }

                MessageBox.Show("已注册到当前用户（HKCU），不需要管理员。\n\n" + FileAssociation.UserHint + extra,
                    "关联 .md 文件");
            }
            catch (Exception ex)
            {
                Log.Error("写注册表失败", ex);
                MessageBox.Show("关联失败：" + ex.Message, "出错了");
            }
        }

        private void Unassoc_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("只删除本程序自己写的注册表键（MdViewer.md 与三个扩展名指向），" +
                "不动其他程序的 ProgId。继续？", "取消关联", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                FileAssoc.Unregister();
                UpdateChrome();
                MessageBox.Show("已经取消关联。下次双击 .md 会回到系统默认程序。", "取消关联");
            }
            catch (Exception ex)
            {
                Log.Error("取消关联失败", ex);
                MessageBox.Show("取消关联失败：" + ex.Message, "出错了");
            }
        }

        // ───────────────────────── 拖放 / 快捷键 / 外部打开 ─────────────────────────

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;

            foreach (var file in files)
            {
                if (IsReadableDocument(file) && File.Exists(file))
                {
                    OpenFile(file, true);
                    return;
                }
            }
            OpenFile(files[0], true);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;

            if (ctrl && e.Key == Key.O) { Open_Click(sender, new RoutedEventArgs()); e.Handled = true; return; }
            if (ctrl && e.Key == Key.F) { ShowFindBar(); e.Handled = true; return; }
            if (ctrl && e.Key == Key.P) { ExportPdf_Click(sender, new RoutedEventArgs()); e.Handled = true; return; }
            if (ctrl && e.Key == Key.E) { Recent_Click(sender, new RoutedEventArgs()); e.Handled = true; return; }
            if (ctrl && e.Key == Key.OemComma) { Settings_Click(sender, new RoutedEventArgs()); e.Handled = true; return; }
            if (ctrl && e.Key == Key.T)
            {
                TocToggle.IsChecked = TocToggle.IsChecked != true;
                UpdateChrome();
                e.Handled = true;
                return;
            }
            if (ctrl && shift && e.Key == Key.D) { Theme_Click(sender, new RoutedEventArgs()); e.Handled = true; return; }
            if (ctrl && shift && e.Key == Key.W) { SetWideMode(!App.Settings.WideMode); e.Handled = true; return; }
            if (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Add)) { ChangeFontSize(1); e.Handled = true; return; }
            if (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract)) { ChangeFontSize(-1); e.Handled = true; return; }
            if (ctrl && e.Key == Key.D0) { ChangeFontSize(16 - App.Settings.FontSize); e.Handled = true; return; }

            if (alt && e.Key == Key.Left) { Back_Click(sender, new RoutedEventArgs()); e.Handled = true; return; }
            if (alt && e.Key == Key.Right) { Forward_Click(sender, new RoutedEventArgs()); e.Handled = true; return; }

            if (e.Key == Key.Escape && FindBar.Visibility == Visibility.Visible)
            {
                HideFindBar();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F5 && !string.IsNullOrEmpty(_currentPath))
            {
                _ = ReloadCurrentAsync(false);
                e.Handled = true;
            }
        }

        private void DownloadWebView_Click(object sender, RoutedEventArgs e)
        {
            // 用户主动点了这个按钮才联网
            OpenExternal("https://developer.microsoft.com/microsoft-edge/webview2/");
        }

        /// <summary>用 shell 打开文件或网址（不拼命令行）。</summary>
        private void OpenExternal(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return;
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("打开失败：" + target, ex);
                MessageBox.Show("打不开：" + target + "\n\n" + ex.Message, "出错了");
            }
        }
    }
}
