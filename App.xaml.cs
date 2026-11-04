using System;
using System.Windows;
using System.Windows.Threading;

namespace WorkEncourager
{
    public partial class App : Application
    {
        /// <summary>
        /// 调试模式：以 -- 开头的参数启动时置为 true。
        /// 调试运行使用独立的配置与历史文件，绝不碰用户真实数据。
        /// </summary>
        public static bool DebugMode { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnUnhandled;

            DebugMode = HasDebugFlag(e.Args);

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
                    win.Width = Math.Max(260, sw);
                    win.Height = Math.Max(130, sh2);
                }
            }

            win.Show();

            if (Array.IndexOf(e.Args, "--open-settings") >= 0)
                win.ShowSettingsForDebug();

            if (Array.IndexOf(e.Args, "--open-stats") >= 0)
                win.ShowStatsForDebug();

            if (Array.IndexOf(e.Args, "--open-detail") >= 0)
                win.ShowDetailForDebug();

            // 调试用：--mini 以迷你挂件模式启动
            if (Array.IndexOf(e.Args, "--mini") >= 0)
                win.ShowMiniForDebug();

            // 调试用：--paused 以暂停状态启动（退出时不把暂停状态写回配置）
            if (Array.IndexOf(e.Args, "--paused") >= 0)
                win.ShowPausedForDebug();

            // 调试用：--shot <路径> 渲染窗口截图后退出
            // 配合 --gap <毫秒> 可在同一个进程内连拍两张（第二张存为 <路径>.t2.png），
            // 用来验证"暂停后数字是否真的冻结"
            int idx = Array.IndexOf(e.Args, "--shot");
            if (idx >= 0 && idx + 1 < e.Args.Length)
            {
                string path = e.Args[idx + 1];
                int gap = 4000;
                int gi = Array.IndexOf(e.Args, "--gap");
                if (gi >= 0 && gi + 1 < e.Args.Length) int.TryParse(e.Args[gi + 1], out gap);

                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                int step = 0;
                t.Tick += (s, ev) =>
                {
                    step++;
                    if (step == 1)
                    {
                        try { win.SaveShot(path); } catch (Exception ex) { Console.WriteLine(ex); }
                        t.Interval = TimeSpan.FromMilliseconds(Math.Max(500, gap));
                    }
                    else
                    {
                        t.Stop();
                        try { win.SaveShot(path + ".t2.png"); } catch (Exception ex) { Console.WriteLine(ex); }
                        Shutdown();
                    }
                };
                t.Start();
            }
        }

        /// <summary>判断是否带调试参数。</summary>
        private static bool HasDebugFlag(string[] args)
        {
            if (args == null) return false;
            foreach (var a in args)
                if (a != null && a.StartsWith("--", StringComparison.Ordinal)) return true;
            return false;
        }

        private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show("程序出了点小状况：\n" + e.Exception.Message, "上班鼓励器",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }
    }
}
