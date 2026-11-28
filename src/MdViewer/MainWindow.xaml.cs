using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MdViewer.Core;
using Microsoft.Web.WebView2.Core;

namespace MdViewer
{
    /// <summary>目录侧栏的一行（ListBox 数据源）。</summary>
    public sealed class TocRow
    {
        public string Text { get; set; }
        public string Id { get; set; }
        public double FontSize { get; set; }
        public Thickness Indent { get; set; }
    }

    /// <summary>主题面板里的一行。</summary>
    public sealed class ThemeRow
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Note { get; set; }
        public Brush SwatchBg { get; set; }
        public Brush SwatchFg { get; set; }
        public Visibility CheckVisibility { get; set; }
    }

    public partial class MainWindow : Window
    {
        /// <summary>渲染出来的 html 放在这个虚拟域名下（映射到 %APPDATA%\MdViewer\cache）。</summary>
        private const string CacheHost = "mdviewer.tmp";

        private const string KindMarkdown = "markdown";
        private const string KindEpub = "epub";

        /// <summary>主题按钮的两个图标：月亮 = 现在是浅色（点一下变深色），太阳反之。</summary>
        private const string MoonIcon = "M13.2,9.4 A5.4,5.4 0 1 1 6.6,2.8 A4.4,4.4 0 0 0 13.2,9.4 Z";

        private const string SunIcon =
            "M8,5.6 A2.4,2.4 0 1 0 8,10.4 A2.4,2.4 0 1 0 8,5.6 Z "
            + "M8,1.6 L8,3 M8,13 L8,14.4 M1.6,8 L3,8 M13,8 L14.4,8 "
            + "M3.5,3.5 L4.5,4.5 M11.5,11.5 L12.5,12.5 M12.5,3.5 L11.5,4.5 M4.5,11.5 L3.5,12.5";

        /// <summary>
        /// 传给 WebView2 的启动参数：只保留"渲染静态 HTML"需要的东西，把联网、后台更新、
        /// 崩溃上报、智能提示这类常驻组件全关掉，少开进程、少占内存。
        /// 没有加 --disable-gpu：那会退化成软件渲染，滚动发涩，省内存不值。
        /// </summary>
        private const string BrowserArgs =
            "--renderer-process-limit=1 "
            + "--disable-background-networking "
            + "--disable-component-update "
            + "--disable-sync "
            + "--disable-default-apps "
            + "--disable-extensions "
            + "--disable-breakpad "
            + "--disable-domain-reliability "
            + "--disable-client-side-phishing-detection "
            + "--no-first-run "
            + "--no-default-browser-check "
            + "--disable-features=TranslateUI,OptimizationHints,MediaRouter "
            + "--js-flags=--max-old-space-size=128";

        private readonly string _initialPath;
        private string _currentPath;
        private string _currentText = string.Empty;
        private string _currentKind = KindMarkdown;

        private RenderedMarkdown _doc;
        private EpubDocument _epub;
        private string _epubHost;

        private int _docSeq;
        private string _docHost;

        private bool _webReady;
        private bool _webFailed;
        private Task _webInit;

        private bool _suppressToc;
        private bool _suppressFind;
        private bool _suppressRecentFilter;
        private List<TocRow> _tocRows = new List<TocRow>();

        /// <summary>目录里当前真正显示出来的那一段（过滤后）。高亮要按这个算下标。</summary>
        private List<TocRow> _tocView;

        /// <summary>打开文件的序号：连着点两个文件时，先发起的那次回来要作废。</summary>
        private int _openSeq;

        /// <summary>渲染新页面后要不要把阅读位置还原回去（刷新、切主题、重开都用）。</summary>
        private bool _restorePending;
        private double _restoreY;
        private double _restoreRatio;

        /// <summary>进宽屏之前目录是不是开着的：退出宽屏时按这个还原，不动设置里的 TocVisible。</summary>
        private bool _tocBeforeWide;

        /// <summary>浏览历史：Alt+← / Alt+→。</summary>
        private readonly List<string> _history = new List<string>();
        private int _historyIndex = -1;

        private readonly System.Windows.Threading.DispatcherTimer _reloadTimer;
        private FileSystemWatcher _watcher;
        private readonly System.Windows.Threading.DispatcherTimer _reloadHintTimer;

        /// <summary>搜索防抖：中文输入法一个字一个字进来时不要每次都重排。</summary>
        private readonly System.Windows.Threading.DispatcherTimer _findTimer;

        public MainWindow(string initialPath)
        {
            InitializeComponent();
            _initialPath = initialPath;

            Width = Math.Max(780, App.Settings.WindowWidth);
            Height = Math.Max(460, App.Settings.WindowHeight);
            TocColumn.Width = new GridLength(Math.Max(140, Math.Min(420, App.Settings.TocWidth)));
            TocToggle.IsChecked = App.Settings.TocVisible;
            WideToggle.IsChecked = App.Settings.WideMode;

            // 上次退出时宽屏还开着：启动就保持「铺满 + 目录收起」这个一致的样子
            if (App.Settings.WideMode && TocToggle.IsChecked == true)
            {
                _tocBeforeWide = true;
                TocToggle.IsChecked = false;
            }

            // Ctrl + 滚轮 = 缩放正文字号（不改变页面本身的缩放，避免图片跟着糊）
            View.PreviewMouseWheel += View_PreviewMouseWheel;

            _reloadTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(420)
            };
            _reloadTimer.Tick += async (s, e) =>
            {
                _reloadTimer.Stop();
                await ReloadCurrentAsync(true);
            };

            _reloadHintTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _reloadHintTimer.Tick += (s, e) =>
            {
                _reloadHintTimer.Stop();
                ReloadHint.Visibility = Visibility.Collapsed;
            };

            _findTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(160)
            };
            _findTimer.Tick += (s, e) =>
            {
                _findTimer.Stop();
                StartFind();
            };
        }

        private void View_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;
            ChangeFontSize(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        }

        private bool IsDark
        {
            get { return App.IsDarkTheme; }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            InitFolderPanel();
            RestoreTabsOnStartup();
            SwitchSidebarTab(App.Settings.SidebarTab);
            UpdateChrome();
            UpdateWelcomeRecent();
            StartAutoDimTimer();

            // 启动顺序：命令行给的路径 > 上次看的那篇（会话恢复可在设置里关掉）
            string initial = _initialPath;
            if (string.IsNullOrWhiteSpace(initial) && App.Settings.RestoreLastDocument)
            {
                string last = App.Settings.LastDocument;
                if (!string.IsNullOrWhiteSpace(last) && File.Exists(last)) initial = last;
            }

            // 不在启动时就建 WebView2：它一建就是 7 个进程、三四百 MB。
            // 等真要显示内容时再建（见 OpenFile 里的 EnsureWebView）。
            if (!string.IsNullOrWhiteSpace(initial))
            {
                OpenFile(initial, true);
            }
            else
            {
                ShowWelcome();
            }

            if (!string.IsNullOrWhiteSpace(App.Settings.FolderRoot)) ReloadFolderTree();
            ApplyAutoDim();
        }

        // ───────────────────────── WebView2 ─────────────────────────

        /// <summary>第一次需要渲染时才去创建 WebView2；已经建好就直接返回已完成的任务。</summary>
        private Task EnsureWebViewAsync()
        {
            if (_webReady) return Task.CompletedTask;
            if (_webFailed) return Task.CompletedTask;
            if (_webInit == null) _webInit = InitWebViewAsync();
            return _webInit;
        }

        private async Task InitWebViewAsync()
        {
            try
            {
                View.CreationProperties = new Microsoft.Web.WebView2.Wpf.CoreWebView2CreationProperties
                {
                    UserDataFolder = App.WebViewDataDir,
                    AdditionalBrowserArguments = BrowserArgs
                };
                await View.EnsureCoreWebView2Async();
            }
            catch (Exception ex)
            {
                Log.Error("WebView2 初始化失败", ex);
                _webInit = null;
                ShowFallback("没有检测到 WebView2 运行时：" + ex.Message);
                return;
            }

            var core = View.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = true;

            core.WebMessageReceived += Core_WebMessageReceived;
            core.NavigationStarting += Core_NavigationStarting;
            core.NewWindowRequested += Core_NewWindowRequested;
            core.SetVirtualHostNameToFolderMapping(CacheHost, App.CacheDir,
                CoreWebView2HostResourceAccessKind.DenyCors);

            core.Find.MatchCountChanged += (s, args) => UpdateFindLabel();
            core.Find.ActiveMatchIndexChanged += (s, args) => UpdateFindLabel();

            _webReady = true;
            _webFailed = false;
            FallbackPanel.Visibility = Visibility.Collapsed;

            // 深色主题下，页面加载前 WebView2 的默认底色是白的，会闪一下
            ApplyViewBackground();

            if (!string.IsNullOrWhiteSpace(_currentPath)) Render();
        }

        /// <summary>让浏览器控件的底色跟着主题走，加载/刷新瞬间不再闪白。</summary>
        private void ApplyViewBackground()
        {
            View.DefaultBackgroundColor = IsDark
                ? System.Drawing.Color.FromArgb(255, 0x1E, 0x29, 0x3B)
                : System.Drawing.Color.FromArgb(255, 0xFF, 0xFF, 0xFF);
        }

        private void ShowFallback(string reason)
        {
            _webFailed = true;
            _webReady = false;
            View.Visibility = Visibility.Collapsed;
            FallbackPanel.Visibility = Visibility.Visible;
            FallbackReason.Text = reason;
            WelcomePanel.Visibility = Visibility.Collapsed;
            if (!string.IsNullOrEmpty(_currentText))
                PlainText.Text = MarkdownRenderer.ToPlainText(_currentText);
        }

        private void Core_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using (var json = JsonDocument.Parse(e.WebMessageAsJson))
                {
                    string kind = json.RootElement.TryGetProperty("kind", out var k) ? k.GetString() : null;
                    if (kind == "heading")
                    {
                        string id = json.RootElement.TryGetProperty("payload", out var p) ? p.GetString() : null;
                        HighlightToc(id);
                    }
                    else if (kind == "ready")
                    {
                        // 页面 DOM 好了才还原滚动位置，早了会算错
                        RestoreScrollIfPending();
                    }
                    else if (kind == "annotate")
                    {
                        string payloadJson = json.RootElement.TryGetProperty("payload", out var ap)
                            ? ap.GetRawText()
                            : null;
                        HandleAnnotateRequest(payloadJson);
                    }
                    else if (kind == "speakState")
                    {
                        string state = json.RootElement.TryGetProperty("payload", out var sp)
                            ? sp.GetString()
                            : null;

                        _speakState = state ?? "stopped";
                        UpdateSpeakButton();     // 图标、提示、状态栏那行一起刷

                        if (_speakState == "unsupported")
                        {
                            StatusRight.Text = "这台机器没有可用的朗读语音，装一个中文语音包就行";
                        }
                        else if (_speakState == "stopped")
                        {
                            if (_stopSilent) _stopSilent = false;
                            else StatusRight.Text = "朗读结束";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("处理页面消息失败", ex);
            }
        }

        /// <summary>页面里的链接被点击时走这里：目录内的 md 自己打开，http(s) 交给默认浏览器。</summary>
        private void Core_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            string uri = e.Uri ?? string.Empty;

            if (uri.StartsWith("https://" + CacheHost + "/", StringComparison.OrdinalIgnoreCase)) return;
            if (uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;

            // 当前文档目录映射出来的虚拟域名：可能是图片/文件，也可能是别的 md
            string prefix = string.IsNullOrEmpty(_docHost) ? null : "https://" + _docHost + "/";
            if (prefix != null && uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string relative = uri.Substring(prefix.Length);
                int cut = relative.IndexOfAny(new[] { '?', '#' });
                if (cut >= 0) relative = relative.Substring(0, cut);
                relative = Uri.UnescapeDataString(relative);
                if (relative.Length == 0)
                {
                    e.Cancel = true;   // 只有锚点，页面内已经跳完了
                    return;
                }

                string dir = Path.GetDirectoryName(_currentPath) ?? App.DataDir;
                string target = null;
                try { target = Path.GetFullPath(Path.Combine(dir, relative.Replace('/', '\\'))); }
                catch (Exception ex) { Log.Error("解析链接失败：" + uri, ex); }

                e.Cancel = true;
                if (target != null && File.Exists(target))
                {
                    if (IsReadableDocument(target)) OpenFile(target, true);
                    else OpenExternal(target);
                }
                else
                {
                    StatusRight.Text = "链接指向的文件不存在：" + relative;
                }
                return;
            }

            // EPUB 里的图片/字体（跨虚拟域名）也走这里：放行给浏览器加载，不当成导航
            string epubPrefix = string.IsNullOrEmpty(_epubHost) ? null : "https://" + _epubHost + "/";
            if (epubPrefix != null && uri.StartsWith(epubPrefix, StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                return;
            }

            e.Cancel = true;
            if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                OpenExternal(uri);
        }

        private void Core_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            if (!string.IsNullOrWhiteSpace(e.Uri)) OpenExternal(e.Uri);
        }

        // ───────────────────────── 打开与渲染 ─────────────────────────

        private static bool IsMarkdown(string path)
        {
            string ext = (Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();
            return ext == ".md" || ext == ".markdown" || ext == ".mdown" || ext == ".txt";
        }

        /// <summary>本程序能直接打开的文档类型。</summary>
        private static bool IsReadableDocument(string path)
        {
            return IsMarkdown(path) || EpubReader.IsEpubFile(path);
        }

        public void OpenFromOutside(string path)
        {
            Log.Info("外部打开请求：" + path + "；当前文档：" + _currentPath);
            BringToFront();

            if (string.IsNullOrWhiteSpace(path)) return;

            // 双击文件总是从磁盘重新读一遍：文件可能在外部被改过，双击就是要看最新内容。
            // 当前标签会直接重新加载；其它标签会先切过去，再刷新那一页。
            OpenFile(path, true, forceReload: true);
        }

        // 把窗口从最小化 / 别的窗口后面拉到最前面。
        // 单靠 Window.Activate() 在 Windows 上偶尔不生效，所以补一层 Win32 调用。
        private const int SW_RESTORE = 9;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);

        private void BringToFront()
        {
            try
            {
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                if (!IsVisible) Show();

                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, SW_RESTORE);
                    // 先临时置顶再取消，确保窗口越过普通窗口浮到最上层；
                    // 取消后仍是普通窗口，不会一直压在别的程序上面。
                    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                    SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                    BringWindowToTop(hwnd);
                    SetForegroundWindow(hwnd);
                }

                Activate();
                Focus();
            }
            catch (Exception ex) { Log.Error("激活窗口失败", ex); }
        }

        private async void OpenFile(string path, bool addToRecent, bool forceReload = false)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            string full;
            try { full = Path.GetFullPath(path.Trim().Trim('"')); }
            catch (Exception ex)
            {
                Log.Error("路径不合法：" + path, ex);
                MessageBox.Show("这个路径看不懂：" + path, "打不开");
                return;
            }

            if (!File.Exists(full))
            {
                MessageBox.Show("文件不存在：\n" + full, "打不开");
                RemoveRecentSilently(full);
                return;
            }

            // 这个文件已经在别的标签里开着：先切过去。普通打开就停在这里（保留那一页的
            // 阅读位置）；从外部双击进来的要刷新，切过去之后继续往下重读磁盘。
            if (FindTab(full) != null && !NoteStore.SamePath(full, _currentPath))
            {
                await SwitchTabAsync(full);
                if (!forceReload) return;
            }

            await LoadDocumentAsync(full, addToRecent);
        }

        /// <summary>
        /// 真正把文档读进视图。打开文件、切标签、前进后退都走这里。
        /// 标签的登记不在这里做 —— 切标签时不该更新「最近打开」和标签顺序。
        /// </summary>
        private async Task LoadDocumentAsync(string full, bool addToRecent)
        {
            Log.Info("加载文档：" + full);
            // 连着点两个文件时，先发起的那次可能在 await 之后才回来。发个号，回来对不上就作废。
            int seq = ++_openSeq;

            if (EpubReader.IsEpubFile(full))
            {
                await OpenEpubAsync(full, addToRecent);
                if (seq == _openSeq)
                {
                    TouchTab(full, true);
                    RefreshNotesForDocument();
                }
                return;
            }

            string text;
            try
            {
                text = TextLoader.ReadAllText(full);
            }
            catch (Exception ex)
            {
                Log.Error("读文件失败：" + full, ex);
                MessageBox.Show("读不了这个文件：\n" + full + "\n\n" + ex.Message, "打不开");
                return;
            }

            // 换文档前记下上一份读到哪了
            if (!string.IsNullOrEmpty(_currentPath) && !NoteStore.SamePath(_currentPath, full))
                RememberScroll(_currentPath);

            _currentPath = full;
            _currentText = text;
            _currentKind = KindMarkdown;
            _epub = null;
            _epubHost = null;
            App.Settings.LastDir = Path.GetDirectoryName(full) ?? string.Empty;
            App.Settings.LastDocument = full;

            if (addToRecent)
            {
                AddRecent(full);
                PushHistory(full);
            }

            Title = Path.GetFileName(full) + " - Markdown 阅读器";
            ShowDocumentView();
            ApplyTocVisibility();   // 打开了文件，目录栏该出来了

            await EnsureWebViewAsync();
            if (seq != _openSeq) return;   // 这期间又打开了别的文件，这里的结果已经过期
            _restorePending = App.Settings.RememberScroll && TryRecallScroll(full);
            Render();
            StartWatching();

            TouchTab(full, false);
            RefreshNotesForDocument();
        }

        /// <summary>把当前文本重新渲染成页面（打开文件、切主题、改字号、刷新都走这里）。</summary>
        private void Render()
        {
            if (string.IsNullOrEmpty(_currentPath)) return;

            // 页面马上要重载，旧文档的朗读队列不会跟着消失，得先掐掉
            StopSpeak(true);

            if (_currentKind == KindEpub)
            {
                RenderEpubPage();
                return;
            }

            _doc = MarkdownRenderer.Render(_currentText);

            if (!_webReady)
            {
                if (_webFailed && !string.IsNullOrEmpty(_currentText))
                    PlainText.Text = MarkdownRenderer.ToPlainText(_currentText);
                FillToc();
                UpdateStatus();
                return;
            }

            _docSeq++;
            _docHost = "md" + _docSeq + ".local";
            string dir = Path.GetDirectoryName(_currentPath) ?? App.DataDir;

            string body = ImageRewriter.Rewrite(_doc.BodyHtml, dir, _docHost);
            body = ImageRewriter.RewriteLinks(body, dir, _docHost);

            var page = MarkdownRenderer.BuildPage(
                new RenderedMarkdown { BodyHtml = body, Toc = _doc.Toc },
                BuildPageOptions(Path.GetFileName(_currentPath), "https://" + _docHost));

            try
            {
                View.CoreWebView2.SetVirtualHostNameToFolderMapping(_docHost, dir,
                    CoreWebView2HostResourceAccessKind.DenyCors);
                NavigateTo(page);
            }
            catch (Exception ex)
            {
                Log.Error("渲染页面失败", ex);
                StatusRight.Text = "渲染失败：" + ex.Message;
            }

            FillToc();
            UpdateStatus();
            PostAnnotationsToPage();
        }

        /// <summary>
        /// 组一份整页 html 的选项。阅读器和导出共用 —— 差别只在 ContentOrigin / VendorBase：
        /// 导出给别人看的文件不能引用虚拟域里的第三方库（对方机器上没有），所以那两项要清掉。
        /// </summary>
        private PageOptions BuildPageOptions(string title, string contentOrigin, RenderedMarkdown doc = null)
        {
            RenderedMarkdown source = doc ?? _doc;
            bool vendor = App.VendorReady;
            string vendorBase = vendor ? "https://" + CacheHost + "/vendor/" : null;

            return new PageOptions
            {
                Title = title,
                ThemeId = App.EffectiveMarkdownTheme.Id,
                FontSize = App.Settings.FontSize,
                FontBody = App.Settings.FontBody,
                FontCode = App.Settings.FontCode,
                FontHeading = App.Settings.FontHeading,
                ContentWidth = ContentWidthForHost,
                ContentOrigin = contentOrigin,
                BodyClass = App.Settings.WideMode ? "wide" : null,
                IncludeMath = vendor && source != null && source.HasMath,
                IncludeMermaid = vendor && source != null && source.HasMermaid,
                IncludeCodeHighlight = vendor && source != null && source.HasCodeBlock,
                VendorBase = vendorBase,
                CustomCss = CurrentSkinCss()
            };
        }

        /// <summary>EPUB 已经解析过就直接拼页面；没解析过说明是重渲染（切主题/改字号）。</summary>
        private void RenderEpubPage()
        {
            if (_epub == null || !_webReady) return;

            _docSeq++;

            var epubDoc = new RenderedMarkdown { BodyHtml = _epub.BodyHtml, Toc = _epub.Toc };
            var options = BuildPageOptions(
                string.IsNullOrWhiteSpace(_epub.Title)
                    ? Path.GetFileNameWithoutExtension(_currentPath)
                    : _epub.Title,
                "https://" + CacheHost,
                epubDoc);

            options.ExtraOrigins = new List<string> { "https://" + _epubHost };
            options.BodyClass = App.Settings.WideMode ? "epub-body wide" : "epub-body";

            var page = MarkdownRenderer.BuildPage(epubDoc, options);

            try
            {
                NavigateTo(page);
            }
            catch (Exception ex)
            {
                Log.Error("渲染 EPUB 页面失败", ex);
                StatusRight.Text = "渲染失败：" + ex.Message;
            }

            _doc = new RenderedMarkdown { BodyHtml = _epub.BodyHtml, Toc = _epub.Toc };
            FillToc();
            UpdateStatus();
        }

        private void NavigateTo(string page)
        {
            // 序号由 Render 统一往前推，host 与缓存文件名用同一个号，排错时好对
            string fileName = "view-" + _docSeq + ".html";
            AtomicFile.WriteAllText(Path.Combine(App.CacheDir, fileName), page);
            // 一次会话里开着几百个文档也不该把磁盘塞满
            CacheStore.Trim(App.CacheDir, keep: 20);

            // 文件名的序号每次启动都从 1 开始，URL 会和上一次运行完全一样，
            // WebView2 有自己的磁盘缓存，可能直接把上次那篇的 html 拿回来，
            // 表现就是「打开 A 却显示上一次读的那篇」。带个唯一参数绕开缓存。
            // （本地文件，每次重新读的成本可以忽略；vendor 那些静态库不受影响，照旧走缓存）
            string stamp = DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
            View.CoreWebView2.Navigate("https://" + CacheHost + "/" + fileName + "?v=" + stamp);
        }

        private void FillToc()
        {
            var rows = new List<TocRow>();
            if (_doc != null && _doc.Toc != null)
            {
                foreach (var item in _doc.Toc)
                {
                    rows.Add(new TocRow
                    {
                        Text = item.Text,
                        Id = item.Id,
                        FontSize = item.Level == 1 ? 14 : 13,
                        Indent = new Thickness(Math.Min(3, Math.Max(0, item.Level - 1)) * 12, 0, 0, 0)
                    });
                }
            }

            if (rows.Count == 0)
            {
                rows.Add(new TocRow { Text = "本文没有标题", Id = null, FontSize = 13, Indent = new Thickness(0) });
            }

            _tocRows = rows;
            _tocView = rows;
            TocList.ItemsSource = null;
            TocList.IsHitTestVisible = _doc != null && _doc.Toc != null && _doc.Toc.Count > 0;

            // 换了一篇文档，上一轮的过滤条件不能留着 —— 否则目录看着像空的
            if (TocFilterBox != null && !string.IsNullOrEmpty(TocFilterBox.Text))
                TocFilterBox.Text = string.Empty;

            ApplyTocFilter();

            // 只在「大纲」页签上改标题，否则会盖掉文件 / 笔记页签自己的标题
            if (TabToc == null || TabToc.IsChecked == true)
                TocTitle.Text = _currentKind == KindEpub ? "章节" : "目录";

            // 刚打开文档时页面停在顶部，把第一项标成"当前章节"，
            // 否则目录里一片没有焦点，看不出读到哪了
            if (_tocRows.Count > 0)
            {
                _suppressToc = true;
                TocList.SelectedIndex = 0;
                _suppressToc = false;
            }
        }

        private void HighlightToc(string id)
        {
            if (string.IsNullOrEmpty(id)) return;

            var view = _tocView ?? _tocRows;
            for (int i = 0; i < view.Count; i++)
            {
                if (!string.Equals(view[i].Id, id, StringComparison.Ordinal)) continue;
                if (TocList.SelectedIndex == i) return;
                _suppressToc = true;
                TocList.SelectedIndex = i;
                TocList.ScrollIntoView(view[i]);
                _suppressToc = false;
                return;
            }
        }

        private void TocList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressToc) return;
            var row = TocList.SelectedItem as TocRow;
            if (row == null || string.IsNullOrEmpty(row.Id)) return;
            if (!_webReady) return;
            PostToPage("scrollTo", row.Id);
        }

        /// <summary>
        /// 单选 ListBox 里再点一次已选中项不会触发 SelectionChanged，
        /// 但用户就是想「回到当前章节」，这里补一次跳转。
        /// </summary>
        private void TocList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var row = TocList.SelectedItem as TocRow;
            if (row == null || string.IsNullOrEmpty(row.Id) || !_webReady) return;

            var container = ItemsControl.ContainerFromElement(TocList, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (container == null) return;                             // 点在侧栏空白处
            if (!ReferenceEquals(container.DataContext, row)) return;   // 点到别的项，交给 SelectionChanged

            PostToPage("scrollTo", row.Id);
        }

        private void PostToPage(string kind, string payload)
        {
            if (!_webReady) return;
            try
            {
                string json = JsonSerializer.Serialize(new Message { kind = kind, payload = payload });
                View.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                Log.Error("给页面发消息失败", ex);
            }
        }

        /// <summary>
        /// 交给页面的正文栏宽度：0 = 不限制（宽屏模式），其余就是设置里那个 640~1400 的值。
        /// 宽屏不改 MaxContentWidth —— 关掉宽屏还是回到用户原来调的宽度。
        /// </summary>
        private int ContentWidthForHost
        {
            get { return App.Settings.WideMode ? 0 : (int)App.Settings.MaxContentWidth; }
        }

        /// <summary>把正文栏宽度推给页面。宽屏 / 普通都走这里，页面只认一个数（0 = 铺满）。</summary>
        private void PostWidthToPage()
        {
            PostToPage("width", ContentWidthForHost.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// 把当前主题推给页面。只换 html 上的 data-theme，不重新渲染整页，
        /// 所以滚动位置不会跳回顶部，大文档切主题也是瞬间的事。
        /// </summary>
        private void PostThemeToPage()
        {
            if (!_webReady) return;
            try
            {
                MarkdownTheme theme = App.EffectiveMarkdownTheme;
                string json = JsonSerializer.Serialize(new ThemeMessage
                {
                    kind = "theme",
                    payload = new ThemePayload
                    {
                        id = theme.Id,
                        mode = theme.IsDark ? "dark" : "light"
                    }
                });
                View.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                Log.Error("推送主题失败", ex);
            }
        }

        private void PostRestoreToPage()
        {
            if (!_webReady) return;
            try
            {
                var payload = new ScrollPayload
                {
                    y = (int)Math.Round(_restoreY),
                    ratio = _restoreRatio
                };
                // payload 必须是对象：app.js 的 restore 分支直接读 data.y / data.ratio，
                // 走 PostToPage 那种「字符串 payload」会拿到 undefined，结果滚回顶部。
                string json = JsonSerializer.Serialize(new RestoreMessage
                {
                    kind = "restore",
                    payload = payload
                });
                View.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                Log.Error("还原阅读位置失败", ex);
            }
        }

        private sealed class Message
        {
            // 属性名要跟 app.js 里读的字段一致（小写，走 JSON 默认命名）
            public string kind { get; set; }
            public string payload { get; set; }
        }

        /// <summary>主题消息的 payload 是个对象，所以单独一个类型。</summary>
        private sealed class ThemeMessage
        {
            public string kind { get; set; }
            public ThemePayload payload { get; set; }
        }

        private sealed class ThemePayload
        {
            public string id { get; set; }
            public string mode { get; set; }
        }

        private sealed class ScrollPayload
        {
            public int y { get; set; }
            public double ratio { get; set; }
        }

        /// <summary>restore 的 payload 也是对象，跟 app.js 里的读取方式保持一致。</summary>
        private sealed class RestoreMessage
        {
            public string kind { get; set; }
            public ScrollPayload payload { get; set; }
        }

        // ───────────────────────── 阅读位置 ─────────────────────────

        /// <summary>问页面现在滚到哪儿了。ExecuteScriptAsync 返回的是 JSON 字符串的 JSON。</summary>
        private async Task CaptureScrollAsync(bool markForRestore = true)
        {
            if (!_webReady) return;
            try
            {
                string raw = await View.CoreWebView2.ExecuteScriptAsync(
                    "window.__mdviewer ? window.__mdviewer.getScroll() : ''");
                if (string.IsNullOrEmpty(raw) || raw == "\"\"") return;

                string inner = JsonSerializer.Deserialize<string>(raw);
                if (string.IsNullOrWhiteSpace(inner)) return;

                using (var document = JsonDocument.Parse(inner))
                {
                    JsonElement element;
                    if (document.RootElement.TryGetProperty("y", out element)) _restoreY = element.GetDouble();
                    if (document.RootElement.TryGetProperty("ratio", out element)) _restoreRatio = element.GetDouble();
                    if (markForRestore) _restorePending = true;
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取滚动位置失败", ex);
            }
        }

        private void RestoreScrollIfPending()
        {
            if (!_restorePending) return;
            _restorePending = false;
            PostRestoreToPage();
            RememberScrollForCurrent();
        }

        /// <summary>把比例存进 settings，下次打开同一篇文档直接跳回原处。</summary>
        private void RememberScrollForCurrent()
        {
            if (string.IsNullOrEmpty(_currentPath)) return;
            if (App.Settings.ScrollPositions == null) App.Settings.ScrollPositions = new Dictionary<string, double>();
            App.Settings.ScrollPositions[_currentPath] = Math.Round(_restoreRatio, 4);
            if (App.Settings.ScrollPositions.Count > 40)
            {
                // 太多就只留最近的一批（字典无序，按值不重要，直接砍）
                var keys = new List<string>(App.Settings.ScrollPositions.Keys);
                for (int i = 0; i < keys.Count - 40; i++) App.Settings.ScrollPositions.Remove(keys[i]);
            }
            App.SaveSettings();
        }

        /// <summary>离开当前文档前，把阅读位置记下来。</summary>
        private async void RememberScroll(string path)
        {
            if (!App.Settings.RememberScroll) return;
            // 这里只是「存档」，不要顺手把新文档也标记成需要还原
            await CaptureScrollAsync(false);
            _restorePending = false;
            if (string.IsNullOrEmpty(path)) return;
            if (App.Settings.ScrollPositions == null) App.Settings.ScrollPositions = new Dictionary<string, double>();
            App.Settings.ScrollPositions[path] = Math.Round(_restoreRatio, 4);
            App.SaveSettings();
        }

        private bool TryRecallScroll(string path)
        {
            if (!App.Settings.RememberScroll || string.IsNullOrEmpty(path)) return false;
            double ratio;
            if (App.Settings.ScrollPositions == null) return false;
            if (!App.Settings.ScrollPositions.TryGetValue(path, out ratio)) return false;
            if (ratio <= 0.001) return false;
            _restoreY = 0;
            _restoreRatio = ratio;
            return true;
        }

        // ───────────────────────── 欢迎页 / 目录可见性 ─────────────────────────

        /// <summary>从欢迎页切回文档视图。WebView2 要在，才显示它；降级模式下留给纯文本面板。</summary>
        private void ShowDocumentView()
        {
            WelcomePanel.Visibility = Visibility.Collapsed;
            if (_webFailed)
            {
                // 降级模式：没有 WebView2，正文走纯文本面板
                FallbackPanel.Visibility = Visibility.Visible;
            }
            else
            {
                View.Visibility = Visibility.Visible;
            }
        }

        private void ShowWelcome()
        {
            ++_openSeq;   // 作废还在 await 的加载，防止它回头又把旧文档渲染回来
            Log.Info("回到欢迎页（标签已清空）");

            // WebView2 是原生窗口，WPF 的 WelcomePanel 盖不住它——只把面板设为 Visible，
            // 底下渲染过的旧文档会透出来。得真正把 View 隐藏，欢迎页才干净。
            View.Visibility = Visibility.Collapsed;
            FallbackPanel.Visibility = Visibility.Collapsed;
            WelcomePanel.Visibility = Visibility.Visible;
            TocList.ItemsSource = null;
            TocCount.Text = string.Empty;
            StatusPath.Text = "没有打开文件";
            StatusRight.Text = string.Empty;
            Title = "Markdown 阅读器";
            _currentPath = null;
            _currentKind = KindMarkdown;
            _epub = null;
            _epubHost = null;
            StopWatching();
            StopSpeak(true);        // 回到欢迎页就没正文可读了，别让声音继续
            UpdateWelcomeRecent();
            ApplyTocVisibility();   // 没打开文件时左侧不摆一个空目录栏
            UpdateNavButtons();
        }

        /// <summary>
        /// 目录栏该不该出现在界面上：用户勾了「目录」并且确实打开了文件。
        /// 欢迎页上空目录没有任何意义，只会把正文挤到右边。
        /// </summary>
        private void ApplyTocVisibility()
        {
            bool show = TocToggle.IsChecked == true && !string.IsNullOrEmpty(_currentPath);

            // MinWidth 也得跟着归零，否则列宽会被 140 的下限钳住，收起后还留一条灰边
            TocColumn.MinWidth = show ? 140 : 0;
            TocColumn.MaxWidth = show ? 420 : 0;

            TocPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            TocColumn.Width = show ? new GridLength(Math.Max(140, App.Settings.TocWidth)) : new GridLength(0);
        }

        private void UpdateStatus()
        {
            if (string.IsNullOrEmpty(_currentPath)) return;

            int headings = _doc != null && _doc.Toc != null ? _doc.Toc.Count : 0;
            int chars = _currentText == null ? 0 : _currentText.Length;
            StatusPath.Text = _currentPath;

            if (_currentKind == KindEpub)
            {
                string author = _epub != null && !string.IsNullOrWhiteSpace(_epub.Author) ? _epub.Author + " · " : string.Empty;
                StatusRight.Text = string.Format("{0}{1} 章 · {2} 字符",
                    author, _epub == null ? 0 : _epub.ChapterCount, chars);
            }
            else
            {
                StatusRight.Text = string.Format("{0} 个标题 · {1} 字符 · {2} 字",
                    headings, chars, CountChinese(_currentText));
            }
        }

        private static int CountChinese(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int count = 0;
            foreach (char ch in text)
            {
                if (ch >= 0x4E00 && ch <= 0x9FFF) count++;
            }
            return count;
        }

        // ───────────────────────── 外观同步 ─────────────────────────

        /// <summary>主题、字号、目录、关联状态这些界面元素，跟着设置刷新。</summary>
        private void UpdateChrome()
        {
            // 深色状态下按钮图标换成太阳，提示里带上当前主题名
            ThemeIcon.Data = Geometry.Parse(IsDark ? SunIcon : MoonIcon);
            ThemeButton.ToolTip = "主题：" + App.EffectiveMarkdownTheme.Name + "（点开切换，Ctrl+Shift+D 切明暗）";
            FontLabel.Text = App.Settings.FontSize.ToString(CultureInfo.InvariantCulture);
            ApplyViewBackground();

            // 宽屏是个开关，勾上时提示里说清楚「再点一下回到多少 px」
            WideToggle.IsChecked = App.Settings.WideMode;
            WideToggle.ToolTip = App.Settings.WideMode
                ? "宽屏已开：正文铺满窗口、目录已收起。再点一下回到 "
                  + ((int)App.Settings.MaxContentWidth).ToString(CultureInfo.InvariantCulture) + " px (Ctrl+Shift+W)"
                : "宽屏：正文铺满窗口并收起目录，多列表格改成左右滚动 (Ctrl+Shift+W)";

            bool associated = FileAssoc.IsAssociated(Environment.ProcessPath ?? string.Empty);
            AssocLabel.Text = associated ? "已关联 ✓" : "未关联 .md";
            ApplyTocVisibility();
            UpdateNavButtons();
            UpdateWatchHint();
            RefreshThemeMenu();
        }

        /// <summary>重建主题面板：浅色一组、深色一组，当前那套打勾。</summary>
        private void RefreshThemeMenu()
        {
            if (ThemeListLight == null || ThemeListDark == null) return;

            string current = App.EffectiveMarkdownTheme.Id;
            ThemeListLight.ItemsSource = BuildThemeRows(false, current);
            ThemeListDark.ItemsSource = BuildThemeRows(true, current);
        }

        private static List<ThemeRow> BuildThemeRows(bool dark, string currentId)
        {
            var rows = new List<ThemeRow>();
            foreach (MarkdownTheme theme in MarkdownThemes.OfMode(dark))
            {
                rows.Add(new ThemeRow
                {
                    Id = theme.Id,
                    Name = theme.Name,
                    Note = theme.Note,
                    SwatchBg = Swatch(theme.SwatchBg),
                    SwatchFg = Swatch(theme.SwatchFg),
                    CheckVisibility = string.Equals(theme.Id, currentId, StringComparison.OrdinalIgnoreCase)
                        ? Visibility.Visible
                        : Visibility.Collapsed
                });
            }
            return rows;
        }

        private static Brush Swatch(string hex)
        {
            try
            {
                object parsed = ColorConverter.ConvertFromString(hex ?? string.Empty);
                if (parsed is Color color)
                {
                    var brush = new SolidColorBrush(color);
                    brush.Freeze();
                    return brush;
                }
            }
            catch (Exception)
            {
                // 颜色写坏了就画个空的，不能让主题面板把程序拖崩
            }
            return Brushes.Transparent;
        }

        /// <summary>
        /// Windows 切了浅色 / 深色（且用户选的是「跟随系统」）。只换配色，不重渲染。
        /// </summary>
        public void ApplySystemThemeChange()
        {
            App.ApplyTheme(Theme.ForMarkdownTheme(App.EffectiveMarkdownTheme));
            UpdateChrome();
            ApplyViewBackground();
            PostThemeToPage();
        }

        private void UpdateNavButtons()
        {
            BackButton.IsEnabled = _historyIndex > 0;
            ForwardButton.IsEnabled = _historyIndex >= 0 && _historyIndex < _history.Count - 1;
        }

        /// <summary>
        /// 把窗口当前的样子存成 PNG。给 <c>--snapshot</c> 自检用。
        /// 注意：WebView2 是原生子窗口，不走 WPF 渲染管线，正文区域会截成空白；
        /// 正文用 <see cref="SnapshotPageAsync"/> 单独截。
        /// </summary>
        public void SnapshotTo(string path)
        {
            WindowSnapshot.Capture(this, path);
            Log.Info("界面截图已保存：" + path);
        }

        /// <summary>用 WebView2 自己截正文区域（WPF 截不到原生子窗口）。</summary>
        public async Task<bool> SnapshotPageAsync(string path)
        {
            if (!_webReady || View.CoreWebView2 == null) return false;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await View.CoreWebView2.CapturePreviewAsync(
                        CoreWebView2CapturePreviewImageFormat.Png, stream);
                }
                Log.Info("正文截图已保存：" + path);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("截取正文失败", ex);
                return false;
            }
        }

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            App.Settings.WindowWidth = Width;
            App.Settings.WindowHeight = Height;
            // 目录收起时 ActualWidth 是 0，别把用户之前拖好的宽度存成 140
            if (TocPanel.Visibility == Visibility.Visible && TocColumn.ActualWidth > 0)
                App.Settings.TocWidth = Math.Max(140, TocColumn.ActualWidth);
            App.Settings.TocVisible = TocToggle.IsChecked == true;

            if (App.Settings.RememberScroll && !string.IsNullOrEmpty(_currentPath) && _webReady)
            {
                try
                {
                    string raw = await View.CoreWebView2.ExecuteScriptAsync(
                        "window.__mdviewer ? window.__mdviewer.getScroll() : ''");
                    if (!string.IsNullOrEmpty(raw) && raw != "\"\"")
                    {
                        string inner = JsonSerializer.Deserialize<string>(raw);

                        // 页面正在导航时 ExecuteScriptAsync 会返回 null，反序列化出来也是 null，
                        // 直接 Parse 会抛 ArgumentNullException（旧版每次自动刷新中途关窗口都会留一条错日志）。
                        // 注意这里不能 return —— 后面的 StopWatching / SaveSettings 必须照常执行。
                        if (!string.IsNullOrWhiteSpace(inner))
                        {
                            using (var document = JsonDocument.Parse(inner))
                            {
                                JsonElement element;
                                if (document.RootElement.TryGetProperty("ratio", out element))
                                {
                                    if (App.Settings.ScrollPositions == null)
                                        App.Settings.ScrollPositions = new Dictionary<string, double>();
                                    App.Settings.ScrollPositions[_currentPath] = Math.Round(element.GetDouble(), 4);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) { Log.Error("退出前记录阅读位置失败", ex); }
            }

            StopWatching();
            StopSpeak(true);        // 关窗口就得断掉，不然进程退了语音引擎还在读
            App.SaveSettings();
        }
    }
}
