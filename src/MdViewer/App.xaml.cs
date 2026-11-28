using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using MdViewer.Core;

namespace MdViewer
{
    public partial class App : Application
    {
        /// <summary>数据目录：%APPDATA%\MdViewer。配置 / 日志 / WebView2 缓存 / 样例都写这里。</summary>
        public static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MdViewer");

        /// <summary>WebView2 用户数据目录（规范 8.1 指定）。</summary>
        public static string WebViewDataDir
        {
            get { return Path.Combine(DataDir, "WebView2"); }
        }

        /// <summary>渲染出来的整页 html 临时落盘的地方（避免 NavigateToString 的 2MB 上限）。</summary>
        public static string CacheDir
        {
            get { return Path.Combine(DataDir, "cache"); }
        }

        /// <summary>EPUB 解压出来的资源放这里，每本书一个子目录。</summary>
        public static string EpubCacheDir
        {
            get { return Path.Combine(DataDir, "epub"); }
        }

        /// <summary>用户自定义皮肤（*.css）放这里，设置窗口里可以导入。</summary>
        public static string ThemesDir
        {
            get { return Path.Combine(DataDir, "themes"); }
        }

        /// <summary>
        /// 第三方库（KaTeX / Mermaid / highlight.js）释放出来的目录，页面通过虚拟域从这里按需加载。
        /// null = 释放失败，数学公式与图表会降级成源码显示（不影响看文档）。
        /// </summary>
        public static string VendorDir { get; private set; }

        /// <summary>第三方库能不能用（少一个文件就当作不能用）。</summary>
        public static bool VendorReady
        {
            get { return VendorAssets.IsUsable(VendorDir); }
        }

        /// <summary>
        /// 定时换肤算出来的明暗覆盖。null = 不覆盖（按设置里的 light / dark / system 走）。
        ///
        /// 单独一个字段而不是直接改 Settings.Theme：改 Theme 会把「跟随系统」变成固定值，
        /// 用户第二天早上就永远停在浅色了 —— 覆盖只影响「此刻用哪套」，不动用户的选择。
        /// </summary>
        public static bool? DarkOverride { get; set; }

        /// <summary>当前实际生效的正文主题（把「跟随系统」换算成具体一套）。</summary>
        public static MarkdownTheme EffectiveMarkdownTheme
        {
            get
            {
                if (Settings == null) return MarkdownThemes.DefaultLight;

                string mode = (Settings.Theme ?? "system").Trim().ToLowerInvariant();
                bool dark;
                if (DarkOverride.HasValue) dark = DarkOverride.Value;
                else if (string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase)) dark = true;
                else if (string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase)) dark = false;
                else dark = SystemPrefersDark();

