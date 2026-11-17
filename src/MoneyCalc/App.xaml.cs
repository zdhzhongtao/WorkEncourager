using System;
using System.Windows;
using System.Windows.Media;
using MoneyCalc.Core;

namespace MoneyCalc
{
    public partial class App : Application
    {
        /// <summary>数据目录：%APPDATA%\MoneyCalc。</summary>
        public static readonly string DataDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MoneyCalc");

        public static AppSettings Settings { get; private set; } = new AppSettings();

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                System.IO.Directory.CreateDirectory(DataDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法创建数据目录：" + DataDir + "\n" + ex.Message, "启动失败");
                Shutdown(1);
                return;
            }

            Log.Init(DataDir);
            AtomicFile.ErrorHandler = (where, ex) => Log.Error(where, ex);

            Settings = SettingsStore.Load(DataDir);
            ApplyTheme(Theme.Get(Settings.Theme));

            try
            {
                if (!System.IO.File.Exists(SettingsStore.PathIn(DataDir)))
                    SettingsStore.Save(DataDir, Settings);
            }
            catch (Exception ex) { Log.Error("写入默认设置失败", ex); }

            base.OnStartup(e);

            if (Cli.Has(e.Args, "--demo"))
            {
                string message =
                    "1234567.89 → " + AmountInWords.ToChinese(1234567.89m) + Environment.NewLine
                    + "1000000.00 → " + AmountInWords.ToChinese(1000000.00m) + Environment.NewLine
                    + "1130 含税 13% → 不含税 " + TaxCalculator.FromGross(1130m, 13m).Net + " / 税额 "
                    + TaxCalculator.FromGross(1130m, 13m).Tax + Environment.NewLine
                    + "1月31日 + 1 个月 → " + DateMath.AddMonths(new DateTime(2026, 1, 31), 1).ToString("yyyy-MM-dd");
                ReportCli(message, "自检结果");
                Shutdown(0);
                return;
            }

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }

        /// <summary>
        /// 命令行模式的结果不能弹模态框（脚本 / CI 会一直卡在那儿等人点确定），
        /// 统一写进 %APPDATA%\MoneyCalc\cli-result.txt，同时记一行日志。
        /// </summary>
        private static void ReportCli(string text, string title)
        {
            try
            {
                string path = System.IO.Path.Combine(DataDir, "cli-result.txt");
                System.IO.File.WriteAllText(path, (text ?? string.Empty).Replace("\r\n", "\n"),
                    new System.Text.UTF8Encoding(true));
                Log.Info(title + " → " + path);
            }
            catch (Exception ex)
            {
                Log.Error("写命令行结果失败", ex);
            }
        }

        public static void ApplyTheme(Theme theme)
        {
            if (theme == null) return;
            foreach (var kv in theme.ToResourceMap())
            {
                var brush = new SolidColorBrush(ParseColor(kv.Value));
                brush.Freeze();
                Current.Resources[kv.Key] = brush;
            }
        }

        private static Color ParseColor(string text)
        {
            try
            {
                object c = ColorConverter.ConvertFromString(text);
                if (c is Color color) return color;
            }
            catch { }
            return Colors.Transparent;
        }

        public static void SaveSettings()
        {
            try { SettingsStore.Save(DataDir, Settings); }
            catch (Exception ex) { Log.Error("保存设置失败", ex); }
        }
    }

    /// <summary>极简命令行解析。</summary>
    public static class Cli
    {
        public static bool Has(string[] args, string name)
        {
            if (args == null) return false;
            foreach (var a in args)
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
