using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MoneyCalc.Core;

namespace MoneyCalc
{
    public partial class MainWindow : Window
    {
        private readonly AppSettings _settings;
        private HolidayCalendar _calendar;
        private bool _loading = true;
        private bool _useBuiltIn = true;
        private string _lastResult = string.Empty;

        public MainWindow()
        {
            InitializeComponent();
            _settings = App.Settings;
            _useBuiltIn = _settings.UseBuiltInHolidays;
            _calendar = LoadCalendar(_settings);

            InitCurrencyCombos();
            ApplySettings();
            FillDefaults();
            _loading = false;

            UpdateThemeButton();
            RefreshAll();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            SaveSettingsFromUi();
            base.OnClosing(e);
        }

        // ---------------- 初始化 ----------------

        private static HolidayCalendar LoadCalendar(AppSettings settings)
        {
            if (settings.UseBuiltInHolidays) return HolidayCalendar.BuiltIn2025And2026();

            var calendar = new HolidayCalendar { DataNote = "自定义节假日表（你自己维护的）" };
            if (settings.Holidays != null)
                foreach (var text in settings.Holidays)
                    if (HolidayCalendar.TryParseDate(text, out var d)) calendar.Holidays.Add(d.Date);
            if (settings.MakeupWorkdays != null)
                foreach (var text in settings.MakeupWorkdays)
                    if (HolidayCalendar.TryParseDate(text, out var d)) calendar.MakeupWorkdays.Add(d.Date);
            return calendar;
        }

        private void ApplySettings()
        {
            _loading = true;
            TaxRateBox.Text = _settings.TaxRate.ToString("0.####", CultureInfo.InvariantCulture);
            Nav.SelectedIndex = IndexOfCalc(_settings.LastCalc);
            _loading = false;
            UpdatePanels();
        }

        private static int IndexOfCalc(string key)
        {
            switch (key)
            {
                case "workday": return 1;
                case "tax": return 2;
                case "date": return 3;
                case "rate": return 4;
                default: return 0;
            }
        }

        private static string CalcKeyOf(int index)
        {
            switch (index)
            {
                case 1: return "workday";
                case 2: return "tax";
                case 3: return "date";
                case 4: return "rate";
                default: return "words";
            }
        }

        private void FillDefaults()
        {
            var today = DateTime.Today;
            StartPicker.SelectedDate = today;
            RangeStart.SelectedDate = new DateTime(today.Year, today.Month, 1);
            RangeEnd.SelectedDate = today;
            DateA.SelectedDate = new DateTime(today.Year, 1, 1);
            DateB.SelectedDate = today;
            DateBase.SelectedDate = today;
            AmountBox.Text = "1234567.89";
        }

        private void InitCurrencyCombos()
        {
            FromCurrency.ItemsSource = RateCalculator.CommonCurrencies;
            ToCurrency.ItemsSource = RateCalculator.CommonCurrencies;
            FromCurrency.Text = string.IsNullOrEmpty(_settings.RateFrom) ? "USD" : _settings.RateFrom;
            ToCurrency.Text = string.IsNullOrEmpty(_settings.RateTo) ? "CNY" : _settings.RateTo;
            RefreshRateList();
        }

