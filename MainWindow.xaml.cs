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
        private double _lastCanvasWidth = -1;   // 上次内容区宽度，用来判断是"拖宽"还是"变高"
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
            // 尺寸预设只改宽度：高度由内容决定（见 SetWidth 的注释）
            ResetSizeBtn.Click += (s, e) => SetWidth(ViewState.FullWindowWidth);
            SizeSmallBtn.Click += (s, e) => SetWidth(360);
            SizeMidBtn.Click += (s, e) => SetWidth(ViewState.FullWindowWidth);
            SizeBigBtn.Click += (s, e) => SetWidth(560);

            FullCanvas.SizeChanged += OnCanvasSizeChanged;

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
    }
}
