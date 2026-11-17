using System;
using System.Windows;
using System.Windows.Media;
using TableDiff.Core;

namespace TableDiff
{
    public partial class App : Application
    {
        /// <summary>数据目录：%APPDATA%\TableDiff。配置、日志都写这里，绝不写程序目录。</summary>
        public static readonly string DataDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TableDiff");

        /// <summary>当前设置（整个进程共用一份，改完即存）。</summary>
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
            ExcelReader.CellErrorHandler = (where, ex) => Log.Error(where, ex);

            Settings = SettingsStore.Load(DataDir);
            ApplyTheme(Theme.Get(Settings.Theme));

            // 首次运行先落一份默认配置，方便用户直接改，也证明数据目录可用
            try
            {
                if (!System.IO.File.Exists(SettingsStore.PathIn(DataDir)))
                    SettingsStore.Save(DataDir, Settings);
            }
            catch (Exception ex) { Log.Error("写入默认设置失败", ex); }

            base.OnStartup(e);

            // 命令行：--maketestdata <目录> 生成验收用样例数据
            string dir = Cli.ValueOf(e.Args, "--maketestdata");
            if (dir != null)
            {
                string target = string.IsNullOrWhiteSpace(dir) ? DataDir : dir;
                try
                {
                    string message = TestDataGenerator.Generate(target);
                    Log.Info("生成样例数据: " + target);
                    ReportCli(message, "样例数据已生成");
                }
                catch (Exception ex)
                {
                    Log.Error("生成样例数据失败", ex);
                    ReportCli("生成失败：" + ex.Message, "生成样例数据失败");
                }
                Shutdown(0);
                return;
            }

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }

        /// <summary>
        /// 命令行模式的结果不能弹模态框（脚本 / CI 会一直卡在那儿等人点确定），
        /// 统一写进 %APPDATA%\TableDiff\cli-result.txt，同时记一行日志。
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

        /// <summary>把主题色刷进 Application.Resources，XAML 里的 DynamicResource 会自动跟着变。</summary>
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

    /// <summary>极简命令行解析：只认 --名字 值 这种形式。</summary>
    public static class Cli
    {
        public static string ValueOf(string[] args, string name)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    return args[i + 1].Trim('"');
                return string.Empty;
            }
            return null;
        }

        public static bool Has(string[] args, string name)
        {
            if (args == null) return false;
            foreach (var a in args)
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