        private void SaveSettingsFromUi()
        {
            _settings.LastCalc = CalcKeyOf(Nav.SelectedIndex);
            _settings.TaxRate = ParseDecimal(TaxRateBox.Text, 13m);
            _settings.RateFrom = FromCurrency.Text;
            _settings.RateTo = ToCurrency.Text;
            _settings.UseBuiltInHolidays = _useBuiltIn;
            _settings.Holidays = _calendar.Holidays.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).OrderBy(s => s).ToList();
            _settings.MakeupWorkdays = _calendar.MakeupWorkdays.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).OrderBy(s => s).ToList();
            _settings.SavedRates = RateList.ItemsSource as List<RateEntry> ?? _settings.SavedRates;
            App.SaveSettings();
        }

        // ---------------- 导航 ----------------

        private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            UpdatePanels();
            RefreshAll();
            _settings.LastCalc = CalcKeyOf(Nav.SelectedIndex);
        }

        private void UpdatePanels()
        {
            int index = Math.Max(0, Nav.SelectedIndex);
            PanelWords.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            PanelWorkday.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            PanelTax.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
            PanelDate.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
            PanelRate.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshAll()
        {
            CalcWords();
            CalcWorkday();
            CalcTax();
            CalcDate();
            CalcRate();
            HolidayNote.Text = _calendar.DataNote + "（节假日表可编辑）";
        }

        // ---------------- 主题 ----------------

        private void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            bool dark = string.Equals(_settings.Theme, "dark", StringComparison.OrdinalIgnoreCase);
            _settings.Theme = dark ? "light" : "dark";
            App.ApplyTheme(Theme.Get(_settings.Theme));
            UpdateThemeButton();
            App.SaveSettings();
        }

        private void UpdateThemeButton()
        {
            bool dark = string.Equals(_settings.Theme, "dark", StringComparison.OrdinalIgnoreCase);
            ThemeButton.Content = dark ? "浅色模式" : "深色模式";
        }

        // ---------------- 1 人民币大写 ----------------

        private void Amount_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            CalcWords();
        }

        private void CalcWords()
        {
            string input = AmountBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(input))
            {
                WordsResult.Text = "零元整";
                WordsSub.Text = " ";
                AmountHint.Text = " ";
                _lastResult = string.Empty;
                return;
            }

            if (!AmountInWords.TryParse(input, out decimal value, out string error))
            {
                WordsResult.Text = "—";
                WordsSub.Text = " ";
                AmountHint.Text = error;
                _lastResult = string.Empty;
                return;
            }

            if (!AmountInWords.TryToChinese(value, out string text, out error))
            {
                WordsResult.Text = "—";
                AmountHint.Text = error;
                _lastResult = string.Empty;
                return;
            }

            WordsResult.Text = text;
            AmountHint.Text = " ";
            WordsSub.Text = "金额：" + value.ToString("#,##0.00", CultureInfo.InvariantCulture) + " ｜ 共 " + text.Length + " 个汉字";
            _lastResult = text;
        }

        private void ClearAmount_Click(object sender, RoutedEventArgs e)
        {
            AmountBox.Text = string.Empty;
            AmountBox.Focus();
        }

        private void CopyWords_Click(object sender, RoutedEventArgs e) => CopyToClipboard(_lastResult, "大写金额已复制");

        // ---------------- 2 工作日 ----------------

        private void Workday_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            CalcWorkday();
        }

        private void Workday_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            CalcWorkday();
        }

        private void CalcWorkday()
        {
            // ① 加 N 个工作日
            var start = StartPicker.SelectedDate ?? DateTime.Today;
            if (int.TryParse((WorkdayCountBox.Text ?? string.Empty).Trim(), out int n))
            {
                var target = _calendar.AddWorkdays(start, n, IncludeStartCheck.IsChecked == true);
                AddWorkdayResult.Text = target.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                                        + "（" + HolidayCalendar.WeekdayName(target) + "）";
            }
            else
            {
                AddWorkdayResult.Text = "工作日数要填整数";
            }

            // ② 区间工作日
            var a = RangeStart.SelectedDate ?? DateTime.Today;
            var b = RangeEnd.SelectedDate ?? DateTime.Today;
            int inclusive = _calendar.CountWorkdays(a, b, true);
            int exclusive = _calendar.CountWorkdays(a, b, false);
            bool reversed = b < a;

            string prefix = reversed ? "注意：结束日早于起始日 ｜ " : string.Empty;
            RangeResult.Text = prefix + "含端点 " + Math.Abs(inclusive) + " 个工作日" + (reversed ? "（反向）" : string.Empty)
                               + "　/　不含端点 " + Math.Abs(exclusive) + " 个";

            // ③ 日期差
            int natural = Math.Abs(DateMath.DaysBetween(a, b));
            int months = Math.Abs(DateMath.WholeMonthsBetween(a, b));
            int years = Math.Abs(DateMath.WholeYearsBetween(a, b));
            SpanResult.Text = "自然日 " + natural + " 天　/　整月 " + months + " 个月　/　整年 " + years + " 年"
                              + Environment.NewLine + "相差 " + DateMath.DescribeSpan(a, b)
                              + Environment.NewLine + "起 " + HolidayCalendar.WeekdayName(a)
                              + "　止 " + HolidayCalendar.WeekdayName(b);

            _lastResult = AddWorkdayResult.Text + Environment.NewLine + RangeResult.Text + Environment.NewLine + SpanResult.Text;
        }

        private void EditHolidays_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new HolidayWindow(_calendar) { Owner = this };
            if (dialog.ShowDialog() != true) return;

            _calendar = dialog.Result;
            _useBuiltIn = false;
            HolidayNote.Text = _calendar.DataNote + "（节假日表可编辑）";
            CalcWorkday();
            SaveSettingsFromUi();
            StatusText.Text = "节假日表已更新：法定节假日 " + _calendar.Holidays.Count + " 天，补班 " + _calendar.MakeupWorkdays.Count + " 天";
        }

        private void ClearHolidays_Click(object sender, RoutedEventArgs e)
        {
            _calendar = HolidayCalendar.WeekdaysOnly();
            _useBuiltIn = false;
            HolidayNote.Text = _calendar.DataNote;
            CalcWorkday();
            SaveSettingsFromUi();
            StatusText.Text = "已清空节假日表，现在只按周一到周五算。";
        }

        private void CopyWorkday_Click(object sender, RoutedEventArgs e) => CopyToClipboard(_lastResult, "工作日结果已复制");

        // ---------------- 3 含税 ----------------

        private void Tax_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            CalcTax();
        }

        private void Tax_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            CalcTax();
        }

        private void CalcTax()
        {
            decimal amount = ParseDecimal(TaxAmountBox.Text, 0m);
            decimal rate = ParseDecimal(TaxRateBox.Text, 13m);

            try
            {
                var result = FromGrossRadio.IsChecked == true
                    ? TaxCalculator.FromGross(amount, rate)
                    : TaxCalculator.FromNet(amount, rate);

                NetText.Text = result.Net.ToString("#,##0.00", CultureInfo.InvariantCulture);
                TaxText.Text = result.Tax.ToString("#,##0.00", CultureInfo.InvariantCulture);
                GrossText.Text = result.Gross.ToString("#,##0.00", CultureInfo.InvariantCulture);
                TaxExactText.Text = "高精度（4 位）：不含税 " + result.NetExact.ToString("0.0000", CultureInfo.InvariantCulture)
                                    + " ／ 税额 " + result.TaxExact.ToString("0.0000", CultureInfo.InvariantCulture)
                                    + " ／ 含税 " + result.GrossExact.ToString("0.0000", CultureInfo.InvariantCulture);
                TaxHint.Text = result.IsConsistent ? "不含税 + 税额 = 含税，一分不差" : "自洽检查失败，请反馈";
                StatusText.Text = "税率 " + rate.ToString("0.####", CultureInfo.InvariantCulture) + "%　"
                                  + (FromGrossRadio.IsChecked == true ? "由含税反推" : "由不含税正推");

                _lastResult = "不含税 " + result.Net.ToString("0.00", CultureInfo.InvariantCulture)
                              + "，税额 " + result.Tax.ToString("0.00", CultureInfo.InvariantCulture)
                              + "，含税 " + result.Gross.ToString("0.00", CultureInfo.InvariantCulture);
            }
            catch (FriendlyException ex)
            {
                NetText.Text = TaxText.Text = GrossText.Text = "—";
                TaxExactText.Text = " ";
                TaxHint.Text = ex.Message;
                _lastResult = string.Empty;
            }
        }

        private void Rate13_Click(object sender, RoutedEventArgs e) => SetRate(13m);
        private void Rate9_Click(object sender, RoutedEventArgs e) => SetRate(9m);
        private void Rate6_Click(object sender, RoutedEventArgs e) => SetRate(6m);
        private void Rate5_Click(object sender, RoutedEventArgs e) => SetRate(5m);
        private void Rate3_Click(object sender, RoutedEventArgs e) => SetRate(3m);
        private void Rate1_Click(object sender, RoutedEventArgs e) => SetRate(1m);

        private void SetRate(decimal rate)
        {
            TaxRateBox.Text = rate.ToString("0.####", CultureInfo.InvariantCulture);
            CalcTax();
        }

        private void CopyTax_Click(object sender, RoutedEventArgs e) => CopyToClipboard(_lastResult, "含税结果已复制");

        // ---------------- 4 日期 ----------------

        private void Date_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            CalcDate();
        }

        private void Date_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            CalcDate();
        }

        private void CalcDate()
        {
            var a = DateA.SelectedDate ?? DateTime.Today;
            var b = DateB.SelectedDate ?? DateTime.Today;

            int days = DateMath.DaysBetween(a, b);
            int workdays = _calendar.CountWorkdays(a, b, true);
            int months = DateMath.WholeMonthsBetween(a, b);
            int years = DateMath.WholeYearsBetween(a, b);

            DateDiffResult.Text =
                "相差 " + Math.Abs(days) + " 天（自然日）" + Environment.NewLine
                + "工作日 " + Math.Abs(workdays) + " 天（含端点）" + Environment.NewLine
                + "整月 " + Math.Abs(months) + " 个月　整年 " + Math.Abs(years) + " 年" + Environment.NewLine
                + DateMath.DescribeSpan(a, b) + Environment.NewLine
                + "起始 " + a.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " " + HolidayCalendar.WeekdayName(a)
                + "　结束 " + b.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " " + HolidayCalendar.WeekdayName(b);

            var baseDate = DateBase.SelectedDate ?? DateTime.Today;
            if (!int.TryParse((AmountBox2.Text ?? string.Empty).Trim(), out int amount))
            {
                DateAddResult.Text = "要加的数量请填整数";
                _lastResult = DateDiffResult.Text;
                return;
            }

            DateTime added;
            string unit;
            if (UnitCombo.SelectedIndex == 1) { added = DateMath.AddMonths(baseDate, amount); unit = " 个月"; }
            else if (UnitCombo.SelectedIndex == 2) { added = DateMath.AddYears(baseDate, amount); unit = " 年"; }
            else { added = DateMath.AddDays(baseDate, amount); unit = " 天"; }

            DateAddResult.Text = baseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " 加 " + amount + unit
                                 + " → " + added.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                                 + "（" + HolidayCalendar.WeekdayName(added) + "）";

            _lastResult = DateDiffResult.Text + Environment.NewLine + DateAddResult.Text;
        }

        private void CopyDate_Click(object sender, RoutedEventArgs e) => CopyToClipboard(_lastResult, "日期结果已复制");

        // ---------------- 5 汇率 ----------------

        private void Rate_Changed(object sender, EventArgs e)
        {
            if (_loading) return;
            CalcRate();
        }

        private void CalcRate()
        {
            string from = (FromCurrency.Text ?? string.Empty).Trim().ToUpperInvariant();
            string to = (ToCurrency.Text ?? string.Empty).Trim().ToUpperInvariant();
            decimal amount = ParseDecimal(RateAmountBox.Text, 0m);

            if (!RateCalculator.TryParseDecimal(RateBox.Text, out decimal rate))
            {
                RateForward.Text = "汇率要填数字";
                RateBackward.Text = " ";
                _lastResult = string.Empty;
                return;
            }

            try
            {
                decimal forward = RateCalculator.Convert(amount, rate);
                decimal backward = RateCalculator.Convert(amount, RateCalculator.Inverse(rate));

                RateForward.Text = amount.ToString("#,##0.####", CultureInfo.InvariantCulture) + " " + from
                                   + " = " + forward.ToString("#,##0.0000", CultureInfo.InvariantCulture) + " " + to;
                RateBackward.Text = "反算：" + amount.ToString("#,##0.####", CultureInfo.InvariantCulture) + " " + to
                                    + " = " + backward.ToString("#,##0.0000", CultureInfo.InvariantCulture) + " " + from
                                    + "　（1 " + to + " = " + RateCalculator.Inverse(rate).ToString("0.0000", CultureInfo.InvariantCulture) + " " + from + "）";
                _lastResult = RateForward.Text;
            }
            catch (FriendlyException ex)
            {
                RateForward.Text = ex.Message;
                RateBackward.Text = " ";
                _lastResult = string.Empty;
            }
        }

        private void SaveRate_Click(object sender, RoutedEventArgs e)
        {
            if (!RateCalculator.TryParseDecimal(RateBox.Text, out decimal rate) || rate <= 0)
            {
                StatusText.Text = "汇率要填一个大于 0 的数字。";
                return;
            }

            string from = (FromCurrency.Text ?? "USD").Trim().ToUpperInvariant();
            string to = (ToCurrency.Text ?? "CNY").Trim().ToUpperInvariant();
            var entry = new RateEntry { From = from, To = to, Rate = rate, SavedAt = DateTime.Now };

            var list = RateCalculator.Add(RateList.ItemsSource as List<RateEntry> ?? new List<RateEntry>(), entry);
            RateList.ItemsSource = list;
            _settings.SavedRates = list;
            App.SaveSettings();
            StatusText.Text = "已存为常用汇率：" + entry.Display;
        }

        private void RefreshRateList()
        {
            var list = _settings.SavedRates ?? new List<RateEntry>();
            RateList.ItemsSource = list;
            RateList.DisplayMemberPath = "Display";
        }

        private void RateList_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!(RateList.SelectedItem is RateEntry entry)) return;

            _loading = true;
            FromCurrency.Text = entry.From;
            ToCurrency.Text = entry.To;
            RateBox.Text = entry.Rate.ToString("0.####", CultureInfo.InvariantCulture);
            _loading = false;
            CalcRate();
            StatusText.Text = "已填入：" + entry.Display;
        }

        private void ClearRates_Click(object sender, RoutedEventArgs e)
        {
            RateList.ItemsSource = new List<RateEntry>();
            _settings.SavedRates = new List<RateEntry>();
            App.SaveSettings();
            StatusText.Text = "常用汇率已清空。";
        }

        private void CopyRate_Click(object sender, RoutedEventArgs e) => CopyToClipboard(_lastResult, "汇率结果已复制");

        // ---------------- 键盘 ----------------

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Enter 跳到下一个输入框，而不是弹按钮
                if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is DatePicker)
                {
                    (Keyboard.FocusedElement as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                    e.Handled = true;
                }
                return;
            }

            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // 焦点不在输入框里时，Ctrl+C 复制当前计算器的结果
                if (!(Keyboard.FocusedElement is TextBox) && !(Keyboard.FocusedElement is ComboBox))
                {
                    CopyToClipboard(_lastResult, "已复制结果");
                    e.Handled = true;
                }
            }
        }

        // ---------------- 小工具 ----------------

        private static decimal ParseDecimal(string text, decimal fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            string clean = text.Trim().Replace(",", string.Empty);
            return decimal.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value) ? value : fallback;
        }

        private void CopyToClipboard(string text, string message)
        {
            if (string.IsNullOrEmpty(text))
            {
                StatusText.Text = "还没有结果可复制。";
                return;
            }

            try
            {
                Clipboard.SetText(text);
                StatusText.Text = message;
            }
            catch (Exception ex)
            {
                StatusText.Text = "复制失败：" + ex.Message;
            }
        }
    }
}
