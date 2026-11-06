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
        /// <summary>
        /// 设置页滚动区的最大高度（DIP）。
        /// 这个数字同时决定"内容可见多高"和"窗口最小高度算到哪"，两处必须一致，
        /// 以前是各写一个 430 字面量，改一处忘另一处就会露馅。
        /// </summary>
        private const double SettingsScrollMaxHeight = 430;

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
            // 先判断有没有真的换 Tab（ApplyTab 会覆盖 _tab）
            bool changed = _tab != tab;

            if (_viewMode == AppViewMode.Mini) SetViewMode(AppViewMode.Full);

            ApplyTab(tab);

            // 切换 Tab 时按该页的实际内容高度调整画布与窗口。
            // 内容测量需要布局已完成，所以推迟到下一帧再执行。
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitCanvasHeight));

            // 立刻把当前 Tab 落盘。
            // 隐藏到托盘 / 退出时虽然也会 RememberBounds + SaveCfg，但那是"顺手"，
            // 一旦进程被强杀、蓝屏、断电，Tab 偏好就丢了——下次打开跳回主页，
            // 而用户会认为"我明明停在设置页关的，这程序有 bug"。
            // 只为一次 ~1KB 的写盘冒这个风险不值得。
            // 真正要省的是"重复点当前 Tab"这种零变化的写入。
            if (changed) SaveCfg();
        }

        /// <summary>
        /// 应用一个 Tab 的全部界面副作用：内容可见性、Tab 按钮高亮、设置页回填、滚动高度上限。
        ///
        /// 启动恢复（RestoreViewState）也必须走这里，不能只翻 Visibility：
        /// v2.2.0 之前它自己手写了一小段只改可见性的代码，结果重启后停在设置页时
        /// 内容显示的是设置、高亮的却是"主页"，而且表单是空的、滚动高度上限也没设。
        /// </summary>
        private void ApplyTab(MainTab tab)
        {
            _tab = tab;
            _cfg.ActiveTab = ViewState.ToText(tab);

            HomePanel.Visibility = tab == MainTab.Home ? Visibility.Visible : Visibility.Collapsed;
            DetailPanel.Visibility = tab == MainTab.Detail ? Visibility.Visible : Visibility.Collapsed;
            StatsPanel.Visibility = tab == MainTab.Stats ? Visibility.Visible : Visibility.Collapsed;
            SettingsScroll.Visibility = tab == MainTab.Settings ? Visibility.Visible : Visibility.Collapsed;

            if (tab == MainTab.Settings)
            {
                FillSettings(_cfg);
                // 记下"打开设置页时"的表单状态，Esc 时用来判断有没有没保存的改动
                CaptureSettingsBaseline();
                // 语录文件有解析问题时一直挂在这儿，别让用户改坏了却以为改了没生效
                SettingsTip.Text = QuoteErrorTip();
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
            SettingsScroll.MaxHeight = tab == MainTab.Settings ? SettingsScrollMaxHeight : double.PositiveInfinity;

            UpdateTabButtons();
        }

        /// <summary>
        /// 按当前 Tab 的内容算出需要多高，并把它设成窗口的高度下限。
        ///
        /// 去掉 Viewbox 之后，界面不再整体缩放，文字始终保持设定字号。
        /// 宽度是自由的（拉宽内容会跟着铺开），高度则"够用就好"：
        /// 内容需要多高，窗口最小就多高，用户拖不矮到裁切内容。
        /// 设置页内容很长，用内部的 ScrollViewer 滚动，所以给它一个上限。
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
            double need = Math.Ceiling(ShellGrid.ActualHeight + 25);

            if (_tab == MainTab.Settings)
            {
                double maxH = SettingsScrollMaxHeight + 25 + 25 + 30 + 11 + 11;
                if (need > maxH) need = maxH;
            }

            double minH = need + ViewState.EdgeMargin * 2;

            // 内容高度 = 窗口高度下限，拖不矮
            if (Math.Abs(MinHeight - minH) > 0.5) MinHeight = minH;

            // 窗口高度是否还"跟着内容走"：
            // 用户从没手动调过高度，或者当前高度正好等于上次自动贴合的值（说明这次也是自动撑出来的），
            // 就跟着内容缩放；反之（用户自己拖过高）就保持用户的选择。
            if (_lastAutoHeight < 0) _lastAutoHeight = Height;
            bool followsContent = !_userResizedFull || Math.Abs(Height - _lastAutoHeight) < 2;

            if (followsContent && Math.Abs(Height - minH) > 0.5) Height = minH;
            _lastAutoHeight = minH;
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

        // ---------------- 设置 ----------------

        private void CloseSettings() => SelectTab(MainTab.Home);

        // ---------------- 未保存改动的保护 ----------------

        /// <summary>进入设置页时的表单快照，用来判断用户有没有改了没保存。</summary>
        private string _settingsBaseline;

        /// <summary>上一次按 Esc 提醒"未保存"的时刻；短时间内再按一次才真的放弃。</summary>
        private DateTime _escLeaveArmedAt = DateTime.MinValue;

        /// <summary>Esc 提醒的有效期，超过就当作重新开始计数。</summary>
        private static readonly TimeSpan EscConfirmWindow = TimeSpan.FromSeconds(5);

        private void CaptureSettingsBaseline() => _settingsBaseline = SettingsFormSnapshot();

        /// <summary>把设置页里所有用户可改的输入项拼成一个字符串，用于比较是否有改动。</summary>
        private string SettingsFormSnapshot() => string.Join("\u0001", new[]
        {
            InSalary.Text, InStart.Text, InEnd.Text, InBreak.Text,
            InExtraHolidays.Text, InExtraWorkdays.Text, InShirt.Text,
            InGoalItem.Text, InPayDay.Text,
            Dw0.IsChecked?.ToString(), Dw1.IsChecked?.ToString(), Dw2.IsChecked?.ToString(),
            Dw3.IsChecked?.ToString(), Dw4.IsChecked?.ToString(), Dw5.IsChecked?.ToString(),
            Dw6.IsChecked?.ToString(),
        });

        private bool SettingsDirty() =>
            _settingsBaseline != null && SettingsFormSnapshot() != _settingsBaseline;

        /// <summary>
        /// 在设置页按 Esc：有未保存的改动就先提醒一次，再按一次才丢弃返回。
        /// 以前是直接跳回主页，用户填到一半按 Esc 就白填了，而且没有任何提示。
        /// </summary>
        private void TryLeaveSettings()
        {
            if (!SettingsDirty())
            {
                _escLeaveArmedAt = DateTime.MinValue;
                SelectTab(MainTab.Home);
                return;
            }

            if (DateTime.Now - _escLeaveArmedAt > EscConfirmWindow)
            {
                _escLeaveArmedAt = DateTime.Now;
                SettingsTip.Text = "有未保存的修改：再按一次 Esc 放弃并返回，或按 Ctrl+S 保存。";
                return;
            }

            _escLeaveArmedAt = DateTime.MinValue;
            SelectTab(MainTab.Home);
        }

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
            // 注意：不能写 _cfg.Opacity = Opacity。幽灵模式淡化期间 Opacity 是 0.28 这种临时值，
            // 存进去下次启动就变成"默认很淡、还以为程序坏了"。滑块改动时已实时同步过配置。
            _cfg.Opacity = NormalOpacity();
            _cfg.WindowWidth = ActualWidth;
            _cfg.WindowHeight = ActualHeight;

            RebuildCalc();
            SaveCfg();
            CloseSettings();
            Render();
        }
    }
}
