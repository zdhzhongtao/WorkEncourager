using System;
using System.Windows;
using System.Windows.Threading;

namespace WorkEncourager
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnUnhandled;

            var win = new MainWindow();
            MainWindow = win;

            // 调试用：--size 宽x高 指定初始窗口尺寸
            int si = Array.IndexOf(e.Args, "--size");
            if (si >= 0 && si + 1 < e.Args.Length)
            {
                var parts = e.Args[si + 1].ToLower().Split('x');
                if (parts.Length == 2 &&
                    double.TryParse(parts[0], out double sw) && double.TryParse(parts[1], out double sh2))
                {
                    win.Width = Math.Max(360, sw);
                    win.Height = Math.Max(400, sh2);
                }
            }

            win.Show();

            if (Array.IndexOf(e.Args, "--open-settings") >= 0)
                win.ShowSettingsForDebug();

            if (Array.IndexOf(e.Args, "--open-stats") >= 0)
                win.ShowStatsForDebug();

            // 调试用：--shot <路径> 渲染窗口截图后退出
            int idx = Array.IndexOf(e.Args, "--shot");
            if (idx >= 0 && idx + 1 < e.Args.Length)
            {
                string path = e.Args[idx + 1];
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                t.Tick += (s, ev) =>
                {
                    t.Stop();
                    try { win.SaveShot(path); } catch (Exception ex) { Console.WriteLine(ex); }
                    Shutdown();
                };
                t.Start();
            }
        }

        private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show("程序出了点小状况：\n" + e.Exception.Message, "上班鼓励器",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }
    }
}
