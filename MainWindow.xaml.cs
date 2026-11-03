using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WorkEncourager
{
    public partial class MainWindow : Window
    {
        /// <summary>法定月计薪天数：(365 - 104) / 12 = 21.75</summary>
        private const double PayDaysPerMonth = 21.75;

        // ---------------- 配置 ----------------
        private class Cfg
        {
            public double? MonthlySalary { get; set; }
            public string Start { get; set; } = "09:00";
            public string End { get; set; } = "18:00";
            public double Shirt { get; set; } = 69;
            public int[] Weekdays { get; set; } = new[] { 1, 2, 3, 4, 5 };
            public double? WindowLeft { get; set; }
            public double? WindowTop { get; set; }
            public double? WindowWidth { get; set; }
            public double? WindowHeight { get; set; }
            public double? Opacity { get; set; }
            public bool Topmost { get; set; } = true;

            // 旧版配置字段（只读不写，用于自动迁移）
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public double? salary { get; set; }
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public double? hours { get; set; }
        }

        private static readonly string CfgDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WorkEncourager");
        private static readonly string CfgPath = Path.Combine(CfgDir, "settings.json");

        private double Salary => _cfg.MonthlySalary ?? 0;

        private Cfg _cfg = new Cfg();
        private readonly DispatcherTimer _timer = new DispatcherTimer();

        // 暂停状态
        private bool _paused;
        private TimeSpan _pausedAccum = TimeSpan.Zero;
        private DateTime _pauseAt;
        private string _dayKey = "";

        private readonly SolidColorBrush _accentDark = new SolidColorBrush(Color.FromRgb(0xC2, 0x41, 0x0C));

        private readonly string[] _weekNames = { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };

        public MainWindow()
        {
            InitializeComponent();
            LoadCfg();
            ApplyTopmost();
            ApplyOpacity();
            RestoreSize();
            RestorePosition();

            _timer.Interval = TimeSpan.FromMilliseconds(60);
            _timer.Tick += (s, e) => Render();
            _timer.Start();

            Shell.MouseLeftButtonDown += OnDragArea;
            SizeChanged += (s, e) => { UpdateBar(); UpdateSizeText(); };
            Loaded += (s, e) => { Render(); UpdateBar(); UpdateSizeText(); };

            ToggleBtn.Click += OnToggleClick;
            ResetBtn.Click += OnResetClick;
            PinBtn.Click += OnPinClick;
            CloseBtn.Click += (s, e) => Close();
            SettingsBtn.Click += (s, e) => OpenSettings();

            CloseSettingsBtn.Click += (s, e) => CloseSettings();
            CancelBtn.Click += (s, e) => CloseSettings();
            SaveBtn.Click += (s, e) => SaveSettings();
            DefaultBtn.Click += (s, e) => FillSettings(new Cfg());
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape && SettingsLayer.Visibility == Visibility.Visible)
                {
                    CloseSettings();
                    e.Handled = true;
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
                SaveCfg();
            };

            InSalary.TextChanged += (s, e) => UpdatePreview();
            InStart.TextChanged += (s, e) => { UpdateAutoHours(); UpdatePreview(); };
            InEnd.TextChanged += (s, e) => { UpdateAutoHours(); UpdatePreview(); };
            InShirt.TextChanged += (s, e) => UpdatePreview();

            OpacitySlider.ValueChanged += (s, e) =>
            {
                if (!IsLoaded) return;
                Opacity = OpacitySlider.Value;
                OpacityValueText.Text = Math.Round(Opacity * 100).ToString() + "%";
            };
            ResetSizeBtn.Click += (s, e) =>
            {
                Width = DefaultWidth;
                Height = DefaultHeight;
                UpdateSizeText();
            };
            SizeSmallBtn.Click += (s, e) => SetSize(392, 556);
            SizeMidBtn.Click += (s, e) => SetSize(456, 644);
            SizeBigBtn.Click += (s, e) => SetSize(600, 820);
        }

        private void SetSize(double w, double h)
        {
            Width = w;
            Height = h;
            UpdateSizeText();
        }

        private const double DefaultWidth = 456;
        private const double DefaultHeight = 644;

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

        // ---------------- 拖动 ----------------
        private void OnDragArea(object sender, MouseButtonEventArgs e)
        {
            if (SettingsLayer.Visibility == Visibility.Visible) return;
            if (e.ChangedButton != MouseButton.Left) return;
            try { DragMove(); } catch { }
            _cfg.WindowLeft = Left;
            _cfg.WindowTop = Top;
            SaveCfg();
        }

        // ---------------- 位置 ----------------
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

        // ---------------- 配置读写 ----------------
        private void LoadCfg()
        {
            try
            {
                if (File.Exists(CfgPath))
                {
                    var json = File.ReadAllText(CfgPath);
                    var loaded = JsonSerializer.Deserialize<Cfg>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (loaded != null) _cfg = loaded;
                }
            }
            catch { _cfg = new Cfg(); }

            // 旧版配置迁移：日薪 + 工时 -> 月薪 + 下班时间
            if (!_cfg.MonthlySalary.HasValue)
            {
                if (_cfg.salary.HasValue && _cfg.salary.Value > 0)
                {
                    _cfg.MonthlySalary = Math.Round(_cfg.salary.Value * PayDaysPerMonth, 2);
                    if (_cfg.hours.HasValue && _cfg.hours.Value > 0)
                        _cfg.End = AddHours(_cfg.Start, _cfg.hours.Value);
                }
                else
                {
                    _cfg.MonthlySalary = 6000;
                }
            }
            _cfg.salary = null;
            _cfg.hours = null;

            if (_cfg.MonthlySalary.Value < 0) _cfg.MonthlySalary = 0;
            if (_cfg.Shirt <= 0) _cfg.Shirt = 69;
            if (_cfg.Weekdays == null || _cfg.Weekdays.Length == 0) _cfg.Weekdays = new[] { 1, 2, 3, 4, 5 };
            if (!TryParseTime(_cfg.Start, out _, out _)) _cfg.Start = "09:00";
            if (!TryParseTime(_cfg.End, out _, out _)) _cfg.End = "18:00";
        }

        private void SaveCfg()
        {
            try
            {
                Directory.CreateDirectory(CfgDir);
                File.WriteAllText(CfgPath, JsonSerializer.Serialize(_cfg, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private static bool TryParseTime(string s, out int h, out int m)
        {
            h = 9; m = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            var parts = s.Trim().Replace("：", ":").Split(':');
            if (parts.Length != 2) return false;
            if (!int.TryParse(parts[0], out h)) return false;
            if (!int.TryParse(parts[1], out m)) return false;
            return h >= 0 && h <= 23 && m >= 0 && m <= 59;
        }

        private static string AddHours(string time, double hours)
        {
            if (!TryParseTime(time, out int h, out int m)) return "18:00";
            var t = new DateTime(2000, 1, 1, h, m, 0).AddHours(hours);
            return t.ToString("HH:mm");
        }

        // ---------------- 计算 ----------------
        private struct Snapshot
        {
            public DateTime Now;
            public DateTime EffNow;
            public DateTime StartTs;
            public DateTime EndTs;
            public double TotalSec;
            public double DailyPay;
            public double PerSec;
            public double Hourly;
            public double WorkedSec;
            public double RemainSec;
            public double Earned;
            public double Pct;
            public string Status; // working / off / wait / paused
        }

        private Snapshot Compute()
        {
            var realNow = DateTime.Now;

            string key = realNow.ToString("yyyy-MM-dd");
            if (_dayKey.Length > 0 && key != _dayKey)
            {
                _paused = false;
                _pausedAccum = TimeSpan.Zero;
            }
            _dayKey = key;

            var effNow = _paused ? _pauseAt - _pausedAccum : realNow - _pausedAccum;

            TryParseTime(_cfg.Start, out int sh, out int sm);
            TryParseTime(_cfg.End, out int eh, out int em);

            var startTs = realNow.Date.AddHours(sh).AddMinutes(sm);
            var endTs = realNow.Date.AddHours(eh).AddMinutes(em);
            if (endTs <= startTs) endTs = endTs.AddDays(1);   // 支持跨天（如 22:00 → 06:00）

            double totalSec = Math.Max(60, (endTs - startTs).TotalSeconds);
            double dailyPay = Salary / PayDaysPerMonth;
            double perSec = dailyPay / totalSec;
            double hourly = perSec * 3600.0;

            double workedSec = (effNow - startTs).TotalSeconds;
            if (workedSec < 0) workedSec = 0;
            if (workedSec > totalSec) workedSec = totalSec;

            double remainSec = Math.Max(0, totalSec - workedSec);
            double earned = perSec * workedSec;

            string status;
            if (_paused) status = "paused";
            else if (effNow < startTs) status = "wait";
            else if (effNow >= endTs) status = "off";
            else status = "working";

            return new Snapshot
            {
                Now = realNow,
                EffNow = effNow,
                StartTs = startTs,
                EndTs = endTs,
                TotalSec = totalSec,
                DailyPay = dailyPay,
                PerSec = perSec,
                Hourly = hourly,
                WorkedSec = workedSec,
                RemainSec = remainSec,
                Earned = earned,
                Pct = workedSec / totalSec * 100.0,
                Status = status
            };
        }

        /// <summary>本月累计：已过的计薪日按满勤补算，今天按实际已赚</summary>
        private double MonthTotal(Snapshot st)
        {
            int y = st.Now.Year, mo = st.Now.Month, today = st.Now.Day;
            double sum = 0;
            for (int d = 1; d <= today; d++)
            {
                var dt = new DateTime(y, mo, d);
                if (Array.IndexOf(_cfg.Weekdays, (int)dt.DayOfWeek) < 0) continue;
                sum += (d == today) ? st.Earned : st.DailyPay;
            }
            return sum;
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

        private double _lastPct;

        private void Render()
        {
            var st = Compute();

            DateText.Text = st.Now.ToString("yyyy年M月d日") + " " + _weekNames[(int)st.Now.DayOfWeek];
            TimeText.Text = st.Now.ToString("HH:mm:ss");

            // 状态胶囊
            string statusText, bg, fg, dot;
            switch (st.Status)
            {
                case "paused": statusText = "已暂停"; bg = "#FFFAEB"; fg = "#B45309"; dot = "#F59E0B"; break;
                case "off": statusText = "已下班"; bg = "#F2F4F7"; fg = "#475467"; dot = "#98A2B3"; break;
                case "wait": statusText = "待上班"; bg = "#EFF6FF"; fg = "#1D4ED8"; dot = "#3B82F6"; break;
                default: statusText = "上班中"; bg = "#ECFDF3"; fg = "#067647"; dot = "#12B76A"; break;
            }
            if (StatusText.Text != statusText) StatusText.Text = statusText;
            StatusChip.Background = (Brush)new BrushConverter().ConvertFromString(bg);
            StatusText.Foreground = (Brush)new BrushConverter().ConvertFromString(fg);
            StatusDot.Fill = (Brush)new BrushConverter().ConvertFromString(dot);

            // 倒计时
            if (st.Status == "wait") CountText.Text = "距上班 " + Clock((st.StartTs - st.EffNow).TotalSeconds);
            else if (st.Status == "off") CountText.Text = "今日已收工";
            else CountText.Text = "距下班 " + Clock(st.RemainSec);

            // 金额
            EarnedText.Text = N4(st.Earned);
            RateText.Text = st.PerSec.ToString("N4", CultureInfo.InvariantCulture);
            DailyTipText.Text = "日薪 ¥" + N4(st.DailyPay) + " · 满勤 " +
                                (st.TotalSec / 3600.0).ToString("0.#", CultureInfo.InvariantCulture) + " 小时";

            // 进度
            PctText.Text = st.Pct.ToString("0.0", CultureInfo.InvariantCulture);
            WorkedText.Text = (st.WorkedSec / 3600.0).ToString("0.00", CultureInfo.InvariantCulture);
            TotalText.Text = (st.TotalSec / 3600.0).ToString("0.0", CultureInfo.InvariantCulture);
            RemainText.Text = (st.RemainSec / 3600.0).ToString("0.00", CultureInfo.InvariantCulture);

            UpdateBar(st.Pct);

            // 数据卡
            MonthLabel.Text = st.Now.Month + "月累计";
            MonthText.Text = N4(MonthTotal(st));
            HourlyText.Text = N4(st.Hourly);
            FullText.Text = N4(st.DailyPay);
            FullNote.Text = "月薪 ÷ " + PayDaysPerMonth.ToString("0.##", CultureInfo.InvariantCulture) + " 天";

            // 按钮状态
            ToggleBtn.Content = _paused ? "开启计时器" : "关闭计时器";
            ToggleBtn.Style = (Style)FindResource(_paused ? "GreenBtn" : "PrimaryBtn");

            BuildEncourage(st);
        }

        private void UpdateBar(double pct = double.NaN)
        {
            if (double.IsNaN(pct)) pct = _lastPct;
            _lastPct = pct;
            double w = BarHost.ActualWidth;
            if (w <= 0) return;
            double fill = w * Math.Max(0, Math.Min(100, pct)) / 100.0;
            BarFill.Width = Math.Max(0, fill);
        }

        private void BuildEncourage(Snapshot st)
        {
            var runs = new List<Inline>();

            void T(string text)
            {
                runs.Add(new Run(text) { Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x34, 0x12)) });
            }
            void B(string text)
            {
                runs.Add(new Run(text) { FontWeight = FontWeights.Bold, Foreground = _accentDark });
            }

            if (st.PerSec <= 0)
            {
                T("先去右上角「设置」里填个月薪吧");
            }
            else if (st.Status == "paused")
            {
                T("计时器已暂停，开心的时光不用算钱");
            }
            else if (st.Status == "off")
            {
                T("今天已经收工，一共赚到 ");
                B("¥" + N4(st.Earned));
                T("，去休息吧");
            }
            else if (st.Status == "wait")
            {
                T("还没到点，距离开工还有 ");
                B(HM((st.StartTs - st.EffNow).TotalSeconds));
            }
            else
            {
                double price = _cfg.Shirt > 0 ? _cfg.Shirt : 0;
                int count = price > 0 ? (int)Math.Floor(st.Earned / price) : 0;
                double nextNeed = price > 0 ? ((count + 1) * price - st.Earned) / st.PerSec : double.MaxValue;

                if (price <= 0)
                {
                    T("设置一个鼓励物单价，就知道离小目标还有多远了");
                }
                else if (nextNeed <= st.RemainSec)
                {
                    if (count > 0)
                    {
                        T("今天已赚回 ");
                        B(count + " 件T恤");
                        T("，再过 ");
                        B(HM(nextNeed));
                        T(" 下一件到手");
                    }
                    else
                    {
                        T("再过 ");
                        B(HM(nextNeed));
                        T("，一件T恤就赚回来了");
                    }
                }
                else
                {
                    double canBuy = st.PerSec * st.RemainSec / price;
                    T("再过 ");
                    B(HM(st.RemainSec));
                    T(" 就下班了，今天的钱还能买 ");
                    B(canBuy.ToString("0.00", CultureInfo.InvariantCulture) + " 件T恤");
                }
            }

            EncText.Inlines.Clear();
            EncText.Inlines.AddRange(runs);
        }

        // ---------------- 交互 ----------------
        private void OnToggleClick(object sender, RoutedEventArgs e)
        {
            if (!_paused)
            {
                _paused = true;
                _pauseAt = DateTime.Now;
            }
            else
            {
                _paused = false;
                _pausedAccum += DateTime.Now - _pauseAt;
            }
            Render();
        }

        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            _paused = false;
            _pausedAccum = TimeSpan.Zero;
            Render();
        }

        private void ApplyTopmost()
        {
            Topmost = _cfg.Topmost;
            PinBtn.Foreground = Topmost
                ? (Brush)new BrushConverter().ConvertFromString("#F97316")
                : (Brush)new BrushConverter().ConvertFromString("#C3CAD6");
        }

        private void OnPinClick(object sender, RoutedEventArgs e)
        {
            _cfg.Topmost = !_cfg.Topmost;
            ApplyTopmost();
            SaveCfg();
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
            SettingsLayer.Visibility = Visibility.Visible;
        }

        private void CloseSettings()
        {
            SettingsLayer.Visibility = Visibility.Collapsed;
        }

        public void ShowSettingsForDebug() { OpenSettings(); }

        private void FillSettings(Cfg c)
        {
            InSalary.Text = (c.MonthlySalary ?? 6000).ToString("0.##", CultureInfo.InvariantCulture);
            InStart.Text = c.Start;
            InEnd.Text = c.End;
            InShirt.Text = c.Shirt.ToString("0.##", CultureInfo.InvariantCulture);

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

        /// <summary>按输入的上下班时间推算每日工时</summary>
        private double? PreviewWorkHours()
        {
            if (!TryParseTime(InStart.Text, out int sh, out int sm)) return null;
            if (!TryParseTime(InEnd.Text, out int eh, out int em)) return null;
            var s = new DateTime(2000, 1, 1, sh, sm, 0);
            var e = new DateTime(2000, 1, 1, eh, em, 0);
            if (e <= s) e = e.AddDays(1);
            return (e - s).TotalHours;
        }

        private void UpdateAutoHours()
        {
            if (AutoHoursText == null) return;
            var h = PreviewWorkHours();
            if (h.HasValue && h.Value > 0)
                AutoHoursText.Text = "每日工时 " + h.Value.ToString("0.##", CultureInfo.InvariantCulture) +
                                     " 小时（" + InStart.Text.Trim() + " → " + InEnd.Text.Trim() + "）";
            else
                AutoHoursText.Text = "上下班时间还没填好，工时按 9 小时预估";
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

            double daily = monthly / PayDaysPerMonth;
            double hourly = daily / hours;
            double perSec = daily / (hours * 3600.0);

            PrevText.Text = "日薪 ¥" + N4(daily) + "　·　时薪 ¥" + N4(hourly) +
                            "\n每秒 ¥" + N4(perSec) + "　·　今天满勤 ¥" + N4(daily);
        }

        private void SaveSettings()
        {
            double monthly = ParseNum(InSalary.Text);
            if (double.IsNaN(monthly) || monthly < 0)
            {
                SettingsTip.Text = "月薪请填一个非负数字"; return;
            }
            if (!TryParseTime(InStart.Text, out int sh, out int sm))
            {
                SettingsTip.Text = "上班时间格式不对，例如 09:00"; return;
            }
            if (!TryParseTime(InEnd.Text, out int eh, out int em))
            {
                SettingsTip.Text = "下班时间格式不对，例如 18:00"; return;
            }
            if (sh == eh && sm == em)
            {
                SettingsTip.Text = "上班时间和下班时间不能相同"; return;
            }
            double shirt = ParseNum(InShirt.Text);
            if (double.IsNaN(shirt) || shirt <= 0)
            {
                SettingsTip.Text = "鼓励物单价需大于 0"; return;
            }

            var days = new List<int>();
            var map = new Dictionary<int, ToggleButton>
            {
                { 0, Dw0 }, { 1, Dw1 }, { 2, Dw2 }, { 3, Dw3 },
                { 4, Dw4 }, { 5, Dw5 }, { 6, Dw6 }
            };
            foreach (var kv in map)
                if (kv.Value.IsChecked == true) days.Add(kv.Key);
            if (days.Count == 0)
            {
                SettingsTip.Text = "至少选择一个计薪日"; return;
            }
            days.Sort();

            _cfg.MonthlySalary = monthly;
            _cfg.Start = sh.ToString("00") + ":" + sm.ToString("00");
            _cfg.End = eh.ToString("00") + ":" + em.ToString("00");
            _cfg.Shirt = shirt;
            _cfg.Weekdays = days.ToArray();
            _cfg.Opacity = Opacity;
            _cfg.WindowWidth = ActualWidth;
            _cfg.WindowHeight = ActualHeight;
            SaveCfg();

            CloseSettings();
            Render();   // 保存后立即用新参数重算所有数值
        }
    }
}
