using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
        private static readonly string CfgPath = Path.Combine(CfgDir, "settings.json");
        private static readonly string HistoryPath = Path.Combine(CfgDir, "history.json");

        private const double DefaultWidth = 456;
        private const double DefaultHeight = 644;

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

        private Theme _theme = Theme.Light();
        private Brush _bTextPrimary, _bTextSecondary, _bTextMuted, _bAccent, _bAccentDark, _bEncText;
        private Brush _bChipBg, _bChipFg, _bChipDot;

        private readonly string[] _weekNames = { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };

        public MainWindow()
        {
            InitializeComponent();
            LoadCfg();
            ApplyTheme();
            ApplyTopmost();
            ApplyOpacity();
            ApplyClickThrough();
            RestoreSize();
            RestorePosition();

            _timer.Interval = TimeSpan.FromMilliseconds(200);
            _timer.Tick += (s, e) => Render();
            _timer.Start();

            Shell.MouseLeftButtonDown += OnDragArea;
            SizeChanged += (s, e) => { UpdateBar(); UpdateSizeText(); };
            Loaded += (s, e) => { _loaded = true; ApplyCompactMode(); Render(); UpdateBar(); UpdateSizeText(); };

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
            CloseBtn.Click += (s, e) => Close();
            SettingsBtn.Click += (s, e) => OpenSettings();

            CloseSettingsBtn.Click += (s, e) => CloseSettings();
            CancelBtn.Click += (s, e) => CloseSettings();
            SaveBtn.Click += (s, e) => SaveSettings();
            DefaultBtn.Click += (s, e) => FillSettings(new AppConfig());
            ExportCfgBtn.Click += (s, e) => ExportConfig();
            ImportCfgBtn.Click += (s, e) => ImportConfig();
            ExportCsvBtn.Click += (s, e) => ExportCsv();

            StatsBtn.Click += (s, e) => OpenStats();
            CloseStatsBtn.Click += (s, e) => CloseStats();
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
                    if (SettingsLayer.Visibility == Visibility.Visible) { CloseSettings(); e.Handled = true; }
                    else if (StatsLayer.Visibility == Visibility.Visible) { CloseStats(); e.Handled = true; }
                }
            };

            Closing += (s, e) =>
            {
                _cfg.WindowLeft = Left;
                _cfg.WindowTop = Top;
                _cfg.WindowWidth = ActualWidth;
                _cfg.WindowHeight = ActualHeight;
                _cfg.Opacity = Opacity;
                _cfg.Topmost = Topmost;
                if (!_debugNoPersist) PersistPauseState();
                SaveCfg();
                _tracker.Save(HistoryPath);
            };

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
                OpacityValueText.Text = Math.Round(Opacity * 100).ToString() + "%";
            };
            ResetSizeBtn.Click += (s, e) => { Width = DefaultWidth; Height = DefaultHeight; UpdateSizeText(); };
            SizeSmallBtn.Click += (s, e) => SetSize(392, 556);
            SizeMidBtn.Click += (s, e) => SetSize(456, 644);
            SizeBigBtn.Click += (s, e) => SetSize(600, 820);

            ToneChillBtn.Click += (s, e) => SetTone("chill");
            ToneSavageBtn.Click += (s, e) => SetTone("savage");
            ToneHypeBtn.Click += (s, e) => SetTone("hype");
            ThemeLightBtn.Click += (s, e) => SetTheme("light");
            ThemeDarkBtn.Click += (s, e) => SetTheme("dark");
            CompactToggleBtn.Click += (s, e) => ToggleCompact();
            ClickThroughToggleBtn.Click += (s, e) => ToggleClickThrough();
        }

        // ---------------- 配置 ----------------

        private void LoadCfg()
        {
            _cfg = ConfigStore.Load(CfgPath);
            _calendar = ConfigStore.BuildCalendar(_cfg);
            _schedule = ConfigStore.BuildSchedule(_cfg);
            _calc = new SalaryCalculator(_schedule, _calendar);
            _tracker = WorkdayTracker.Load(HistoryPath);
            RestorePauseState();
        }

        private void SaveCfg() => ConfigStore.Save(CfgPath, _cfg);

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

        /// <summary>保存当前窗口位置与尺寸到配置。</summary>
        private void SaveWindowBounds()
        {
            _cfg.WindowLeft = Left;
            _cfg.WindowTop = Top;
            _cfg.WindowWidth = ActualWidth;
            _cfg.WindowHeight = ActualHeight;
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

            // 极简面板
            if (CompactPanel.Visibility == Visibility.Visible)
            {
                CompactEarned.Text = N4(st.Earned);
                CompactCount.Text = st.Status == "paused" ? "计时已暂停"
                                : st.Status == "wait" ? "距上班 " + Clock((st.StartTs - now).TotalSeconds)
                                : st.Status == "off" ? "今日已收工"
                                : "距下班 " + Clock(st.RemainSec);
                CompactStatus.Text = statusText + " · 日薪 ¥" + N4(st.DailyPay);
            }

            BuildEncourage(st);
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

        /// <summary>语录库：按语气随机取一条，用于设置面板预览。</summary>
        public string GetSampleQuote()
        {
            return _quotes.Get(QuoteProvider.ParseTone(_cfg.Tone),
                               string.IsNullOrWhiteSpace(_cfg.GoalItem) ? "目标" : _cfg.GoalItem,
                               "3小时25分", "888.8888");
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
            SettingsShell.Background = MakeBrush(_theme.SettingsBg);
            SettingsShell.BorderBrush = MakeBrush(_theme.ShellBorder);

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
            SettingsTitleText.Foreground = _bTextPrimary;
            SettingsSubText.Foreground = _bTextMuted;
            StatsTitleText.Foreground = _bTextPrimary;
            StatsSubText.Foreground = _bTextMuted;
            StatsShell.Background = MakeBrush(_theme.SettingsBg);
            StatsShell.BorderBrush = MakeBrush(_theme.ShellBorder);
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

        private void ToggleCompact()
        {
            _cfg.CompactMode = !_cfg.CompactMode;
            ApplyCompactMode();
            SaveCfg();
        }

        private void ApplyCompactMode()
        {
            if (_cfg.CompactMode)
            {
                FullPanel.Visibility = Visibility.Collapsed;
                CompactPanel.Visibility = Visibility.Visible;
                Width = 268; Height = 132;
            }
            else
            {
                FullPanel.Visibility = Visibility.Visible;
                CompactPanel.Visibility = Visibility.Collapsed;
                Width = _cfg.WindowWidth ?? DefaultWidth;
                Height = _cfg.WindowHeight ?? DefaultHeight;
            }
            UpdateSizeText();
        }

        // ---------------- 鼠标穿透 ----------------

        private void ToggleClickThrough()
        {
            _cfg.ClickThrough = !_cfg.ClickThrough;
            ApplyClickThrough();
            SaveCfg();
        }

        private void ApplyClickThrough()
        {
            Win32.SetClickThrough(this, _cfg.ClickThrough);
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
            UpdateSizeText();
        }

        private void RestoreSize()
        {
            double w = _cfg.WindowWidth ?? DefaultWidth;
            double h = _cfg.WindowHeight ?? DefaultHeight;
            if (w < 360) w = DefaultWidth;
            if (h < 400) h = DefaultHeight;
            Width = w;
            Height = h;
        }

        private void UpdateSizeText()
        {
            if (SizeText == null) return;
            SizeText.Text = Math.Round(ActualWidth) + " × " + Math.Round(ActualHeight) +
                            "（拖动窗体边框可缩放）";
        }

        private void OnDragArea(object sender, MouseButtonEventArgs e)
        {
            if (SettingsLayer.Visibility == Visibility.Visible) return;
            if (e.ChangedButton != MouseButton.Left) return;
            try { DragMove(); } catch { }
            _cfg.WindowLeft = Left;
            _cfg.WindowTop = Top;
            SaveCfg();
        }

        private void RestorePosition()
        {
            var wa = SystemParameters.WorkArea;
            double w = Width;
            double left = _cfg.WindowLeft ?? (wa.Right - w - 24);
            double top = _cfg.WindowTop ?? (wa.Top + 90);
            if (left < wa.Left - 40 || left > wa.Right - 80) left = wa.Right - w - 24;
            if (top < wa.Top - 10 || top > wa.Bottom - 120) top = wa.Top + 90;
            Left = left;
            Top = top;
        }

        // ---------------- 设置 ----------------

        private void OpenSettings()
        {
            FillSettings(_cfg);
            SettingsTip.Text = "";
            ApplyOpacity();
            UpdateAutoHours();
            UpdatePreview();
            UpdateSizeText();
            UpdateToneButtons();
            UpdateThemeButtons();
            SettingsLayer.Visibility = Visibility.Visible;
        }

        private void CloseSettings() => SettingsLayer.Visibility = Visibility.Collapsed;

        public void ShowSettingsForDebug() => OpenSettings();

        private void FillSettings(AppConfig c)
        {
            InSalary.Text = (c.MonthlySalary ?? 6000).ToString("0.##", CultureInfo.InvariantCulture);
            InStart.Text = c.Start;
            InEnd.Text = c.End;
            InBreak.Text = c.Breaks != null && c.Breaks.Length > 0 ? c.Breaks[0] : "";
            InShirt.Text = c.Shirt.ToString("0.##", CultureInfo.InvariantCulture);
            InGoalItem.Text = c.GoalItem;
            InPayDay.Text = c.PayDay.ToString(CultureInfo.InvariantCulture);

            var map = new Dictionary<int, ToggleButton>
            {
                { 0, Dw0 }, { 1, Dw1 }, { 2, Dw2 }, { 3, Dw3 },
                { 4, Dw4 }, { 5, Dw5 }, { 6, Dw6 }
            };
            foreach (var kv in map)
                kv.Value.IsChecked = Array.IndexOf(c.Weekdays, kv.Key) >= 0;
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
            string bt = (InBreak.Text ?? "").Trim();
            if (bt.Length > 0)
            {
                var p = bt.Split('-');
                if (p.Length == 2 && WorkSchedule.TryParseMinutes(p[0], out _) && WorkSchedule.TryParseMinutes(p[1], out _))
                    breaks.Add(bt);
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
            string brkText = (InBreak.Text ?? "").Trim();
            if (brkText.Length > 0)
            {
                var p = brkText.Split('-');
                if (p.Length != 2 || !WorkSchedule.TryParseMinutes(p[0], out _) || !WorkSchedule.TryParseMinutes(p[1], out _))
                { SettingsTip.Text = "午休格式不对，例如 12:00-13:00"; return; }
                breaks.Add(brkText);
            }

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

        private void ExportConfig()
        {
            try
            {
                var path = Path.Combine(CfgDir, "workenc-config-" +
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
                    InitialDirectory = CfgDir
                };
                if (dlg.ShowDialog(this) != true) return;
                var cfg = ConfigStore.ImportJson(File.ReadAllText(dlg.FileName));
                if (cfg == null) { SettingsTip.Text = "配置文件格式不对，导入失败"; return; }
                _cfg = cfg;
                RebuildCalc();
                SaveCfg();
                ApplyTheme();
                FillSettings(_cfg);
                ApplyOpacity();
                UpdateAutoHours();
                UpdatePreview();
                UpdateToneButtons();
                UpdateThemeButtons();
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
                var path = Path.Combine(CfgDir, "工时报表-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
                CsvExporter.WriteFile(path, CsvExporter.BuildWorkReport(_tracker.Records, _calendar, _cfg.Weekdays));
                _lastExportPath = path;
                ShowTip(StatsLayer.Visibility == Visibility.Visible ? StatsTip : SettingsTip,
                        "工时报表已导出，路径可复制：\n" + path);
            }
            catch (Exception ex) { ShowTip(SettingsTip, "导出失败：" + ex.Message); }
        }

        private void ExportMonthlyCsv()
        {
            try
            {
                _tracker.Save(HistoryPath);
                var path = Path.Combine(CfgDir, "月度汇总-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
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
                string dir = Path.GetDirectoryName(_lastExportPath) ?? CfgDir;
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

        private void OpenStats()
        {
            StatsTip.Text = "";
            RefreshStats();
            StatsLayer.Visibility = Visibility.Visible;
        }

        private void CloseStats() => StatsLayer.Visibility = Visibility.Collapsed;

        public void ShowStatsForDebug() => OpenStats();

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
