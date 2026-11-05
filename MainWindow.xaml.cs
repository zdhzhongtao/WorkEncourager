using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WorkEnc.Core;

namespace WorkEncourager
{
    public partial class MainWindow : Window
    {
        private static readonly string CfgDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WorkEncourager");

        /// <summary>
        /// 数据目录。调试模式下走独立子目录，不污染用户真实数据。
        /// 配置、历史、导出的 CSV、日志全部落在这里。
        /// </summary>
        private static readonly string DataDir = App.DebugMode ? Path.Combine(CfgDir, "debug") : CfgDir;

        /// <summary>配置文件路径。</summary>
        private readonly string CfgPath = Path.Combine(DataDir, "settings.json");

        /// <summary>工时历史路径。</summary>
        private readonly string HistoryPath = Path.Combine(DataDir, "history.json");

        private const double DefaultWidth = 432;
        private const double DefaultHeight = 388;

        private AppConfig _cfg = new AppConfig();
        private PayCalendar _calendar = new PayCalendar();
        private WorkSchedule _schedule = new WorkSchedule();
        private SalaryCalculator _calc = new SalaryCalculator();
        private WorkdayTracker _tracker = new WorkdayTracker();
        private QuoteProvider _quotes = new QuoteProvider();

        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private bool _paused;
        private DateTime _pauseAt;
        private double _pausedAccumSec;
        private string _dayKey = "";
        private bool _loaded;
        private bool _debugNoPersist;      // 调试用：--paused 启动时不把暂停状态写回配置

        private string _lastStatus = "";
        private string _lastEncText = null;
        private double _lastPct = double.NaN;

        // 视图状态
        private AppViewMode _viewMode = AppViewMode.Full;
        private MainTab _tab = MainTab.Home;
        private bool _userResizedFull;     // 用户在完整模式下手动拖过窗体边框
        private bool _userResizedMini;     // 用户在迷你模式下手动拖过窗体边框

        private Theme _theme = Theme.Light();
        private Brush _bTextPrimary, _bTextSecondary, _bTextMuted, _bAccent, _bAccentDark, _bEncText;
        private Brush _bChipBg, _bChipFg, _bChipDot;

        private readonly string[] _weekNames = { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };

        // ---------------- 托盘常驻 ----------------

        private TrayIcon _tray;
        /// <summary>true 表示真的退出；false 时点关闭只是隐藏到托盘。</summary>
        private bool _reallyExit;
        private bool _hiddenOnce;
        private DateTime _lastHistorySave = DateTime.Now;
        private DateTime _trayTipAt = DateTime.MinValue;
        private int _showInstanceMsg;

        // ---------------- 穿透逃生 ----------------

        /// <summary>全局热键 Ctrl+Alt+X 的 id。</summary>
        private const int HotkeyIdToggleClickThrough = 0xC101;
        private const int VkX = 0x58;

        /// <summary>
        /// 穿透状态下仍然可点的顶部条高度（DIP）。
        /// 保留这一条，用户才能点标题栏的按钮把穿透关掉、把窗口收起来。
        /// </summary>
        private const double FullHeaderZone = 50;
        private const double MiniHeaderZone = 30;

        private bool _hotkeyRegistered;
        private bool _clickThroughHinted;
        private IntPtr _hwnd = IntPtr.Zero;

        /// <summary>用户语录文件（CSV，Excel 可直接编辑）。</summary>
        private string _quotePath;
        private DateTime _quoteStamp = DateTime.MinValue;

        public MainWindow()
        {
            InitializeComponent();
            LoadCfg();
            ApplyTheme();
            ApplyTopmost();
            ApplyOpacity();
            // 注意：鼠标穿透不能在构造函数里设——此时窗口句柄还没创建，
            // SetWindowLong 会拿不到 HWND，配置里的穿透状态会被静默丢掉（v1.0.0 的 bug）。
            // 真正的应用时机在 Loaded（见下方）。
            RestoreViewState();

            _timer.Interval = TimeSpan.FromMilliseconds(200);
            _timer.Tick += (s, e) => Render();
            _timer.Start();

            Shell.MouseLeftButtonDown += OnDragArea;
            MiniShell.MouseLeftButtonDown += OnDragArea;
            SizeChanged += (s, e) => { UpdateBar(); UpdateSizeText(); };
            Loaded += (s, e) =>
            {
                _loaded = true;
                ApplyViewMode();
                // 句柄已就绪，这时设置穿透才会真正生效
                ApplyClickThrough();
                Render();
                UpdateBar();
                UpdateSizeText();

                _ = CheckUpdateSilentlyAsync();

                // 恢复的配置里穿透是开着的：一启动就把怎么关说清楚，别让人再摸黑
                if (_cfg.ClickThrough && !_clickThroughHinted)
                {
                    _clickThroughHinted = true;
                    _tray?.ShowBalloon("鼠标穿透处于开启状态",
                        "内容区点击会穿到下层，顶部标题栏仍可操作。要关掉：按 Ctrl+Alt+X，或右键托盘图标。");
                }
            };

            // 窗口边缘缩放热区
            foreach (var grip in new FrameworkElement[]
                     { GripTop, GripBottom, GripLeft, GripRight,
                       GripTopLeft, GripTopRight, GripBottomLeft, GripBottomRight })
            {
                grip.MouseLeftButtonDown += OnGripMouseDown;
                grip.MouseMove += OnGripMouseMove;
                grip.MouseLeftButtonUp += OnGripMouseUp;
                grip.MouseLeave += OnGripMouseLeave;
            }

            ToggleBtn.Click += OnToggleClick;
            ResetBtn.Click += OnResetClick;
            PinBtn.Click += OnPinClick;
            CloseBtn.Click += (s, e) => HideToTray();
            MiniBtn.Click += (s, e) => SetViewMode(AppViewMode.Mini);
            MiniExpandBtn.Click += (s, e) => SetViewMode(AppViewMode.Full);
            MiniShell.AddHandler(UIElement.MouseLeftButtonDownEvent,
                new MouseButtonEventHandler(OnMiniShellMouseDown));

            TabHomeBtn.Click += (s, e) => SelectTab(MainTab.Home);
            TabDetailBtn.Click += (s, e) => SelectTab(MainTab.Detail);
            TabStatsBtn.Click += (s, e) => SelectTab(MainTab.Stats);
            TabSettingsBtn.Click += (s, e) => SelectTab(MainTab.Settings);

            CancelBtn.Click += (s, e) => SelectTab(MainTab.Home);
            SaveBtn.Click += (s, e) => SaveSettings();
            DefaultBtn.Click += (s, e) => FillSettings(new AppConfig());
            ExportCfgBtn.Click += (s, e) => ExportConfig();
            ImportCfgBtn.Click += (s, e) => ImportConfig();
            ExportCsvBtn.Click += (s, e) => ExportCsv();

            StatsExportCsvBtn.Click += (s, e) => ExportCsv();
            StatsExportMonthlyBtn.Click += (s, e) => ExportMonthlyCsv();

            SettingsCopyPathBtn.Click += (s, e) => CopyLastPath(SettingsTip);
            SettingsOpenFolderBtn.Click += (s, e) => OpenLastFolder(SettingsTip);
            StatsCopyPathBtn.Click += (s, e) => CopyLastPath(StatsTip);
            StatsOpenFolderBtn.Click += (s, e) => OpenLastFolder(StatsTip);

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    if (_viewMode == AppViewMode.Mini) { SetViewMode(AppViewMode.Full); e.Handled = true; }
                    else if (_tab != MainTab.Home) { SelectTab(MainTab.Home); e.Handled = true; }
                }
            };

            Closing += (s, e) =>
            {
                // 关闭按钮 = 隐藏到托盘，不是退出。真正退出走托盘右键菜单（或系统关机）。
                if (!_reallyExit)
                {
                    e.Cancel = true;
                    HideToTray();
                    return;
                }

                RememberBounds();
                if (!_debugNoPersist) PersistPauseState();
                SaveCfg();
                _tracker.Save(HistoryPath);

                _tray?.Dispose();
                _tray = null;
            };

            // 注销 / 关机时不能被"隐藏到托盘"拦住，否则系统关不掉机
            if (Application.Current != null)
                Application.Current.SessionEnding += (s, e) => _reallyExit = true;

            InSalary.TextChanged += (s, e) => UpdatePreview();
            InStart.TextChanged += (s, e) => { UpdateAutoHours(); UpdatePreview(); };
            InEnd.TextChanged += (s, e) => { UpdateAutoHours(); UpdatePreview(); };
            InBreak.TextChanged += (s, e) => { UpdateAutoHours(); UpdatePreview(); };
            InShirt.TextChanged += (s, e) => UpdatePreview();
            InGoalItem.TextChanged += (s, e) => UpdatePreview();
            InPayDay.TextChanged += (s, e) => UpdatePreview();

            OpacitySlider.ValueChanged += (s, e) =>
            {
                if (!_loaded) return;
                Opacity = OpacitySlider.Value;
                _cfg.Opacity = Opacity;   // 幽灵模式需要用这个值作为"正常"基准
                OpacityValueText.Text = Math.Round(Opacity * 100).ToString() + "%";
            };
            ResetSizeBtn.Click += (s, e) => { SetSize(ViewState.FullWindowWidth, ViewState.FullWindowHeight(_tab)); UpdateSizeText(); };
            SizeSmallBtn.Click += (s, e) => SetSize(340, 300);
            SizeMidBtn.Click += (s, e) => SetSize(ViewState.FullWindowWidth, ViewState.FullWindowHeight(_tab));
            SizeBigBtn.Click += (s, e) => SetSize(560, 620);

            ToneChillBtn.Click += (s, e) => SetTone("chill");
            ToneSavageBtn.Click += (s, e) => SetTone("savage");
            ToneHypeBtn.Click += (s, e) => SetTone("hype");
            ThemeLightBtn.Click += (s, e) => SetTheme("light");
            ThemeDarkBtn.Click += (s, e) => SetTheme("dark");
            MiniToggleBtn.Click += (s, e) => SetViewMode(
                _viewMode == AppViewMode.Mini ? AppViewMode.Full : AppViewMode.Mini);
            ClickThroughToggleBtn.Click += (s, e) => ToggleClickThrough();
            GhostModeBtn.Click += (s, e) => ToggleGhostMode();
            SnapEdgeBtn.Click += (s, e) => ToggleSnapEdge();
            BackupAllBtn.Click += (s, e) => BackupAll();
            RestoreAllBtn.Click += (s, e) => RestoreAll();
            ImportWorkLogBtn.Click += (s, e) => ImportWorkLog();
            EditQuotesBtn.Click += (s, e) => EditQuotes();
            CheckUpdateBtn.Click += (s, e) => CheckUpdate();

            if (VersionText != null) VersionText.Text = "v" + UpdateChecker.CurrentVersion;

            // 幽灵模式靠这两个事件判断鼠标是否在窗口里
            MouseEnter += (s, e) => SetGhostFaded(false);
            MouseLeave += (s, e) => SetGhostFaded(true);

            InitTray();

