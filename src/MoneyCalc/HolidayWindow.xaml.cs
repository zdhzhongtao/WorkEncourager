using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using MoneyCalc.Core;

namespace MoneyCalc
{
    /// <summary>节假日表编辑：一行一个 yyyy-MM-dd。</summary>
    public partial class HolidayWindow : Window
    {
        public HolidayCalendar Result { get; private set; }

        public HolidayWindow(HolidayCalendar current)
        {
            InitializeComponent();

            var source = current ?? HolidayCalendar.BuiltIn2025And2026();
            HolidayBox.Text = string.Join(Environment.NewLine,
                source.Holidays.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).OrderBy(s => s));
            MakeupBox.Text = string.Join(Environment.NewLine,
                source.MakeupWorkdays.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).OrderBy(s => s));

            HintText.Text = "当前共 " + source.Holidays.Count + " 天假、" + source.MakeupWorkdays.Count + " 天补班。";
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!TryParseList(HolidayBox.Text, out var holidays, out string error))
            {
                MessageBox.Show("「法定节假日」里有认不出的日期：" + error + "\n请按 2026-10-01 这样的格式一行一个。",
                    "格式不对", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParseList(MakeupBox.Text, out var makeup, out error))
            {
                MessageBox.Show("「调休补班」里有认不出的日期：" + error + "\n请按 2025-01-26 这样的格式一行一个。",
                    "格式不对", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var calendar = new HolidayCalendar { DataNote = "自定义节假日表（你自己维护的）" };
            foreach (var d in holidays) calendar.Holidays.Add(d);
            foreach (var d in makeup) calendar.MakeupWorkdays.Add(d);

            Result = calendar;
            DialogResult = true;
        }

        private static bool TryParseList(string text, out List<DateTime> dates, out string error)
        {
            dates = new List<DateTime>();
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(text)) return true;

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var line in lines)
            {
                string item = (line ?? string.Empty).Trim();
                if (item.Length == 0) continue;
                if (!HolidayCalendar.TryParseDate(item, out DateTime date))
                {
                    error = item;
                    return false;
                }
                dates.Add(date.Date);
            }
            return true;
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            HolidayBox.Text = string.Empty;
            MakeupBox.Text = string.Empty;
            HintText.Text = "清空后只按周一到周五算工作日。";
        }

        /// <summary>改乱了想重来：把内置表重新填回两个框里（点确定才真正生效）。</summary>
        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            var builtIn = HolidayCalendar.BuiltIn2025And2026();
            HolidayBox.Text = string.Join(Environment.NewLine,
                builtIn.Holidays.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).OrderBy(s => s));
            MakeupBox.Text = string.Join(Environment.NewLine,
                builtIn.MakeupWorkdays.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).OrderBy(s => s));
            HintText.Text = "已填回内置表：" + builtIn.DataNote + "。点「确定」生效。";
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