                string id = dark ? Settings.DarkThemeId : Settings.LightThemeId;
                return MarkdownThemes.ResolveOrFallback(id, dark);
            }
        }

        /// <summary>实际生效的明暗（"light" / "dark"）。</summary>
        public static string EffectiveThemeName
        {
            get { return EffectiveMarkdownTheme.IsDark ? "dark" : "light"; }
        }

        public static bool IsDarkTheme
        {
            get { return EffectiveMarkdownTheme.IsDark; }
        }

        /// <summary>Windows「应用模式」是不是深色。读不到就当浅色。</summary>
        public static bool SystemPrefersDark()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    if (value is int light) return light == 0;
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取系统主题失败", ex);
            }
            return false;
        }

        /// <summary>显示用的版本号，形如 0.3.0。</summary>
        public static string VersionText
        {
            get
            {
                Version version = typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0);
                return string.Format("{0}.{1}.{2}", version.Major, version.Minor, Math.Max(0, version.Build));
            }
        }

        public static AppSettings Settings { get; private set; } = new AppSettings();

        private static MutexHolder _instance;

        /// <summary>本进程是否拥有主窗口。第二个实例只做转发，退出时绝对不能回写 settings.json。</summary>
        private bool _isOwner;

        public App()
        {
            // 界面线程上任何没接住的异常：写日志，别让程序默默消失
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        }

        private void OnDispatcherUnhandledException(object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error("未处理异常", e.Exception);
            WriteCliReport("程序出错：" + e.Exception);
            e.Handled = true;
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log.Error("后台线程未处理异常", e.ExceptionObject as Exception);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                Directory.CreateDirectory(DataDir);
                Directory.CreateDirectory(CacheDir);
                Directory.CreateDirectory(WebViewDataDir);
                Directory.CreateDirectory(EpubCacheDir);
                Directory.CreateDirectory(ThemesDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法创建数据目录：" + DataDir + "\n" + ex.Message, "启动失败");
                Shutdown(1);
                return;
            }

            Log.Init(DataDir);
            AtomicFile.ErrorHandler = (where, error) => Log.Error(where, error);
            CleanupOldExecutables();

            // 渲染缓存只留最近 20 个，别让 %APPDATA% 无限长
            try { CacheStore.Trim(CacheDir); } catch (Exception ex) { Log.Error("清理渲染缓存失败", ex); }

            // 第三方库：首次运行从 exe 里释放出来（3.2MB），之后就走缓存。
            // 失败不致命 —— 页面会退化成「公式显示原始 TeX、图表显示代码块」。
            try
            {
                VendorDir = VendorAssets.Ensure(CacheDir);
                if (!VendorAssets.IsUsable(VendorDir))
                    Log.Error("第三方渲染库释放不完整，数学公式与图表将降级显示", null);
            }
            catch (Exception ex)
            {
                VendorDir = null;
                Log.Error("释放第三方渲染库失败", ex);
            }

            Settings = SettingsStore.Load(DataDir);
            if (Settings.RecentFiles == null) Settings.RecentFiles = new System.Collections.Generic.List<string>();
            if (Settings.RecentEntries == null) Settings.RecentEntries = new System.Collections.Generic.List<RecentEntry>();
            if (Settings.ScrollPositions == null) Settings.ScrollPositions = new System.Collections.Generic.Dictionary<string, double>();
            SettingsStore.Normalize(Settings);
            ApplyTheme(Theme.ForMarkdownTheme(EffectiveMarkdownTheme));
            HookSystemThemeChange();

            try
            {
                if (!File.Exists(SettingsStore.PathIn(DataDir))) SettingsStore.Save(DataDir, Settings);
            }
            catch (Exception ex) { Log.Error("写入默认设置失败", ex); }

            CleanCache();

            CommandLineOptions options = CommandLine.Parse(e.Args);

            if (options.Help)
            {
                // 命令行模式下不弹窗，否则在脚本里调用会一直等人点确定
                WriteCliReport(
                    "用法：\n  MdViewer.exe \"C:\\路径\\文档.md\"\n  MdViewer.exe --demo      生成样例文档并打开\n" +
                    "  MdViewer.exe --help      显示这段说明\n" +
                    "  MdViewer.exe --snapshot <图片路径> [文件]  把界面存成 PNG（自检用）\n\n" +
                    "也支持把一个或多个 .md 文件拖到 exe 或窗口上。");
                Shutdown(0);
                return;
            }

            // 自检模式：渲染窗口 → 存图 → 退出。不走单实例，免得被转发到已在运行的窗口里。
            if (options.IsSnapshot)
            {
                RunSnapshot(options);
                return;
            }

            // 安装脚本用：给自己建快捷方式（PowerShell 那边调 COM 在 UTF-8 代码页机器上不可靠）
            if (options.IsMakeShortcut)
            {
                RunMakeShortcut(options);
                return;
            }

            string initialPath = options.FilePath;

            if (options.Demo)
            {
                try
                {
                    initialPath = DemoDoc.Ensure(DataDir);
                    Log.Info("已生成样例：" + initialPath);
                }
                catch (Exception ex)
                {
                    Log.Error("生成样例失败", ex);
                    MessageBox.Show("生成样例失败：" + ex.Message, "出错了");
                    Shutdown(1);
                    return;
                }
            }

            // 单实例：第二个进程把路径转给已有窗口后自己退出
            _instance = new MutexHolder();
            if (!_instance.IsFirst)
            {
                bool sent = SingleInstance.TrySend(initialPath);
                // 冷启动时第一个实例可能还没把管道服务器起好，多等几次
                for (int i = 0; i < 6 && !sent && !string.IsNullOrWhiteSpace(initialPath); i++)
                {
                    System.Threading.Thread.Sleep(300);
                    sent = SingleInstance.TrySend(initialPath);
                }

                if (!sent)
                {
                    Log.Info("已有实例在运行，但没有路径可转发，直接退出。");
                    if (!string.IsNullOrWhiteSpace(initialPath))
                    {
                        MessageBox.Show("本程序已经在运行，但暂时没能把文件交给它。\n请稍等一秒再双击一次，或从任务栏切到已打开的窗口。",
                            "Markdown 阅读器", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                Shutdown(0);
                return;
            }

            SingleInstance.StartServer(path =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var window = MainWindow as MainWindow;
                    if (window != null) window.OpenFromOutside(path);
                }));
            });

            var main = new MainWindow(initialPath);
            MainWindow = main;
            _isOwner = true;
            main.Show();

            // 启动时静默看一眼有没有新版本（默认关闭，设置里可以打开）
            if (Settings.CheckUpdateOnStart)
                _ = CheckUpdateInBackgroundAsync(main);
        }

        /// <summary>
        /// 后台检查更新。失败不打扰用户（只写日志），有新版才在工具栏亮个徽章。
        /// </summary>
        private static async Task CheckUpdateInBackgroundAsync(MainWindow window)
        {
            try
            {
                await Task.Delay(2000).ConfigureAwait(true);
                UpdateResult result = await UpdateChecker.CheckAsync(
                    Settings.UpdateRepo, VersionText, CancellationToken.None).ConfigureAwait(true);

                Settings.LastUpdateCheck = DateTimeOffset.Now.ToString("o");
                SaveSettings();

                if (!result.HasUpdate || !result.CanDownload) return;
                if (string.Equals(result.Latest, Settings.SkippedVersion, StringComparison.OrdinalIgnoreCase)) return;

                if (window != null) window.ShowUpdateBadge(result);
            }
            catch (Exception ex)
            {
                Log.Error("启动时检查更新失败", ex);
            }
        }

        /// <summary>
        /// <c>--snapshot</c>：把窗口渲染成 PNG 然后退出。给没法用截图工具的环境做界面自检。
        /// 带文件时要等 WebView2 起来并渲染完，只显示欢迎页则很快。
        /// </summary>
        private void RunSnapshot(CommandLineOptions options)
        {
            var window = new MainWindow(options.FilePath);
            MainWindow = window;
            _isOwner = true;
            window.Show();

            // 自检用：设了 MDVIEWER_TOAST_DEMO 就在截图前弹一条通知，
            // 好让没法手点导出（也就看不到通知）的环境也能拍下来检查排版。
            string toastDemo = Environment.GetEnvironmentVariable("MDVIEWER_TOAST_DEMO");
            if (!string.IsNullOrWhiteSpace(toastDemo))
                window.ShowToast("已导出 PDF（A4 横向）",
                    Path.GetFileName(toastDemo), toastDemo);

            // 自检用：MDVIEWER_SETTINGS_DEMO=<png 路径> 会把设置窗口一起打开并拍一张。
            // 设置窗口是 ShowDialog 模态，主窗口的 RenderTargetBitmap 拍不到它，只能单独拍。
            string settingsShot = Environment.GetEnvironmentVariable("MDVIEWER_SETTINGS_DEMO");
            SettingsWindow settingsWindow = null;
            if (!string.IsNullOrWhiteSpace(settingsShot))
            {
                settingsWindow = new SettingsWindow { Owner = window };
                settingsWindow.Show();
                settingsWindow.ScrollToBottom();
            }

            int delayMs = options.HasFile ? 9000 : 2500;

            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(delayMs);
            timer.Tick += async (sender, args) =>
            {
                timer.Stop();
                try
                {
                    window.SnapshotTo(options.SnapshotPath);
                    string message = "界面截图已保存：" + options.SnapshotPath;

                    if (settingsWindow != null)
                    {
                        WindowSnapshot.Capture(settingsWindow, settingsShot);
                        message += "\n设置窗口截图：" + settingsShot;
                    }

                    // WPF 截不到 WebView2 那个原生子窗口，正文单独让浏览器截一张
                    if (options.HasFile && !string.IsNullOrWhiteSpace(options.SnapshotPath))
                    {
                        string dir = Path.GetDirectoryName(options.SnapshotPath) ?? ".";
                        string page = Path.Combine(dir,
                            Path.GetFileNameWithoutExtension(options.SnapshotPath) + ".page.png");
                        if (await window.SnapshotPageAsync(page))
                            message += "\n正文截图：" + page;
                    }

                    WriteCliReport(message);
                }
                catch (Exception ex)
                {
                    Log.Error("界面截图失败", ex);
                    WriteCliReport("界面截图失败：" + ex.Message);
                }
                Shutdown(0);
            };
            timer.Start();
        }

        /// <summary>
        /// <c>--make-shortcut &lt;lnk&gt;</c>：在指定位置建一个指向本程序的快捷方式。
        /// 安装脚本用它，省得在 PowerShell 里碰 COM（UTF-8 代码页机器上会写坏中文名）。
        /// </summary>
        private void RunMakeShortcut(CommandLineOptions options)
        {
            try
            {
                string exe = Environment.ProcessPath ?? string.Empty;
                string workDir = Path.GetDirectoryName(exe) ?? string.Empty;
                Shortcuts.Create(options.ShortcutPath, exe, workDir, "Markdown 阅读器 - 双击 .md 直接看");
                WriteCliReport("快捷方式已创建：" + options.ShortcutPath);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Log.Error("创建快捷方式失败", ex);
                WriteCliReport("创建快捷方式失败：" + ex.Message);
                Shutdown(1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 第二个实例只转发路径，不拥有界面，也不许写 settings.json（否则会把已打开窗口的改动覆盖掉）
            if (_isOwner) SaveSettings();
            UnhookSystemThemeChange();
            SingleInstance.Stop();
            if (_instance != null) _instance.Dispose();
            base.OnExit(e);
        }

        /// <summary>把主题色刷进 Application.Resources，XAML 里的 DynamicResource 跟着变。</summary>
        public static void ApplyTheme(Theme theme)
        {
            if (theme == null || Current == null) return;
            foreach (var pair in theme.ToResourceMap())
            {
                var brush = new SolidColorBrush(ParseColor(pair.Value));
                brush.Freeze();
                Current.Resources[pair.Key] = brush;
            }
        }

        // ───────────── 「跟随系统」要真的跟得上 ─────────────

        private static bool _systemThemeHooked;

        private static void HookSystemThemeChange()
        {
            if (_systemThemeHooked) return;
            try
            {
                Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
                _systemThemeHooked = true;
            }
            catch (Exception ex)
            {
                // 拿不到系统事件不影响主流程：主题固定用启动时读到的那个
                Log.Error("监听系统主题变化失败", ex);
            }
        }

        private static void UnhookSystemThemeChange()
        {
            if (!_systemThemeHooked) return;
            try { Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged; }
            catch (Exception) { }
            _systemThemeHooked = false;
        }

        /// <summary>
        /// 事件是后台线程发的，要切回界面线程。General 这个分类覆盖「个性化 → 颜色 → 选择默认应用模式」。
        /// </summary>
        private static void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            if (e.Category != Microsoft.Win32.UserPreferenceCategory.General) return;

            Application app = Current;
            if (app == null) return;

            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Settings == null) return;
                // 用户已经固定了浅色 / 深色，就别再跟着系统跳
                if (!string.Equals(Settings.Theme ?? "system", "system", StringComparison.OrdinalIgnoreCase)) return;

                var window = app.MainWindow as MainWindow;
                if (window != null) window.ApplySystemThemeChange();
            }));
        }

        private static Color ParseColor(string text)
        {
            try
            {
                object parsed = ColorConverter.ConvertFromString(text);
                if (parsed is Color color) return color;
            }
            catch (Exception)
            {
                // 颜色写错就用透明兜底，不抛
            }
            return Colors.Transparent;
        }

        public static void SaveSettings()
        {
            try { SettingsStore.Save(DataDir, Settings); }
            catch (Exception ex) { Log.Error("保存设置失败", ex); }
        }

        /// <summary>
        /// 把设置恢复默认值。先备份旧的 settings.json，再把主题刷到界面上。
        /// 返回备份文件路径（没备份成功时为 null），界面拿它给用户一句交代。
        /// </summary>
        public static string ResetSettings()
        {
            string backup = null;
            try { backup = SettingsStore.Backup(DataDir); }
            catch (Exception ex) { Log.Error("备份设置失败", ex); }

            Settings = SettingsStore.Reset(Settings);
            SaveSettings();

            ApplyTheme(Theme.ForMarkdownTheme(EffectiveMarkdownTheme));
            Log.Info("设置已恢复默认值，备份：" + (backup ?? "(无)"));
            return backup;
        }

        /// <summary>自动更新会把旧 exe 改名成 .old；下次启动时清掉，别一直堆在安装目录里。</summary>
        private static void CleanupOldExecutables()
        {
            try
            {
                string exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;

                string dir = Path.GetDirectoryName(exe);
                string name = Path.GetFileName(exe);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name) || !Directory.Exists(dir)) return;

                foreach (string file in Directory.GetFiles(dir, name + "*.old"))
                {
                    try { File.Delete(file); }
                    catch (Exception) { /* 正被占用就下次再说 */ }
                }
            }
            catch (Exception ex)
            {
                Log.Error("清理旧版本 exe 失败", ex);
            }
        }

        /// <summary>清掉上次渲染留下的 html，别让 cache 目录越堆越大。</summary>
        private static void CleanCache()
        {
            // 只留最近 20 个、超过 7 天的清掉（最新 3 个无论如何留着）
            CacheStore.Trim(CacheDir, keep: 20, maxAgeDays: 7);
            TrimEpubCache();
        }

        /// <summary>EPUB 解压出来的图片很占地方，只留最近 3 本。</summary>
        private static void TrimEpubCache()
        {
            try
            {
                if (!Directory.Exists(EpubCacheDir)) return;

                var dirs = new DirectoryInfo(EpubCacheDir).GetDirectories();
                if (dirs.Length <= 3) return;

                Array.Sort(dirs, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                for (int i = 3; i < dirs.Length; i++)
                {
                    try { dirs[i].Delete(true); }
                    catch (Exception) { /* 正在用的那本删不掉就留着 */ }
                }
            }
            catch (Exception ex)
            {
                Log.Error("清理 EPUB 缓存失败", ex);
            }
        }

        /// <summary>命令行模式的结果写文件，不弹模态框（否则脚本里调用会卡住）。</summary>
        private static void WriteCliReport(string text)
        {
            try
            {
                string path = Path.Combine(DataDir, "cli-result.txt");
                File.WriteAllText(path, (text ?? string.Empty).Replace("\r\n", "\n"), new System.Text.UTF8Encoding(true));
                Log.Info("命令行说明已写入 " + path);
            }
            catch (Exception ex) { Log.Error("写命令行结果失败", ex); }
        }

        private sealed class MutexHolder : IDisposable
        {
            public bool IsFirst { get; }

            public MutexHolder()
            {
                IsFirst = SingleInstance.TryAcquire();
            }

            public void Dispose()
            {
                if (IsFirst) SingleInstance.Stop();
            }
        }
    }
}