            // 跨不同缩放比的显示器移动后位置可能落到屏幕外，这里兜一次底
            DpiChanged += (s, e) =>
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(EnsureWindowVisible));
        }

        // ---------------- 托盘常驻 ----------------

        private void InitTray()
        {
            try
            {
                _tray = new TrayIcon();
                _tray.ToggleRequested += ToggleWindowVisibility;
                _tray.ModeSwitchRequested += () =>
                {
                    ShowFromTray();
                    SetViewMode(_viewMode == AppViewMode.Mini ? AppViewMode.Full : AppViewMode.Mini);
                };
                _tray.SettingsRequested += () =>
                {
                    ShowFromTray();
                    // 穿透状态下窗口根本点不到，先进设置就得恢复可点击，否则是个死路
                    if (_cfg.ClickThrough)
                    {
                        _cfg.ClickThrough = false;
                        ApplyClickThrough();
                        SaveCfg();
                    }
                    SelectTab(MainTab.Settings);
                };
                _tray.ClickThroughToggleRequested += ToggleClickThrough;
                _tray.PauseToggleRequested += () => OnToggleClick(null, null);
                _tray.ResetWindowRequested += ResetWindowLayout;
                _tray.ExitRequested += ExitApp;
                _tray.SetWindowVisible(IsVisible);
                _tray.SetMode(_viewMode == AppViewMode.Mini);
                _tray.SetClickThrough(_cfg.ClickThrough);
                _tray.SetPaused(_paused);
            }
            catch (Exception ex)
            {
                // 托盘创建失败不能导致主程序起不来
                Log.Error("创建托盘图标失败", ex);
            }
        }

        /// <summary>左键点托盘：可见就收起来，隐藏就弹回来。</summary>
        private void ToggleWindowVisibility()
        {
            if (IsVisible) HideToTray();
            else ShowFromTray();
        }

        /// <summary>
        /// 隐藏到托盘：只是不显示窗口，计时器继续跑。
        /// 这是"关闭按钮"的新语义——真正退出走托盘右键菜单。
        /// </summary>
        private void HideToTray()
        {
            if (!IsVisible) return;

            // 先把当前几何状态落盘（Hide 不会触发 Closing）
            RememberBounds();
            SaveCfg();

            Hide();
            _tray?.SetWindowVisible(false);

            if (!_hiddenOnce)
            {
                _hiddenOnce = true;
                _tray?.ShowBalloon("还在后台计时",
                    "窗口已隐藏，计时不会停。左键点托盘图标可以唤回来，右键可退出。");
            }
        }

        /// <summary>从托盘恢复窗口并置前。</summary>
        private void ShowFromTray()
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

            // 隐藏期间可能换过显示器/改了分辨率
            EnsureWindowVisible();

            Topmost = false;
            Topmost = _cfg.Topmost;
            Activate();
            _tray?.SetWindowVisible(true);
        }

        /// <summary>真正退出（走托盘右键菜单）。</summary>
        private void ExitApp()
        {
            _reallyExit = true;
            Close();
        }

        /// <summary>把当前窗口位置/尺寸写进配置对象（不落盘）。</summary>
        private void RememberBounds()
        {
            // 注意：不能写 _cfg.Opacity = Opacity。
            // 幽灵模式淡化时 Opacity 是临时值，存进去下次启动就变成"默认很淡"。
            // 滑块改动时已经实时同步过 _cfg.Opacity，这里保持原值即可。
            _cfg.Topmost = Topmost;
            _cfg.ViewMode = ViewState.ToText(_viewMode);
            _cfg.ActiveTab = ViewState.ToText(_tab);

            var screen = ScreenOfWindow();
            if (_viewMode == AppViewMode.Mini)
            {
                _cfg.MiniLeft = Left;
                _cfg.MiniTop = Top;
                _cfg.MiniWidth = ActualWidth;
                _cfg.MiniHeight = ActualHeight;
                if (screen != null) _cfg.MiniScreen = screen.DeviceName;
            }
            else
            {
                _cfg.WindowLeft = Left;
                _cfg.WindowTop = Top;
                _cfg.WindowWidth = ActualWidth;
                _cfg.WindowHeight = ActualHeight;
                if (screen != null) _cfg.WindowScreen = screen.DeviceName;
            }
        }

        /// <summary>当前窗口所在的显示器；句柄还没建时返回 null。</summary>
        private System.Windows.Forms.Screen ScreenOfWindow()
        {
            try
            {
                var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (h == IntPtr.Zero) return null;
                return System.Windows.Forms.Screen.FromHandle(h);
            }
            catch { return null; }
        }

        /// <summary>
        /// 取指定显示器的工作区（DIP）。找不到就回主屏。
        /// 副屏的物理像素按主屏缩放比换算成 DIP —— 多屏同缩放比时完全准确，
        /// 不同缩放比时略有偏差，但外面还有 IsOnAnyScreen 兜底，窗口不会丢。
        /// </summary>
        private static Rect WorkAreaOf(string deviceName)
        {
            Rect fallback = SystemParameters.WorkArea;
            if (string.IsNullOrEmpty(deviceName)) return fallback;

            try
            {
                var screens = System.Windows.Forms.Screen.AllScreens;
                var target = screens.FirstOrDefault(x =>
                    string.Equals(x.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
                if (target == null || target.Primary) return fallback;

                var primary = screens.FirstOrDefault(x => x.Primary);
                if (primary == null) return fallback;

                double kx = SystemParameters.WorkArea.Width / Math.Max(1.0, primary.WorkingArea.Width);
                double ky = SystemParameters.WorkArea.Height / Math.Max(1.0, primary.WorkingArea.Height);

                var a = target.WorkingArea;
                return new Rect(a.Left * kx, a.Top * ky, a.Width * kx, a.Height * ky);
            }
            catch { return fallback; }
        }

        /// <summary>
        /// 收到第二个实例的广播后，把窗口显示出来（单实例注册在 App 里）。
        /// </summary>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var src = (System.Windows.Interop.HwndSource)PresentationSource.FromVisual(this);
                if (src != null)
                {
                    _showInstanceMsg = Win32.WM_SHOW_INSTANCE;
                    src.AddHook(WndProc);
                    _hwnd = src.Handle;
                    RegisterClickThroughHotkey(src.Handle);
                }
            }
            catch (Exception ex) { Log.Error("注册窗口消息钩子失败", ex); }
        }

        /// <summary>
        /// 注册全局热键 Ctrl+Alt+X 切换穿透。
        /// 这是最硬的一道逃生门：即使标题栏被拖动、窗口位置不合适，热键仍然能把穿透关掉。
        /// </summary>
        private void RegisterClickThroughHotkey(IntPtr hwnd)
        {
            try
            {
                _hotkeyRegistered = Win32.RegisterHotKey(hwnd, HotkeyIdToggleClickThrough,
                    Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT, VkX);
                if (!_hotkeyRegistered)
                    Log.Info("全局热键 Ctrl+Alt+X 注册失败（可能被其它程序占用），仅保留标题栏与托盘入口");
            }
            catch (Exception ex) { Log.Error("注册全局热键失败", ex); }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_hotkeyRegistered && _hwnd != IntPtr.Zero)
            {
                try { Win32.UnregisterHotKey(_hwnd, HotkeyIdToggleClickThrough); } catch { }
                _hotkeyRegistered = false;
            }
            base.OnClosed(e);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // 第二个实例启动 → 把自己叫出来
            if (_showInstanceMsg != 0 && msg == _showInstanceMsg)
            {
                ShowFromTray();
                handled = true;
                return IntPtr.Zero;
            }

            // 全局热键：Ctrl+Alt+X 切换穿透
            if (msg == Win32.WM_HOTKEY && wParam.ToInt32() == HotkeyIdToggleClickThrough)
            {
                ToggleClickThrough();
                handled = true;
                return IntPtr.Zero;
            }

            // 穿透：除了顶部标题条，其余区域都让给下层窗口
            if (msg == Win32.WM_NCHITTEST && _cfg.ClickThrough)
                return HitTestClickThrough(lParam, ref handled);

            return IntPtr.Zero;
        }

        /// <summary>
        /// 穿透状态下的命中测试。
        /// 返回 HTTRANSPARENT = 这次点击不归我（穿透到下层窗口）。
        /// 顶部标题条、以及按住 Alt 时返回正常，保证用户任何时候都能点回去。
        /// </summary>
        private IntPtr HitTestClickThrough(IntPtr lParam, ref bool handled)
        {
            try
            {
                // 按住 Alt 临时恢复正常交互，方便把整个窗口拖走
                if ((Win32.GetKeyState(Win32.VK_MENU) & 0x8000) != 0) return IntPtr.Zero;

                long lp = lParam.ToInt64();
                int sx = (short)(lp & 0xFFFF);
                int sy = (short)((lp >> 16) & 0xFFFF);

                var local = PointFromScreen(new Point(sx, sy));
                double zone = _viewMode == AppViewMode.Mini ? MiniHeaderZone : FullHeaderZone;

                if (local.Y > zone)
                {
                    handled = true;
                    return new IntPtr(Win32.HTTRANSPARENT);
                }
            }
            catch (Exception ex) { Log.Error("穿透命中测试失败", ex); }
            return IntPtr.Zero;
        }

        // ---------------- 配置 ----------------

        private void LoadCfg()
        {
            _cfg = ConfigStore.Load(CfgPath);
            _calendar = ConfigStore.BuildCalendar(_cfg);
            _schedule = ConfigStore.BuildSchedule(_cfg);
            _calc = new SalaryCalculator(_schedule, _calendar);
            _tracker = WorkdayTracker.Load(HistoryPath);
            // 开启休眠保护：tick 间隔超过 90 秒视为机器睡过，不把睡眠时间算成工时
            _tracker.MaxGapSeconds = 90;
            _userResizedFull = _cfg.FullResized;
            _userResizedMini = _cfg.MiniResized;
            _quotePath = Path.Combine(DataDir, "quotes.csv");
            LoadQuotesIfChanged(force: true);
            RestorePauseState();
        }

        private void SaveCfg()
        {
            // 调试模式下不落盘，避免调试参数污染下次运行的状态
            if (App.DebugMode) return;
            ConfigStore.Save(CfgPath, _cfg);
        }

        /// <summary>把暂停状态写回配置，实现"重启后接着算"。</summary>
        private void PersistPauseState()
        {
            if (_paused)
            {
                _cfg.Paused = true;
                _cfg.PausedAt = _pauseAt.ToString("o");
                _cfg.PausedDate = _pauseAt.ToString("yyyy-MM-dd");
                _cfg.PausedAccumSec = _pausedAccumSec;
            }
            else
            {
                _cfg.Paused = false;
                _cfg.PausedAt = "";
                _cfg.PausedDate = "";
                _cfg.PausedAccumSec = 0;
            }
            _cfg.LastRunDate = DateTime.Now.ToString("yyyy-MM-dd");
        }

        /// <summary>从配置恢复暂停状态（跨重启不丢）。</summary>
        private void RestorePauseState()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (_cfg.Paused && !string.IsNullOrEmpty(_cfg.PausedAt) &&
                DateTime.TryParse(_cfg.PausedAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var pa) && _cfg.PausedDate == today)
            {
                _paused = true;
                _pauseAt = pa;
                _pausedAccumSec = _cfg.PausedAccumSec;
                return;
            }
            _paused = false;
            _pausedAccumSec = 0;
            _cfg.Paused = false;
            _cfg.PausedAt = "";
            _cfg.PausedDate = "";
        }

        private void RebuildCalc()
        {
            _calendar = ConfigStore.BuildCalendar(_cfg);
            _schedule = ConfigStore.BuildSchedule(_cfg);
            _calc = new SalaryCalculator(_schedule, _calendar);
        }

        // ---------------- 暂停 / 重置 ----------------

        private void OnToggleClick(object sender, RoutedEventArgs e)
        {
            if (!_paused)
            {
                // 先把到此刻为止的工时结算掉，再进入暂停，否则这段工时会丢失
                _tracker.Advance(DateTime.Now, DayScheduleProvider, false);
                _paused = true;
                _pauseAt = DateTime.Now;
            }
            else
            {
                _paused = false;
                // 累计的是"暂停期间落入工作时段的秒数"，而不是墙上时钟秒数，
                // 这样暂停横跨午休时不会多扣工时。
                _pausedAccumSec += PausedWorkSec(_pauseAt, DateTime.Now);
                _pauseAt = DateTime.MinValue;
            }
            PersistPauseState();
            SaveCfg();
            _tray?.SetPaused(_paused);
            Render();
        }

        /// <summary>暂停期间实际落在工作时段内的秒数（午休段不计入）。</summary>
        private double PausedWorkSec(DateTime from, DateTime to)
        {
            if (to <= from) return 0;
            if (from.Date == to.Date)
            {
                return _schedule.GetElapsedWorkSeconds(from.Date, to)
                     - _schedule.GetElapsedWorkSeconds(from.Date, from);
            }
            return (to - from).TotalSeconds;   // 跨天暂停无法按天拆，退化为墙上时钟
        }

        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            _paused = false;
            _pausedAccumSec = 0;
            _pauseAt = DateTime.MinValue;
            PersistPauseState();
            SaveCfg();
            Render();
        }

        // ---------------- 窗口边缘缩放 ----------------

        private enum ResizeDir { None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

        private static readonly Dictionary<string, ResizeDir> _gripDirs =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["GripLeft"] = ResizeDir.Left,
                ["GripRight"] = ResizeDir.Right,
                ["GripTop"] = ResizeDir.Top,
                ["GripBottom"] = ResizeDir.Bottom,
                ["GripTopLeft"] = ResizeDir.TopLeft,
                ["GripTopRight"] = ResizeDir.TopRight,
                ["GripBottomLeft"] = ResizeDir.BottomLeft,
                ["GripBottomRight"] = ResizeDir.BottomRight,
            };

        private ResizeDir _resizeDir = ResizeDir.None;
        private Point _resizeAnchor;                 // 按下时的屏幕坐标（物理像素）
        private double _resizeStartW, _resizeStartH, _resizeStartLeft, _resizeStartTop;

        private void OnGripMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (!_gripDirs.TryGetValue(fe.Name, out var dir)) return;

            _resizeDir = dir;
            _resizeAnchor = PointToScreen(e.GetPosition(this));
            _resizeStartW = Width;
            _resizeStartH = Height;
            _resizeStartLeft = Left;
            _resizeStartTop = Top;
            fe.CaptureMouse();
            e.Handled = true;
        }

        private void OnGripMouseMove(object sender, MouseEventArgs e)
        {
            if (_resizeDir == ResizeDir.None) return;
            if (e.LeftButton != MouseButtonState.Pressed) { EndResize(sender); return; }

            // PointToScreen 返回物理像素，窗口尺寸用的是 DIP，需要除以 DPI 缩放
            var dpi = VisualTreeHelper.GetDpi(this);
            var cur = PointToScreen(e.GetPosition(this));
            double dx = (cur.X - _resizeAnchor.X) / dpi.DpiScaleX;
            double dy = (cur.Y - _resizeAnchor.Y) / dpi.DpiScaleY;

            double w = _resizeStartW, h = _resizeStartH, l = _resizeStartLeft, t = _resizeStartTop;

            bool moveLeft = _resizeDir is ResizeDir.Left or ResizeDir.TopLeft or ResizeDir.BottomLeft;
            bool moveRight = _resizeDir is ResizeDir.Right or ResizeDir.TopRight or ResizeDir.BottomRight;
            bool moveTop = _resizeDir is ResizeDir.Top or ResizeDir.TopLeft or ResizeDir.TopRight;
            bool moveBottom = _resizeDir is ResizeDir.Bottom or ResizeDir.BottomLeft or ResizeDir.BottomRight;

            if (moveLeft) { w = _resizeStartW - dx; l = _resizeStartLeft + dx; }
            else if (moveRight) { w = _resizeStartW + dx; }
            if (moveTop) { h = _resizeStartH - dy; t = _resizeStartTop + dy; }
            else if (moveBottom) { h = _resizeStartH + dy; }

            // 夹到最小/最大尺寸，同时把对边拉回来，保证锚点那一侧不移动
            if (w < MinWidth) { if (moveLeft) l -= (MinWidth - w); w = MinWidth; }
            if (h < MinHeight) { if (moveTop) t -= (MinHeight - h); h = MinHeight; }
            if (!double.IsInfinity(MaxWidth) && MaxWidth > 0 && w > MaxWidth)
            { if (moveLeft) l += (w - MaxWidth); w = MaxWidth; }
            if (!double.IsInfinity(MaxHeight) && MaxHeight > 0 && h > MaxHeight)
            { if (moveTop) t += (h - MaxHeight); h = MaxHeight; }

            Width = w; Height = h; Left = l; Top = t;
            e.Handled = true;
        }

        private void OnGripMouseUp(object sender, MouseButtonEventArgs e)
        {
            EndResize(sender);
            e.Handled = true;
        }

        private void OnGripMouseLeave(object sender, MouseEventArgs e)
        {
            // 鼠标带着拖出窗口时松开，也要结束缩放
            if (_resizeDir != ResizeDir.None && e.LeftButton != MouseButtonState.Pressed)
                EndResize(sender);
        }

        private void EndResize(object sender)
        {
            if (_resizeDir == ResizeDir.None) return;
            _resizeDir = ResizeDir.None;
            if (sender is FrameworkElement fe) fe.ReleaseMouseCapture();
            SaveWindowBounds();
        }

        /// <summary>保存当前窗口位置与尺寸到配置（完整模式与迷你模式分开记）。</summary>
        private void SaveWindowBounds()
        {
            if (_viewMode == AppViewMode.Mini)
            {
                _userResizedMini = true;
                _cfg.MiniResized = true;
                _cfg.MiniLeft = Left;
                _cfg.MiniTop = Top;
                _cfg.MiniWidth = ActualWidth;
                _cfg.MiniHeight = ActualHeight;
            }
            else
            {
                _userResizedFull = true;
                _cfg.FullResized = true;
                _cfg.WindowLeft = Left;
                _cfg.WindowTop = Top;
                _cfg.WindowWidth = ActualWidth;
                _cfg.WindowHeight = ActualHeight;
            }
            SaveCfg();
        }

        // ---------------- 渲染 ----------------

        private static string N4(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
            return v.ToString("N4", CultureInfo.InvariantCulture);
        }

        private static string HM(double sec)
        {
            if (double.IsNaN(sec) || sec < 0) sec = 0;
            int totalMin = (int)Math.Round(sec / 60.0);
            if (totalMin >= 60)
            {
                int h = totalMin / 60, m = totalMin % 60;
                return m > 0 ? h + "小时" + m + "分" : h + "小时";
            }
            if (totalMin >= 1) return totalMin + "分钟";
            return "不到1分钟";
        }

        private static string Clock(double sec)
        {
            if (double.IsNaN(sec) || sec < 0) sec = 0;
            int s = (int)Math.Floor(sec);
            int h = s / 3600, m = (s % 3600) / 60, ss = s % 60;
            return h.ToString("00") + ":" + m.ToString("00") + ":" + ss.ToString("00");
        }

        /// <summary>按日期提供当日工作时段与下班时刻，供工时追踪器使用。</summary>
        private (List<TimeSegment>, DateTime) DayScheduleProvider(DateTime day)
        {
            var segs = _schedule.GetWorkSegments(day);
            return (segs, _schedule.GetEndTime(day));
        }

        /// <summary>
        /// 每 60 秒把工时写盘一次。
        /// v1.0.0 只在退出时写，被强杀 / 断电就丢掉一整天数据。
        /// </summary>
        private void PersistPeriodically(DateTime now)
        {
            if (App.DebugMode) return;
            if ((now - _lastHistorySave).TotalSeconds < 60) return;
            _lastHistorySave = now;
            _tracker.Save(HistoryPath);
        }

        /// <summary>更新托盘悬停提示（5 秒一次，避免频繁调 Shell_NotifyIcon）。</summary>
        private void UpdateTrayTip(DateTime now, Snapshot st, string statusText = null)
        {
            if ((now - _trayTipAt).TotalSeconds < 5) return;
            _trayTipAt = now;

            // 用户可能刚用 Excel 改完语录，这里顺便热加载
            LoadQuotesIfChanged();

            if (_tray == null) return;
            _tray.UpdateTip("上班鼓励器 · " + (statusText ?? StatusLabel(st.Status)) + " · 今日已赚 ¥" + N4(st.Earned));
            _tray.SetMode(_viewMode == AppViewMode.Mini);
        }

        private static string StatusLabel(string status) => status switch
        {
            "paused" => "已暂停",
            "rest" => "休息日",
            "off" => "已下班",
            "wait" => "待上班",
            _ => "上班中"
        };

        private void Render()
        {
            var now = DateTime.Now;

            // 跨天重置
            string key = now.ToString("yyyy-MM-dd");
            if (_dayKey.Length > 0 && key != _dayKey)
            {
                _paused = false;
                _pausedAccumSec = 0;
                _dayKey = key;
                SaveCfg();
            }
            _dayKey = key;

            // 追踪真实在场时长（"开着就在工作"）
            _tracker.Advance(now, DayScheduleProvider, _paused);

            // 窗口隐藏在托盘里时只维护数据，不去碰任何 UI 元素：
            // 既省 CPU，也避免对不可见元素做无意义的布局失效。
            if (!IsVisible)
            {
                var hidden = _calc.Compute(now, _cfg.MonthlySalary ?? 0, _cfg.Weekdays, _cfg.PayDay,
                                           _paused, _pausedAccumSec,
                                           _paused ? _pauseAt : (DateTime?)null);
                PersistPeriodically(now);
                UpdateTrayTip(now, hidden);
                return;
            }

            var st = _calc.Compute(now, _cfg.MonthlySalary ?? 0, _cfg.Weekdays, _cfg.PayDay,
                                  _paused, _pausedAccumSec,
                                  _paused ? _pauseAt : (DateTime?)null);

            var todayRec = _tracker.GetOrCreate(key);
            todayRec.ScheduledSec = st.TotalSec;
            todayRec.Earned = st.Earned;
            todayRec.IsPayDay = _calendar.IsPayDay(now, _cfg.Weekdays);
            todayRec.DayNote = PayCalendar.IsBuiltInHoliday(now) ? "法定节假日"
                            : PayCalendar.IsBuiltInMakeupWorkday(now) ? "调休上班" : "";

            DateText.Text = now.ToString("yyyy年M月d日") + " " + _weekNames[(int)now.DayOfWeek];
            TimeText.Text = now.ToString("HH:mm:ss");

            // 状态胶囊（仅在变化时改画刷）
            string statusText;
            switch (st.Status)
            {
                case "paused": statusText = "已暂停"; break;
                case "rest": statusText = "休息日"; break;
                case "off": statusText = "已下班"; break;
                case "wait": statusText = "待上班"; break;
                default: statusText = "上班中"; break;
            }
            if (statusText != _lastStatus)
            {
                _lastStatus = statusText;
                StatusText.Text = statusText;
                switch (st.Status)
                {
                    case "paused":
                        _bChipBg = MakeBrush(_theme.ChipPausedBg); _bChipFg = MakeBrush(_theme.ChipPausedFg); _bChipDot = MakeBrush(_theme.ChipPausedDot); break;
                    case "rest":
                    case "off":
                        _bChipBg = MakeBrush(_theme.ChipOffBg); _bChipFg = MakeBrush(_theme.ChipOffFg); _bChipDot = MakeBrush(_theme.ChipOffDot); break;
                    case "wait":
                        _bChipBg = MakeBrush(_theme.ChipWaitBg); _bChipFg = MakeBrush(_theme.ChipWaitFg); _bChipDot = MakeBrush(_theme.ChipWaitDot); break;
                    default:
                        _bChipBg = MakeBrush(_theme.ChipWorkingBg); _bChipFg = MakeBrush(_theme.ChipWorkingFg); _bChipDot = MakeBrush(_theme.ChipWorkingDot); break;
                }
                StatusChip.Background = _bChipBg;
                StatusText.Foreground = _bChipFg;
                StatusDot.Fill = _bChipDot;
            }

            if (st.Status == "paused") CountText.Text = "计时已暂停";
            else if (st.Status == "rest") CountText.Text = "今天休息 · 加班工时照常记录";
            else if (st.Status == "wait") CountText.Text = "距上班 " + Clock((st.StartTs - now).TotalSeconds);
            else if (st.Status == "off") CountText.Text = "今日已收工";
            else CountText.Text = "距下班 " + Clock(st.RemainSec);

            EarnedText.Text = N4(st.Earned);
            RateText.Text = st.PerSec.ToString("N4", CultureInfo.InvariantCulture);
            DailyTipText.Text = "日薪 ¥" + N4(st.DailyPay) + " · 满勤 " +
                                (st.TotalSec / 3600.0).ToString("0.#", CultureInfo.InvariantCulture) + " 小时";

            PctText.Text = st.Pct.ToString("0.0", CultureInfo.InvariantCulture);
            WorkedText.Text = (st.WorkedSec / 3600.0).ToString("0.00", CultureInfo.InvariantCulture);
            TotalText.Text = (st.TotalSec / 3600.0).ToString("0.0", CultureInfo.InvariantCulture);
            RemainText.Text = (st.RemainSec / 3600.0).ToString("0.00", CultureInfo.InvariantCulture);
            UpdateBar(st.Pct);

            MonthLabel.Text = now.Month + "月累计";
            MonthText.Text = N4(st.MonthTotal);
            MonthPctText.Text = st.MonthPct.ToString("0.0", CultureInfo.InvariantCulture);
            UpdateMonthBar(st.MonthPct);
            HourlyText.Text = N4(st.Hourly);
            FullText.Text = N4(st.DailyPay);

            if (st.NextPayDate.HasValue)
            {
                var d = st.NextPayDate.Value;
                if (st.PayDayInDays == 0) PayDayText.Text = "今天发工资";
                else if (st.PayDayInDays == 1) PayDayText.Text = "明天发工资";
                else PayDayText.Text = "距发薪 " + st.PayDayInDays + " 天";
                PayDaySubText.Text = d.ToString("M月d日") + " 到账 · 本月 " + st.MonthPayDays + " 个计薪日";
            }
            else PayDayText.Text = "未设置发薪日";

            ActualWorkedText.Text = (todayRec.WorkedSec / 3600.0).ToString("0.00", CultureInfo.InvariantCulture);

            ToggleBtn.Content = _paused ? "开启计时器" : "关闭计时器";
            ToggleBtn.Style = (Style)FindResource(_paused ? "GreenBtn" : "PrimaryBtn");

            // 迷你挂件
            if (_viewMode == AppViewMode.Mini)
            {
                MiniEarned.Text = N4(st.Earned);
                MiniCount.Text = st.Status == "paused" ? "计时已暂停"
                              : st.Status == "rest" ? "今天休息"
                              : st.Status == "wait" ? "距上班 " + Clock((st.StartTs - now).TotalSeconds)
                              : st.Status == "off" ? "今日已收工"
                              : "距下班 " + Clock(st.RemainSec);
                MiniStatus.Text = statusText;
                MiniStatus.Foreground = _bChipFg;
                MiniDot.Fill = _bChipDot;
                UpdateMiniBar(st.Pct);
            }

            PersistPeriodically(now);
            UpdateTrayTip(now, st, statusText);

            BuildEncourage(st);
        }

        /// <summary>更新迷你挂件底部的进度条。</summary>
        private void UpdateMiniBar(double pct)
        {
            if (MiniViewbox.Visibility != Visibility.Visible) return;
            double w = MiniBarHost2.ActualWidth;
            if (w <= 0) return;
            MiniBarFill.Width = Math.Max(0, w * Math.Max(0, Math.Min(100, pct)) / 100.0);
        }

        private void UpdateBar(double pct = double.NaN)
        {
            if (double.IsNaN(pct)) pct = _lastPct;
            _lastPct = pct;
            double w = BarHost.ActualWidth;
            if (w <= 0) return;
            BarFill.Width = Math.Max(0, w * Math.Max(0, Math.Min(100, pct)) / 100.0);
        }

        private void UpdateMonthBar(double pct)
        {
            double w = MonthBarHost.ActualWidth;
            if (w <= 0) return;
            MonthBarFill.Width = Math.Max(0, w * Math.Max(0, Math.Min(100, pct)) / 100.0);
        }

        private void BuildEncourage(Snapshot st)
        {
            string item = string.IsNullOrWhiteSpace(_cfg.GoalItem) ? "目标" : _cfg.GoalItem;
            var tone = QuoteProvider.ParseTone(_cfg.Tone);

            if (st.PerSec <= 0)
            {
                SetEncourage("先去右上角「设置」里填个月薪吧", null);
                return;
            }
            if (st.Status == "paused")
            {
                SetEncourage("计时器已暂停，开心的时光不用算钱", null);
                return;
            }
            if (st.Status == "rest")
            {
                SetEncourage("今天休息，不用算钱。真要加班，工时也照常记着", null);
                return;
            }
            if (st.Status == "off")
            {
                SetEncourage("今天已经收工，一共赚到 ¥" + N4(st.Earned) + "，去休息吧", "¥" + N4(st.Earned));
                return;
            }
            if (st.Status == "wait")
            {
                SetEncourage("还没到点，距离开工还有 " + HM((st.StartTs - st.Now).TotalSeconds),
                             HM((st.StartTs - st.Now).TotalSeconds));
                return;
            }

            double price = _cfg.Shirt > 0 ? _cfg.Shirt : 0;
            if (price <= 0)
            {
                SetEncourage("设置一个目标物单价，就知道离小目标还有多远了", null);
                return;
            }

            int count = (int)Math.Floor(st.Earned / price);
            double nextNeed = ((count + 1) * price - st.Earned) / st.PerSec;

            QuoteScene scene;
            string timeText;
            string canBuyText = null;

            if (nextNeed <= st.RemainSec)
            {
                scene = count > 0 ? QuoteScene.RepeatItem : QuoteScene.FirstItem;
                timeText = HM(nextNeed);
            }
            else
            {
                scene = QuoteScene.NotEnough;
                timeText = HM(st.RemainSec);
                canBuyText = (st.PerSec * st.RemainSec / price).ToString("0.00", CultureInfo.InvariantCulture);
            }

            string text = PickQuote(scene, () =>
                _quotes.Get(tone, scene, item, timeText, N4(st.Earned), count, canBuyText));
            SetEncourage(text, null);
        }

        /// <summary>语录轮换节流：同一句在 N 秒内不重复，避免每帧乱跳。</summary>
        private readonly Dictionary<QuoteScene, (string text, DateTime at)> _lastQuote = new();

        private string PickQuote(QuoteScene scene, Func<string> factory)
        {
            var now = DateTime.Now;
            if (_lastQuote.TryGetValue(scene, out var last) && (now - last.at).TotalSeconds < 25)
                return last.text;

            string text = factory();
            _lastQuote[scene] = (text, now);
            return text;
        }

        /// <summary>设置鼓励语文本；highlight 非空时会加粗高亮该片段。</summary>
        private void SetEncourage(string text, string highlight)
        {
            if (text == _lastEncText) return;
            _lastEncText = text;

            EncText.Inlines.Clear();
            if (!string.IsNullOrEmpty(highlight) && text.Contains(highlight))
            {
                int i = text.IndexOf(highlight, StringComparison.Ordinal);
                if (i > 0)
                    EncText.Inlines.Add(new Run(text.Substring(0, i)) { Foreground = _bEncText });
                EncText.Inlines.Add(new Run(highlight) { FontWeight = FontWeights.Bold, Foreground = _bAccentDark });
                if (i + highlight.Length < text.Length)
                    EncText.Inlines.Add(new Run(text.Substring(i + highlight.Length)) { Foreground = _bEncText });
            }
            else
            {
                EncText.Inlines.Add(new Run(text) { Foreground = _bEncText });
            }
        }

        // ---------------- 主题 ----------------

        private static Brush MakeBrush(string hex) =>
            (Brush)new BrushConverter().ConvertFromString(hex);

        private void ApplyTheme()
        {
            _theme = Theme.Get(_cfg.Theme);
            _bTextPrimary = MakeBrush(_theme.TextPrimary);
            _bTextSecondary = MakeBrush(_theme.TextSecondary);
            _bTextMuted = MakeBrush(_theme.TextMuted);
            _bAccent = MakeBrush(_theme.Accent);
            _bAccentDark = MakeBrush(_theme.AccentDark);
            _bEncText = MakeBrush(_theme.EncText);

            Shell.Background = MakeBrush(_theme.ShellBg);
            Shell.BorderBrush = MakeBrush(_theme.ShellBorder);
            MiniShell.Background = MakeBrush(_theme.ShellBg);
            MiniShell.BorderBrush = MakeBrush(_theme.ShellBorder);
            TabBar.Background = MakeBrush(_theme.TabBarBg);

            TitleText.Foreground = _bTextPrimary;
            SubTitleText.Foreground = _bTextMuted;
            DateText.Foreground = _bTextSecondary;
            TimeText.Foreground = _bTextPrimary;

            AmountLabel.Foreground = _bTextMuted;
            CountText.Foreground = MakeBrush(_theme.AccentDark);
            CountChip.Background = MakeBrush(_theme.AccentSoft);
            CountChip.BorderBrush = MakeBrush(_theme.AccentSoftBorder);

            SpeedCard.Background = MakeBrush(_theme.CardBg);
            SpeedCard.BorderBrush = MakeBrush(_theme.CardBorder);
            SpeedLabel.Foreground = _bTextMuted;
            RateText.Foreground = _bTextPrimary;
            UnitText.Foreground = _bTextMuted;
            DailyTipText.Foreground = MakeBrush(_theme.TextFaint);

            EncCard.Background = MakeBrush(_theme.EncBg);
            EncCard.BorderBrush = MakeBrush(_theme.EncBorder);

            ProgressLabel.Foreground = _bTextSecondary;
            PctText.Foreground = _bAccent;
            BarHost.Background = MakeBrush(_theme.BarBg);
            WorkedLabel.Foreground = _bTextMuted;
            TotalLabel.Foreground = _bTextMuted;
            RemainLabel.Foreground = _bTextMuted;
            WorkedText.Foreground = _bTextPrimary;
            TotalText.Foreground = _bTextPrimary;
            RemainText.Foreground = _bTextSecondary;
            Divider.Background = MakeBrush(_theme.Divider);

            MonthCard.Background = MakeBrush(_theme.CardBg);
            MonthCard.BorderBrush = MakeBrush(_theme.CardBorder);
            MonthLabel.Foreground = _bTextMuted;
            MonthPctText.Foreground = _bAccent;
            MonthBarHost.Background = MakeBrush(_theme.BarBg);
            MonthNote.Foreground = MakeBrush(_theme.TextFaint);

            HourlyCard.Background = MakeBrush(_theme.CardBg);
            HourlyCard.BorderBrush = MakeBrush(_theme.CardBorder);
            FullCard.Background = MakeBrush(_theme.CardBg);
            FullCard.BorderBrush = MakeBrush(_theme.CardBorder);
            HourlyLabel.Foreground = _bTextMuted;
            FullLabel.Foreground = _bTextMuted;
            HourlyText.Foreground = _bTextPrimary;
            FullText.Foreground = _bTextPrimary;
            HourlyNote.Foreground = MakeBrush(_theme.TextFaint);
            FullNote.Foreground = MakeBrush(_theme.TextFaint);

            PayDayCard.Background = MakeBrush(_theme.CardBg);
            PayDayCard.BorderBrush = MakeBrush(_theme.CardBorder);
            PayDayText.Foreground = _bAccentDark;
            PayDaySubText.Foreground = _bTextMuted;

            ActualWorkedCard.Background = MakeBrush(_theme.CardBg);
            ActualWorkedCard.BorderBrush = MakeBrush(_theme.CardBorder);
            ActualWorkedLabel.Foreground = _bTextMuted;
            ActualWorkedText.Foreground = _bTextPrimary;

            FooterHint.Foreground = MakeBrush(_theme.TextFaint);
            DetailFooterHint.Foreground = MakeBrush(_theme.TextFaint);
            UpdateTabButtons();
            StatWeekCard.Background = MakeBrush(_theme.CardBg);
            StatWeekCard.BorderBrush = MakeBrush(_theme.CardBorder);
            StatMonthCard.Background = MakeBrush(_theme.CardBg);
            StatMonthCard.BorderBrush = MakeBrush(_theme.CardBorder);
            StatYearCard.Background = MakeBrush(_theme.CardBg);
            StatYearCard.BorderBrush = MakeBrush(_theme.CardBorder);
            StatOvertimeCard.Background = MakeBrush(_theme.CardBg);
            StatOvertimeCard.BorderBrush = MakeBrush(_theme.CardBorder);
            StatDetailCard.Background = MakeBrush(_theme.CardBg);
            StatDetailCard.BorderBrush = MakeBrush(_theme.CardBorder);
            StatWeekHours.Foreground = _bTextPrimary;
            StatMonthHours.Foreground = _bTextPrimary;
            StatYearHours.Foreground = _bTextPrimary;
            StatOvertimeHours.Foreground = _bTextPrimary;
            StatWeekEarned.Foreground = _bAccentDark;
            StatMonthEarned.Foreground = _bAccentDark;
            StatYearEarned.Foreground = _bAccentDark;
            StatOvertimeDays.Foreground = _bTextMuted;
            StatDetailText.Foreground = _bTextSecondary;
        }

        private void SetTheme(string name)
        {
            _cfg.Theme = name;
            ApplyTheme();
            SaveCfg();
            UpdateToneButtons();
            UpdateThemeButtons();
            Render();
        }

        private void SetTone(string tone)
        {
            _cfg.Tone = tone;
            SaveCfg();
            UpdateToneButtons();
            Render();
        }

        private void UpdateToneButtons()
        {
            ToneChillBtn.Style = (Style)FindResource(_cfg.Tone == "chill" ? "ToggleActive" : "ToggleIdle");
            ToneSavageBtn.Style = (Style)FindResource(_cfg.Tone == "savage" ? "ToggleActive" : "ToggleIdle");
            ToneHypeBtn.Style = (Style)FindResource(_cfg.Tone == "hype" ? "ToggleActive" : "ToggleIdle");
        }

        private void UpdateThemeButtons()
        {
            ThemeLightBtn.Style = (Style)FindResource(_cfg.Theme == "light" ? "ToggleActive" : "ToggleIdle");
            ThemeDarkBtn.Style = (Style)FindResource(_cfg.Theme == "dark" ? "ToggleActive" : "ToggleIdle");
        }

        // ---------------- 视图模式（完整 / 迷你挂件） ----------------

        private void SetViewMode(AppViewMode mode)
        {
            if (_viewMode == mode && _loaded) return;
            _viewMode = mode;
            _cfg.ViewMode = ViewState.ToText(mode);
            ApplyViewMode();
            SaveCfg();
        }

        /// <summary>应用当前视图模式：切换两个画布的可见性与窗口尺寸。</summary>
        private void ApplyViewMode()
        {
            bool mini = _viewMode == AppViewMode.Mini;

            FullViewbox.Visibility = mini ? Visibility.Collapsed : Visibility.Visible;
            MiniViewbox.Visibility = mini ? Visibility.Visible : Visibility.Collapsed;

            // 最小尺寸必须先放开，否则 WPF 会把 Width/Height 往上夹：
            // 迷你挂件设计尺寸（252×108）比完整模式的最小尺寸小，
            // 不先改 Min 就会被撑成 260×130。
            ApplyMinSize();

            if (mini)
            {
                var sz = ViewState.WindowSize(AppViewMode.Mini, _cfg.MiniWidth, _cfg.MiniHeight,
                                              MainTab.Home, _userResizedMini);
                Width = sz.W;
                Height = sz.H;
                RestoreMiniPosition();
            }
            else
            {
                var sz = ViewState.WindowSize(AppViewMode.Full, _cfg.WindowWidth, _cfg.WindowHeight,
                                              _tab, _userResizedFull);
                Width = sz.W;
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitCanvasHeight));
                RestoreFullPosition();
            }

            UpdateSizeText();
            UpdateViewButtons();
            _tray?.SetMode(mini);
        }

        /// <summary>按当前视图模式设置窗口允许缩到的最小尺寸。</summary>
        private void ApplyMinSize()
        {
            var min = ViewState.MinSize(_viewMode);
            MinWidth = min.W;
            MinHeight = min.H;
        }

        /// <summary>
        /// 窗口矩形是否还有足够面积落在虚拟桌面内。
        ///
        /// 用虚拟桌面（所有显示器合起来的矩形）而不是主屏 WorkArea：
        /// v1.0.0 只比对主屏，摆在副屏上的窗口重启后会被硬拽回主屏。
        /// </summary>
        private static bool IsOnAnyScreen(double left, double top, double w, double h)
        {
            double vl = SystemParameters.VirtualScreenLeft;
            double vt = SystemParameters.VirtualScreenTop;
            double vr = vl + SystemParameters.VirtualScreenWidth;
            double vb = vt + SystemParameters.VirtualScreenHeight;

            double visW = Math.Min(left + w, vr) - Math.Max(left, vl);
            double visH = Math.Min(top + h, vb) - Math.Max(top, vt);
            return visW >= 80 && visH >= 60;
        }

        /// <summary>把窗口拉回主屏右下角（落屏 / 换显示器时的兜底）。</summary>
        private void EnsureWindowVisible()
        {
            double w = ActualWidth > 0 ? ActualWidth : Width;
            double h = ActualHeight > 0 ? ActualHeight : Height;
            if (w <= 0 || h <= 0) return;
            if (IsOnAnyScreen(Left, Top, w, h)) return;

            var wa = SystemParameters.WorkArea;
            Left = Math.Max(wa.Left, wa.Right - w - 24);
            Top = _viewMode == AppViewMode.Mini ? Math.Max(wa.Top, wa.Bottom - h - 24) : wa.Top + 90;
        }

        /// <summary>恢复迷你挂件的位置（与完整模式分开记）。</summary>
        private void RestoreMiniPosition()
        {
            var wa = WorkAreaOf(_cfg.MiniScreen);
            double w = Width, h = Height;
            double left = _cfg.MiniLeft ?? (wa.Right - w - 24);
            double top = _cfg.MiniTop ?? (wa.Bottom - h - 24);
            if (!IsOnAnyScreen(left, top, w, h))
            {
                left = wa.Right - w - 24;
                top = wa.Bottom - h - 24;
            }
            Left = left;
            Top = top;
        }

        /// <summary>恢复完整模式窗口的位置。</summary>
        private void RestoreFullPosition()
        {
            var wa = WorkAreaOf(_cfg.WindowScreen);
            double w = Width, h = Height;
            double left = _cfg.WindowLeft ?? (wa.Right - w - 24);
            double top = _cfg.WindowTop ?? (wa.Top + 90);
            if (!IsOnAnyScreen(left, top, w, h))
            {
                left = wa.Right - w - 24;
                top = wa.Top + 90;
            }
            Left = left;
            Top = top;
        }

        // ---------------- Tab 切换 ----------------

        private void SelectTab(MainTab tab)
        {
            if (_viewMode == AppViewMode.Mini) SetViewMode(AppViewMode.Full);

            _tab = tab;
            _cfg.ActiveTab = ViewState.ToText(tab);

            HomePanel.Visibility = tab == MainTab.Home ? Visibility.Visible : Visibility.Collapsed;
            DetailPanel.Visibility = tab == MainTab.Detail ? Visibility.Visible : Visibility.Collapsed;
            StatsPanel.Visibility = tab == MainTab.Stats ? Visibility.Visible : Visibility.Collapsed;
            SettingsScroll.Visibility = tab == MainTab.Settings ? Visibility.Visible : Visibility.Collapsed;

            if (tab == MainTab.Settings)
            {
                FillSettings(_cfg);
                SettingsTip.Text = "";
                ApplyOpacity();
                UpdateAutoHours();
                UpdatePreview();
                UpdateSizeText();
                UpdateToneButtons();
                UpdateThemeButtons();
                UpdateFeatureButtons();
            }

            if (tab == MainTab.Stats) RefreshStats();

            // 设置页内容很长，限制在固定高度内，超出部分滚动
            SettingsScroll.MaxHeight = tab == MainTab.Settings ? 430 : double.PositiveInfinity;

            UpdateTabButtons();

            // 切换 Tab 时按该页的实际内容高度调整画布与窗口。
            // 内容测量需要布局已完成，所以推迟到下一帧再执行。
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitCanvasHeight));

            SaveCfg();
        }

        /// <summary>
        /// 按当前 Tab 内容的实际高度撑开画布，让内容完整显示且不出现多余滚动条。
        ///
        /// 画布宽度固定 400，内容按这个宽度排版；高度由布局实测得出
        /// （ShellGrid.ActualHeight），不依赖手算常数，以后加内容也不会算错。
        /// 窗口高度跟着画布走（宽度固定时 Viewbox 缩放比为 1:1）。
        /// </summary>
        private void FitCanvasHeight()
        {
            if (_viewMode != AppViewMode.Full) return;

            // 等布局完成后再读实际高度
            if (ShellGrid.ActualHeight <= 0)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitCanvasHeight));
                return;
            }

            // ShellGrid 的上下 margin 是 13 + 12
            double canvasH = Math.Ceiling(ShellGrid.ActualHeight + 25);

            // 设置页内容很长，限制在合理高度，超出部分滚动
            if (_tab == MainTab.Settings)
            {
                double maxH = 430 + 25 + 25 + 30 + 11 + 11;
                if (canvasH > maxH) canvasH = maxH;
            }

            if (Math.Abs(FullCanvas.Height - canvasH) > 0.5)
            {
                FullCanvas.Height = canvasH;
                // 画布变了，布局还没跟上，下一帧再校正
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitCanvasHeight));
                return;
            }

            if (!_userResizedFull)
            {
                double target = canvasH + ViewState.EdgeMargin * 2;
                if (Math.Abs(Height - target) > 0.5) Height = target;
            }
        }

        /// <summary>给选中的 Tab 加高亮底色，其余为无底色。</summary>
        private void UpdateTabButtons()
        {
            var buttons = new[]
            {
                (TabHomeBtn, MainTab.Home),
                (TabDetailBtn, MainTab.Detail),
                (TabStatsBtn, MainTab.Stats),
                (TabSettingsBtn, MainTab.Settings)
            };
            foreach (var (btn, tab) in buttons)
            {
                bool active = tab == _tab;
                btn.Background = active ? MakeBrush(_theme.TabActiveBg) : Brushes.Transparent;
                btn.BorderBrush = active ? MakeBrush(_theme.TabActiveBorder) : Brushes.Transparent;
                btn.Foreground = active ? MakeBrush(_theme.TabActiveFg) : MakeBrush(_theme.TabIdleFg);
            }
        }

        /// <summary>更新"迷你挂件"按钮的选中态。</summary>
        private void UpdateViewButtons()
        {
            bool mini = _viewMode == AppViewMode.Mini;
            MiniToggleBtn.Style = (Style)FindResource(mini ? "ToggleActive" : "ToggleIdle");
            MiniToggleBtn.Content = mini ? "完整界面" : "迷你挂件";
        }

        // ---------------- 鼠标穿透 ----------------

        private void ToggleClickThrough()
        {
            _cfg.ClickThrough = !_cfg.ClickThrough;
            ApplyClickThrough();
            SaveCfg();

            if (_cfg.ClickThrough)
            {
                // 第一次开启必须把怎么关掉说清楚，否则就是把人锁在门外
                if (!_clickThroughHinted)
                {
                    _clickThroughHinted = true;
                    _tray?.ShowBalloon("鼠标穿透已开启",
                        "内容区点击会穿到下层，顶部标题栏仍可操作。要关掉：点标题栏的穿透按钮、按 Ctrl+Alt+X、或右键托盘图标。");
                }
            }
            else if (!IsVisible)
            {
                // 用热键关掉穿透时窗口可能藏着，直接弹出来给个反馈
                ShowFromTray();
            }
        }

        /// <summary>把配置里的穿透状态应用到窗口，并同步按钮 / 托盘菜单文案。</summary>
        private void ApplyClickThrough()
        {
            if (!Win32.SetClickThrough(this, _cfg.ClickThrough) && _cfg.ClickThrough)
                Log.Info("鼠标穿透延后应用：窗口句柄尚未创建");

            UpdateClickThroughButton();
            _tray?.SetClickThrough(_cfg.ClickThrough);
        }

        /// <summary>穿透开关的选中态。v1.0.0 完全没同步，用户看不出开没开。</summary>
        private void UpdateClickThroughButton()
        {
            if (ClickThroughToggleBtn == null) return;
            ClickThroughToggleBtn.Style = (Style)FindResource(_cfg.ClickThrough ? "ToggleActive" : "ToggleIdle");
        }

        /// <summary>
        /// 把窗口尺寸、位置、缩放标记全部恢复默认，并顺手关掉穿透。
        /// 专门救"被拖得极小 / 拖到屏幕外，已经没法操作"的事故现场。
        /// </summary>
        private void ResetWindowLayout()
        {
            ShowFromTray();

            _userResizedFull = false;
            _userResizedMini = false;
            _cfg.FullResized = false;
            _cfg.MiniResized = false;
            _cfg.WindowWidth = null;
            _cfg.WindowHeight = null;
            _cfg.MiniWidth = null;
            _cfg.MiniHeight = null;
            _cfg.WindowLeft = null;
            _cfg.WindowTop = null;
            _cfg.MiniLeft = null;
            _cfg.MiniTop = null;

            if (_cfg.ClickThrough)
            {
                _cfg.ClickThrough = false;
                ApplyClickThrough();
            }

            ApplyMinSize();
            _viewMode = AppViewMode.Full;
            _cfg.ViewMode = ViewState.ToText(AppViewMode.Full);
            ApplyViewMode();   // 内部会 RestoreFullPosition + FitCanvasHeight

            SaveCfg();
            Render();
            Log.Info("已通过托盘重置窗口大小与位置");
        }

        // ---------------- 幽灵模式 / 边缘吸附 ----------------

        /// <summary>幽灵模式：鼠标离开窗口时淡下去，移回来恢复。</summary>
        private void ToggleGhostMode()
        {
            _cfg.GhostMode = !_cfg.GhostMode;
            SaveCfg();
            UpdateFeatureButtons();

            if (_cfg.GhostMode) SetGhostFaded(!IsMouseOver);   // 立刻按当前位置生效
            else Opacity = NormalOpacity();
        }

        /// <summary>把窗口切到"淡出"或"正常"。只在幽灵模式开启、且没开穿透时生效。</summary>
        private void SetGhostFaded(bool faded)
        {
            if (!_cfg.GhostMode) return;
            if (_cfg.ClickThrough) return;   // 穿透后收不到鼠标事件，淡出会一直卡在淡的状态

            double normal = NormalOpacity();
            double target = faded ? Math.Min(normal, Math.Max(0.12, _cfg.GhostOpacity)) : normal;
            if (Math.Abs(Opacity - target) > 0.001) Opacity = target;
        }

        /// <summary>用户设定的正常不透明度（夹到 0.3~1，与滑块下限一致）。</summary>
        private double NormalOpacity()
        {
            double op = _cfg.Opacity ?? 0.97;
            if (op < 0.3) op = 0.3;
            if (op > 1) op = 1;
            return op;
        }

        /// <summary>边缘吸附开关：只对迷你挂件生效。</summary>
        private void ToggleSnapEdge()
        {
            _cfg.SnapToEdge = !_cfg.SnapToEdge;
            SaveCfg();
            UpdateFeatureButtons();
            if (_cfg.SnapToEdge) SnapMiniToEdge();
        }

        /// <summary>
        /// 迷你挂件松手后，离屏幕边缘够近就贴上去。
        /// 用主屏工作区判断，副屏窗口直接跳过（避免坐标系统混淆）。
        /// </summary>
        private void SnapMiniToEdge()
        {
            if (!_cfg.SnapToEdge || _viewMode != AppViewMode.Mini) return;

            var wa = SystemParameters.WorkArea;
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            // 只处理完全落在主屏工作区里的窗口
            if (Left < wa.Left - 1 || Top < wa.Top - 1 ||
                Left + w > wa.Right + 1 || Top + h > wa.Bottom + 1) return;

            const double threshold = 28;   // 离边缘多近算"想贴边"
            const double gap = 6;          // 贴上后留的缝

            double left = Left, top = Top;

            if (left - wa.Left < threshold) left = wa.Left + gap;
            else if (wa.Right - (left + w) < threshold) left = wa.Right - w - gap;

            if (top - wa.Top < threshold) top = wa.Top + gap;
            else if (wa.Bottom - (top + h) < threshold) top = wa.Bottom - h - gap;

            if (Math.Abs(left - Left) > 0.5 || Math.Abs(top - Top) > 0.5)
            {
                Left = left;
                Top = top;
            }
        }

        /// <summary>同步设置页两个开关的选中态。</summary>
        private void UpdateFeatureButtons()
        {
            if (GhostModeBtn != null)
                GhostModeBtn.Style = (Style)FindResource(_cfg.GhostMode ? "ToggleActive" : "ToggleIdle");
            if (SnapEdgeBtn != null)
                SnapEdgeBtn.Style = (Style)FindResource(_cfg.SnapToEdge ? "ToggleActive" : "ToggleIdle");
        }

        // ---------------- 置顶 / 透明度 / 位置 ----------------

        private void ApplyTopmost()
        {
            Topmost = _cfg.Topmost;
            PinBtn.Foreground = Topmost ? MakeBrush("#F97316") : MakeBrush("#C3CAD6");
        }

        private void OnPinClick(object sender, RoutedEventArgs e)
        {
            _cfg.Topmost = !_cfg.Topmost;
            ApplyTopmost();
            SaveCfg();
        }

        private void ApplyOpacity()
        {
            double op = _cfg.Opacity ?? 0.97;
            if (op < 0.3) op = 0.3;
            if (op > 1) op = 1;
            Opacity = op;
            if (OpacitySlider != null)
            {
                OpacitySlider.Value = op;
                OpacityValueText.Text = Math.Round(op * 100).ToString() + "%";
            }
        }

        private void SetSize(double w, double h)
        {
            Width = w;
            Height = h;
            _userResizedFull = true;
            _cfg.FullResized = true;
            UpdateSizeText();
        }

        /// <summary>从配置恢复视图模式与 Tab（启动时调用）。</summary>
        private void RestoreViewState()
        {
            _viewMode = ViewState.ParseViewMode(_cfg.ViewMode);
            _tab = ViewState.ParseTab(_cfg.ActiveTab);

            HomePanel.Visibility = _tab == MainTab.Home ? Visibility.Visible : Visibility.Collapsed;
            DetailPanel.Visibility = _tab == MainTab.Detail ? Visibility.Visible : Visibility.Collapsed;
            StatsPanel.Visibility = _tab == MainTab.Stats ? Visibility.Visible : Visibility.Collapsed;
            SettingsScroll.Visibility = _tab == MainTab.Settings ? Visibility.Visible : Visibility.Collapsed;

            ApplyMinSize();

            if (_viewMode == AppViewMode.Mini)
            {
                var sz = ViewState.WindowSize(AppViewMode.Mini, _cfg.MiniWidth, _cfg.MiniHeight,
                                              MainTab.Home, _userResizedMini);
                Width = sz.W;
                Height = sz.H;
            }
            else
            {
                var sz = ViewState.WindowSize(AppViewMode.Full, _cfg.WindowWidth, _cfg.WindowHeight,
                                              _tab, _userResizedFull);
                Width = sz.W;
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitCanvasHeight));
            }
        }

        private void UpdateSizeText()
        {
            if (SizeText == null) return;
            if (_viewMode == AppViewMode.Mini)
            {
                SizeText.Text = "迷你挂件 · 双击展开完整界面";
                return;
            }
            SizeText.Text = Math.Round(ActualWidth) + " × " + Math.Round(ActualHeight) +
                            "（拖动窗体边框可缩放）";
        }

        private void OnDragArea(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            try { DragMove(); } catch { }
            SnapMiniToEdge();   // 迷你挂件松手后贴边（开关打开时才生效）

            if (_viewMode == AppViewMode.Mini)
            {
                _cfg.MiniLeft = Left;
                _cfg.MiniTop = Top;
            }
            else
            {
                _cfg.WindowLeft = Left;
                _cfg.WindowTop = Top;
            }
            SaveCfg();
        }

        /// <summary>迷你挂件上双击展开完整界面（单击仍用于拖动窗口）。</summary>
        private void OnMiniShellMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2 && e.ChangedButton == MouseButton.Left)
            {
                e.Handled = true;
                SetViewMode(AppViewMode.Full);
            }
        }

        // ---------------- 设置 ----------------

        private void CloseSettings() => SelectTab(MainTab.Home);

        public void ShowSettingsForDebug() => SelectTab(MainTab.Settings);

        private void FillSettings(AppConfig c)
        {
            InSalary.Text = (c.MonthlySalary ?? 6000).ToString("0.##", CultureInfo.InvariantCulture);
            InStart.Text = c.Start;
            InEnd.Text = c.End;
            // 多段午休全部回填（v1.0.0 只取第一段，保存时会把其余段覆盖掉）
            InBreak.Text = c.Breaks != null ? string.Join(", ", c.Breaks) : "";
            InExtraHolidays.Text = c.ExtraHolidays != null ? string.Join(", ", c.ExtraHolidays) : "";
            InExtraWorkdays.Text = c.ExtraWorkdays != null ? string.Join(", ", c.ExtraWorkdays) : "";
            InShirt.Text = c.Shirt.ToString("0.##", CultureInfo.InvariantCulture);
            InGoalItem.Text = c.GoalItem;
            InPayDay.Text = c.PayDay.ToString(CultureInfo.InvariantCulture);
            UpdateCalendarHint();

            var map = new Dictionary<int, ToggleButton>
            {
                { 0, Dw0 }, { 1, Dw1 }, { 2, Dw2 }, { 3, Dw3 },
                { 4, Dw4 }, { 5, Dw5 }, { 6, Dw6 }
            };
            foreach (var kv in map)
                kv.Value.IsChecked = Array.IndexOf(c.Weekdays, kv.Key) >= 0;
        }

        /// <summary>把逗号 / 分号 / 空格分隔的输入拆成条目。</summary>
        private static IEnumerable<string> SplitList(string s) =>
            (s ?? "")
                .Split(new[] { ',', '，', ';', '；', '\n', '\r', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0);

        /// <summary>解析日期列表，统一存成 yyyy-MM-dd；失败时通过 bad 返回第一个坏值。</summary>
        private static bool TryParseDateList(string s, out List<string> dates, out string bad)
        {
            dates = new List<string>();
            bad = "";
            foreach (var item in SplitList(s))
            {
                if (!DateTime.TryParse(item, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) &&
                    !DateTime.TryParse(item, CultureInfo.CurrentCulture, DateTimeStyles.None, out d))
                {
                    bad = item;
                    return false;
                }
                string key = d.ToString("yyyy-MM-dd");
                if (!dates.Contains(key)) dates.Add(key);
            }
            return true;
        }

        /// <summary>当前年度没有内置节假日数据时，提示用户补充自定义日期。</summary>
        private void UpdateCalendarHint()
        {
            if (CalendarHintText == null) return;
            int y = DateTime.Now.Year;
            CalendarHintText.Text = PayCalendar.HasBuiltInData(y)
                ? ""
                : "注意：" + y + " 年暂无内置节假日数据，法定假日会被当成工作日算进月累计，请在上方补充自定义放假日。";
        }

        private static double ParseNum(string s)
        {
            if (double.TryParse((s ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double v)) return v;
            if (double.TryParse((s ?? "").Trim(), NumberStyles.Any, CultureInfo.CurrentCulture, out v)) return v;
            return double.NaN;
        }

        private void UpdateAutoHours()
        {
            if (AutoHoursText == null) return;
            double? h = PreviewWorkHours();
            if (h.HasValue && h.Value > 0)
            {
                string brk = string.IsNullOrWhiteSpace(InBreak.Text) ? "无午休" : ("午休 " + InBreak.Text.Trim());
                AutoHoursText.Text = "每日计薪工时 " + h.Value.ToString("0.##", CultureInfo.InvariantCulture) +
                                     " 小时（" + InStart.Text.Trim() + " → " + InEnd.Text.Trim() + "，" + brk + "）";
            }
            else AutoHoursText.Text = "上下班时间还没填好，工时按 9 小时预估";
        }

        private double? PreviewWorkHours()
        {
            if (!WorkSchedule.TryParseMinutes(InStart.Text, out _)) return null;
            if (!WorkSchedule.TryParseMinutes(InEnd.Text, out _)) return null;
            var sch = new WorkSchedule { Start = InStart.Text.Trim(), End = InEnd.Text.Trim() };
            var breaks = new List<string>();
            foreach (var seg in SplitList(InBreak.Text))
            {
                var p = seg.Split('-');
                if (p.Length == 2 && WorkSchedule.TryParseMinutes(p[0], out _) && WorkSchedule.TryParseMinutes(p[1], out _))
                    breaks.Add(seg);
            }
            sch.Breaks = breaks;
            double sec = sch.GetTotalWorkSeconds(DateTime.Today);
            return sec > 0 ? sec / 3600.0 : (double?)null;
        }

        private void UpdatePreview()
        {
            if (PrevText == null) return;
            double monthly = ParseNum(InSalary.Text);
            double hours = PreviewWorkHours() ?? 9.0;
            if (double.IsNaN(monthly) || monthly < 0 || hours <= 0)
            {
                PrevText.Text = "输入还不太对，检查一下月薪与上下班时间";
                return;
            }
            double daily = monthly / SalaryCalculator.PayDaysPerMonth;
            double hourly = daily / hours;
            double perSec = daily / (hours * 3600.0);
            PrevText.Text = "日薪 ¥" + N4(daily) + "　·　时薪 ¥" + N4(hourly) +
                            "\n每秒 ¥" + N4(perSec) + "　·　今天满勤 ¥" + N4(daily);
        }

        private void SaveSettings()
        {
            double monthly = ParseNum(InSalary.Text);
            if (double.IsNaN(monthly) || monthly < 0) { SettingsTip.Text = "月薪请填一个非负数字"; return; }
            if (!WorkSchedule.TryParseMinutes(InStart.Text, out int sh)) { SettingsTip.Text = "上班时间格式不对，例如 09:00"; return; }
            if (!WorkSchedule.TryParseMinutes(InEnd.Text, out int eh)) { SettingsTip.Text = "下班时间格式不对，例如 18:00"; return; }
            if (sh == eh) { SettingsTip.Text = "上班时间和下班时间不能相同"; return; }

            var breaks = new List<string>();
            foreach (var seg in SplitList(InBreak.Text))
            {
                var p = seg.Split('-');
                if (p.Length != 2 || !WorkSchedule.TryParseMinutes(p[0], out _) || !WorkSchedule.TryParseMinutes(p[1], out _))
                { SettingsTip.Text = "午休格式不对，例如 12:00-13:00（多段用逗号隔开）"; return; }
                breaks.Add(p[0].Trim() + "-" + p[1].Trim());
            }

            if (!TryParseDateList(InExtraHolidays.Text, out var holidays, out string badHoliday))
            { SettingsTip.Text = "自定义放假日格式不对：" + badHoliday + "（例 2027-01-01）"; return; }
            if (!TryParseDateList(InExtraWorkdays.Text, out var workdays, out string badWorkday))
            { SettingsTip.Text = "自定义调休上班日格式不对：" + badWorkday + "（例 2027-01-04）"; return; }

            double shirt = ParseNum(InShirt.Text);
            if (double.IsNaN(shirt) || shirt <= 0) { SettingsTip.Text = "目标物单价需大于 0"; return; }

            double payDayNum = ParseNum(InPayDay.Text);
            if (double.IsNaN(payDayNum) || payDayNum < 1 || payDayNum > 31) { SettingsTip.Text = "发薪日需在 1-31 之间"; return; }
            int payDay = (int)payDayNum;

            var days = new List<int>();
            var map = new Dictionary<int, ToggleButton>
            {
                { 0, Dw0 }, { 1, Dw1 }, { 2, Dw2 }, { 3, Dw3 },
                { 4, Dw4 }, { 5, Dw5 }, { 6, Dw6 }
            };
            foreach (var kv in map) if (kv.Value.IsChecked == true) days.Add(kv.Key);
            if (days.Count == 0) { SettingsTip.Text = "至少选择一个计薪日"; return; }
            days.Sort();

            _cfg.MonthlySalary = monthly;
            _cfg.Start = WorkSchedule.FormatMinutes(sh);
            _cfg.End = WorkSchedule.FormatMinutes(eh);
            _cfg.Breaks = breaks.ToArray();
            _cfg.ExtraHolidays = holidays.ToArray();
            _cfg.ExtraWorkdays = workdays.ToArray();
            _cfg.Shirt = shirt;
            _cfg.GoalItem = string.IsNullOrWhiteSpace(InGoalItem.Text) ? "T恤" : InGoalItem.Text.Trim();
            _cfg.PayDay = payDay;
            _cfg.Weekdays = days.ToArray();
            _cfg.Opacity = Opacity;
            _cfg.WindowWidth = ActualWidth;
            _cfg.WindowHeight = ActualHeight;

            RebuildCalc();
            SaveCfg();
            CloseSettings();
            Render();
        }

        // ---------------- 导入导出 ----------------

        /// <summary>最近一次导出的文件路径，供"复制路径 / 打开文件夹"使用。</summary>
        private string _lastExportPath = "";

        /// <summary>进备份包的文件清单。</summary>
        private static readonly string[] BackupFiles = { "settings.json", "history.json", "quotes.json" };

        // ---------------- 版本更新 ----------------

        /// <summary>手动检查更新：失败给原因，有新版本直接打开下载页。</summary>
        private async void CheckUpdate()
        {
            CheckUpdateBtn.IsEnabled = false;
            SettingsTip.Text = "正在检查更新…";
            try
            {
                var (tag, url, error) = await UpdateChecker.GetLatestAsync();
                if (tag == null)
                {
                    SettingsTip.Text = "检查失败：" + (error ?? "未知原因")
                        + "\n可以手动打开：" + UpdateChecker.RepoUrl;
                    _lastExportPath = UpdateChecker.RepoUrl;
                    return;
                }

                if (UpdateChecker.IsNewer(tag, UpdateChecker.CurrentVersion))
                {
                    SettingsTip.Text = "发现新版本 " + tag + "（当前 v" + UpdateChecker.CurrentVersion + "）\n" + url;
                    _lastExportPath = url;
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                else
                {
                    SettingsTip.Text = "已是最新版本 v" + UpdateChecker.CurrentVersion;
                }
            }
            catch (Exception ex)
            {
                Log.Error("检查更新失败", ex);
                SettingsTip.Text = "检查失败：" + ex.Message;
            }
            finally
            {
                CheckUpdateBtn.IsEnabled = true;
            }
        }

        /// <summary>启动时静默查一次，有新版本才提示，失败完全不打扰。</summary>
        private async System.Threading.Tasks.Task CheckUpdateSilentlyAsync()
        {
            try
            {
                var (tag, _, _) = await UpdateChecker.GetLatestAsync(6);
                if (tag == null) return;

                if (UpdateChecker.IsNewer(tag, UpdateChecker.CurrentVersion))
                {
                    Log.Info("发现新版本 " + tag);
                    _tray?.ShowBalloon("上班鼓励器有新版 " + tag,
                        "当前 v" + UpdateChecker.CurrentVersion + "。右键托盘图标 → 打开设置 → 检查更新。");
                }
            }
            catch { /* 静默 */ }
        }

        // ---------------- 语录（CSV，Excel 可直接编辑） ----------------

        /// <summary>
        /// 语录文件有变化就重新加载。
        /// 用户用 Excel 改完保存，界面最多 5 秒后自动生效，不用重启。
        /// </summary>
        private void LoadQuotesIfChanged(bool force = false)
        {
            try
            {
                if (string.IsNullOrEmpty(_quotePath)) _quotePath = Path.Combine(DataDir, "quotes.csv");
                if (!File.Exists(_quotePath)) return;

                var stamp = File.GetLastWriteTimeUtc(_quotePath);
                if (!force && stamp == _quoteStamp) return;
                _quoteStamp = stamp;

                QuoteStore.SetCustom(File.ReadAllText(_quotePath, Encoding.UTF8));
                Log.Info("语录已加载：自定义 " + QuoteStore.CustomCount + " 条");
            }
            catch (Exception ex) { Log.Error("加载语录失败", ex); }
        }

        /// <summary>语录文件不存在时，把当前生效的全部语录写成一份模板。</summary>
        private void EnsureQuotesFile()
        {
            try
            {
                if (File.Exists(_quotePath)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(_quotePath));
                File.WriteAllText(_quotePath, QuoteStore.BuildCsv(), new UTF8Encoding(true));
                _quoteStamp = File.GetLastWriteTimeUtc(_quotePath);
            }
            catch (Exception ex) { Log.Error("生成语录模板失败", ex); }
        }

        /// <summary>
        /// 用系统默认程序打开语录 CSV（Excel / WPS 都行）。
        /// 存的是「语气,情境,语录」三列，改完保存即可热加载。
        /// </summary>
        private void EditQuotes()
        {
            try
            {
                EnsureQuotesFile();
                _lastExportPath = _quotePath;   // 让下方的"复制路径 / 打开文件夹"也能用

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _quotePath,
                    UseShellExecute = true
                });

                SettingsTip.Text = "已打开语录文件，共 " + QuoteStore.CustomCount + " 条自定义：\n" + _quotePath
                    + "\n格式：语气,情境,语录（佛系/毒舌/热血 × 首件/多件/买不够）。改完保存，5 秒内自动生效。"
                    + "\n保存时请选「CSV UTF-8」，否则中文会乱码。";
            }
            catch (Exception ex)
            {
                Log.Error("打开语录文件失败", ex);
                SettingsTip.Text = "打开失败：" + ex.Message + "\n文件路径：" + _quotePath;
            }
        }

        /// <summary>
        /// 导入外部工时文件（CSV/TXT）。
        ///
        /// 语义是"按日期增量合并"：文件里有的日期就更新，没有的日期一条不动。
        /// 同一天导入两遍结果完全一样（幂等），所以可以放心重复点。
        /// 打开对话框时会直接定位到上次导入的目录。
        /// </summary>
        private void ImportWorkLog()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "导入工时数据（CSV / TXT）",
                    Filter = "工时数据 (*.csv;*.txt)|*.csv;*.txt|所有文件 (*.*)|*.*",
                    InitialDirectory = Directory.Exists(_cfg.LastImportDir) ? _cfg.LastImportDir : DataDir
                };
                if (dlg.ShowDialog(this) != true) return;

                // 记住目录，下次直接到这儿
                _cfg.LastImportDir = Path.GetDirectoryName(dlg.FileName) ?? "";
                SaveCfg();

                var errors = new List<string>();
                var rows = WorkLogImporter.Parse(File.ReadAllText(dlg.FileName), errors);
                if (rows.Count == 0)
                {
                    SettingsTip.Text = "没解析出可导入的行。最少需要「日期 + 工时」两列。"
                        + (errors.Count > 0 ? "\n" + string.Join("\n", errors.Take(5)) : "");
                    return;
                }

                // 金额统一按当前月薪口径重算，避免和程序自己算的日期对不上
                double perSec = _calc.Compute(DateTime.Now, _cfg.MonthlySalary ?? 0, _cfg.Weekdays,
                                              _cfg.PayDay, false, 0).PerSec;

                int added = 0, updated = 0;
                foreach (var d in rows)
                {
                    bool existed = _tracker.HasDate(d.Date);
                    var rec = _tracker.GetOrCreate(d.Date);

                    if (d.WorkedHours.HasValue) rec.WorkedSec = d.WorkedHours.Value * 3600.0;
                    if (!string.IsNullOrEmpty(d.FirstClockIn)) rec.FirstClockIn = d.FirstClockIn;
                    if (!string.IsNullOrEmpty(d.LastClockOut)) rec.LastClockOut = d.LastClockOut;
                    if (d.OvertimeHours.HasValue) rec.OvertimeSec = d.OvertimeHours.Value * 3600.0;
                    if (!string.IsNullOrEmpty(d.Note)) rec.DayNote = d.Note;

                    rec.Earned = perSec * rec.WorkedSec;

                    if (DateTime.TryParse(d.Date, out var day))
                    {
                        if (rec.ScheduledSec <= 0) rec.ScheduledSec = _schedule.GetTotalWorkSeconds(day);
                        rec.IsPayDay = _calendar.IsPayDay(day, _cfg.Weekdays);
                    }

                    if (existed) updated++; else added++;
                }

                _tracker.Save(HistoryPath);
                if (_tab == MainTab.Stats) RefreshStats();
                Render();

                SettingsTip.Text = "导入完成：新增 " + added + " 天，更新 " + updated + " 天。"
                    + (errors.Count > 0 ? "\n有 " + errors.Count + " 行没读进来：" + errors[0] : "");
                Log.Info("导入工时 " + dlg.FileName + " 新增 " + added + " 更新 " + updated);
            }
            catch (Exception ex)
            {
                Log.Error("导入工时失败", ex);
                SettingsTip.Text = "导入失败：" + ex.Message;
            }
        }

        /// <summary>把配置、工时历史、语录打成一个 zip，方便换机或存档。</summary>
        private void BackupAll()
        {
            try
            {
                _tracker.Save(HistoryPath);
                ConfigStore.Save(CfgPath, _cfg);

                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "备份全部数据",
                    Filter = "备份包 (*.zip)|*.zip",
                    FileName = "WorkEncourager-备份-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".zip",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                };
                if (dlg.ShowDialog(this) != true) return;

                var files = BackupFiles
                    .Select(n => Path.Combine(DataDir, n))
                    .Where(File.Exists)
                    .ToList();
                if (files.Count == 0) { SettingsTip.Text = "没有可备份的数据"; return; }

                if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
                using (var zip = System.IO.Compression.ZipFile.Open(dlg.FileName,
                           System.IO.Compression.ZipArchiveMode.Create))
                {
                    foreach (var f in files) zip.CreateEntryFromFile(f, Path.GetFileName(f));
                }

                _lastExportPath = dlg.FileName;
                SettingsTip.Text = "已备份 " + files.Count + " 个文件到：\n" + dlg.FileName;
                Log.Info("备份完成: " + dlg.FileName);
            }
            catch (Exception ex)
            {
                Log.Error("备份失败", ex);
                SettingsTip.Text = "备份失败：" + ex.Message;
            }
        }

        /// <summary>
        /// 从 zip 恢复。当前数据会先原样留在 before-restore-时间戳 目录里，
        /// 恢复错了还能退回去。
        /// </summary>
        private void RestoreAll()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择备份包",
                    Filter = "备份包 (*.zip)|*.zip",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                };
                if (dlg.ShowDialog(this) != true) return;

                string safety = Path.Combine(DataDir, "before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(safety);
                foreach (var name in BackupFiles)
                {
                    var p = Path.Combine(DataDir, name);
                    if (File.Exists(p)) File.Copy(p, Path.Combine(safety, name), true);
                }

                int restored = 0;
                using (var zip = System.IO.Compression.ZipFile.OpenRead(dlg.FileName))
                {
                    foreach (var entry in zip.Entries)
                    {
                        // 只认白名单里的三个文件，防止压缩包里塞别的东西
                        if (Array.IndexOf(BackupFiles, entry.Name) < 0) continue;
                        entry.ExtractToFile(Path.Combine(DataDir, entry.Name), true);
                        restored++;
                    }
                }
                if (restored == 0) { SettingsTip.Text = "这个包里没有可恢复的数据"; return; }

                LoadCfg();
                ApplyTheme();
                ApplyTopmost();
                ApplyOpacity();
                ApplyClickThrough();
                FillSettings(_cfg);
                UpdateFeatureButtons();
                UpdateToneButtons();
                UpdateThemeButtons();
                UpdateAutoHours();
                UpdatePreview();
                Render();

                SettingsTip.Text = "已恢复 " + restored + " 个文件。恢复前的数据留在：\n" + safety;
                Log.Info("从备份恢复: " + dlg.FileName);
            }
            catch (Exception ex)
            {
                Log.Error("恢复失败", ex);
                SettingsTip.Text = "恢复失败：" + ex.Message;
            }
        }

        private void ExportConfig()
        {
            try
            {
                var path = Path.Combine(DataDir, "workenc-config-" +
                    DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                File.WriteAllText(path, ConfigStore.ExportJson(_cfg), new System.Text.UTF8Encoding(true));
                _lastExportPath = path;
                SettingsTip.Text = "配置已导出，路径可复制：\n" + path;
            }
            catch (Exception ex) { SettingsTip.Text = "导出失败：" + ex.Message; }
        }

        private void ImportConfig()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择要导入的配置文件",
                    Filter = "配置文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                    InitialDirectory = DataDir
                };
                if (dlg.ShowDialog(this) != true) return;
                var cfg = ConfigStore.ImportJson(File.ReadAllText(dlg.FileName));
                if (cfg == null) { SettingsTip.Text = "配置文件格式不对，导入失败"; return; }
                _cfg = cfg;
                RebuildCalc();

                // 导入的每一项都要真正生效（v1.0.0 漏了置顶、穿透与视图模式）
                ApplyTheme();
                ApplyTopmost();
                ApplyOpacity();
                ApplyClickThrough();
                ApplyMinSize();
                SetViewMode(ViewState.ParseViewMode(_cfg.ViewMode));

                FillSettings(_cfg);
                SelectTab(ViewState.ParseTab(_cfg.ActiveTab));
                UpdateToneButtons();
                UpdateThemeButtons();
                UpdateAutoHours();
                UpdatePreview();

                SaveCfg();
                SettingsTip.Text = "配置已导入并保存";
                Render();
            }
            catch (Exception ex) { SettingsTip.Text = "导入失败：" + ex.Message; }
        }

        private void ExportCsv()
        {
            try
            {
                _tracker.Save(HistoryPath);
                var path = Path.Combine(DataDir, "工时报表-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
                CsvExporter.WriteFile(path, CsvExporter.BuildWorkReport(_tracker.Records, _calendar, _cfg.Weekdays));
                _lastExportPath = path;
                ShowTip(_tab == MainTab.Stats ? StatsTip : SettingsTip,
                        "工时报表已导出，路径可复制：\n" + path);
            }
            catch (Exception ex) { ShowTip(SettingsTip, "导出失败：" + ex.Message); }
        }

        private void ExportMonthlyCsv()
        {
            try
            {
                _tracker.Save(HistoryPath);
                var path = Path.Combine(DataDir, "月度汇总-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
                CsvExporter.WriteFile(path, CsvExporter.BuildMonthlyReport(_tracker.Records, _calendar, _cfg.Weekdays));
                _lastExportPath = path;
                ShowTip(StatsTip, "月度汇总已导出，路径可复制：\n" + path);
            }
            catch (Exception ex) { ShowTip(StatsTip, "导出失败：" + ex.Message); }
        }

        /// <summary>把最近一次导出的路径复制到剪贴板。</summary>
        private void CopyLastPath(TextBox tip)
        {
            if (string.IsNullOrEmpty(_lastExportPath))
            {
                ShowTip(tip, "还没有导出过文件，先点一次导出。");
                return;
            }
            try
            {
                Clipboard.SetText(_lastExportPath);
                ShowTip(tip, "路径已复制到剪贴板：\n" + _lastExportPath);
            }
            catch (Exception ex) { ShowTip(tip, "复制失败：" + ex.Message); }
        }

        /// <summary>在资源管理器中定位最近一次导出的文件。</summary>
        private void OpenLastFolder(TextBox tip)
        {
            if (string.IsNullOrEmpty(_lastExportPath))
            {
                ShowTip(tip, "还没有导出过文件，先点一次导出。");
                return;
            }
            try
            {
                string dir = Path.GetDirectoryName(_lastExportPath) ?? DataDir;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + _lastExportPath + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex) { ShowTip(tip, "打开失败：" + ex.Message); }
        }

        private static void ShowTip(TextBox target, string text)
        {
            if (target != null) target.Text = text;
        }

        // ---------------- 历史统计 ----------------

        public void ShowStatsForDebug() => SelectTab(MainTab.Stats);

        public void ShowDetailForDebug() => SelectTab(MainTab.Detail);

        public void ShowMiniForDebug() => SetViewMode(AppViewMode.Mini);

        /// <summary>调试用：以暂停状态启动，且退出时不把暂停状态写回配置。</summary>
        public void ShowPausedForDebug()
        {
            _paused = true;
            _pauseAt = DateTime.Now;
            _debugNoPersist = true;
            Render();
        }

        private void RefreshStats()
        {
            var analyzer = new HistoryAnalyzer(_calendar);
            var now = DateTime.Now;
            var s = analyzer.Analyze(_tracker.Records, now, _cfg.Weekdays);

            StatWeekHours.Text = (s.Week.TotalWorkedSec / 3600.0).ToString("0.00", CultureInfo.InvariantCulture);
            StatWeekEarned.Text = "¥" + N4(s.Week.TotalEarned);
            StatMonthHours.Text = (s.Month.TotalWorkedSec / 3600.0).ToString("0.00", CultureInfo.InvariantCulture);
            StatMonthEarned.Text = "¥" + N4(s.Month.TotalEarned);
            StatYearHours.Text = (s.Year.TotalWorkedSec / 3600.0).ToString("0.00", CultureInfo.InvariantCulture);
            StatYearEarned.Text = "¥" + N4(s.Year.TotalEarned);
            StatOvertimeHours.Text = (s.All.TotalOvertimeSec / 3600.0).ToString("0.00", CultureInfo.InvariantCulture);

            int otDays = _tracker.Records.Count(r => r.OvertimeSec > 60);
            StatOvertimeDays.Text = otDays + " 天有加班";

            var sb = new List<string>();
            sb.Add("本月出勤 " + s.Month.DayCount + " 天 / 应出勤 " + s.MonthPayDays + " 个计薪日");
            sb.Add("日均工时 " + (s.Month.DayCount > 0 ? (s.Month.AvgWorkedSec / 3600.0).ToString("0.00") : "0.00") + " 小时");
            if (!string.IsNullOrEmpty(s.All.BestDayDate))
                sb.Add("最赚的一天 " + s.All.BestDayDate + "（¥" + N4(s.All.BestDayEarned) +
                       "，" + (s.All.BestDayWorkedSec / 3600.0).ToString("0.00") + " 小时）");
            sb.Add("累计工作 " + (s.All.TotalWorkedSec / 3600.0).ToString("0.00") + " 小时，共 " + s.All.DayCount + " 天");
            StatDetailText.Text = string.Join("\n", sb);
        }

        // ---------------- 调试截图 ----------------

        public void SaveShot(string path)
        {
            double w = Math.Max(1, ActualWidth);
            double h = Math.Max(1, ActualHeight);
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)Math.Ceiling(w * 2), (int)Math.Ceiling(h * 2), 192, 192, PixelFormats.Pbgra32);
            rtb.Render(this);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using (var fs = File.Create(path)) enc.Save(fs);
        }
    }
}
