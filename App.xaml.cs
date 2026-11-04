using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using WorkEnc.Core;

namespace WorkEncourager
{
    public partial class App : Application
    {
        /// <summary>
        /// 调试模式：以 -- 开头的参数启动时置为 true。
        /// 调试运行使用独立的配置与历史文件，绝不碰用户真实数据。
        /// </summary>
        public static bool DebugMode { get; private set; }

        /// <summary>单实例锁。持有它直到进程结束，第二个实例会直接退出。</summary>
        private Mutex _instanceLock;

        /// <summary>上次弹错误框的时刻，用于防止异常风暴把用户淹没。</summary>
        private DateTime _lastErrorShown = DateTime.MinValue;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnUnhandled;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandled;

            DebugMode = HasDebugFlag(e.Args);

            // 让 Core 层的文件读写失败也进日志
            AtomicFile.ErrorHandler = (msg, ex) =>
            {
                if (ex == null) Log.Info(msg);
                else Log.Error(msg, ex);
            };

            Log.Info("启动，参数：" + (e.Args.Length == 0 ? "(无)" : string.Join(" ", e.Args)));

            // 调试参数需要多开（--shot 连拍等），只有正常启动才做单实例限制
            if (!DebugMode && !AcquireSingleInstance())
            {
                Log.Info("已有实例在运行，本次启动退出");
                Shutdown();
                return;
            }

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
                        try { win.SaveShot(path); } catch (Exception ex) { Log.Error("截图失败", ex); }
                        t.Interval = TimeSpan.FromMilliseconds(Math.Max(500, gap));
                    }
                    else
                    {
                        t.Stop();
                        try { win.SaveShot(path + ".t2.png"); } catch (Exception ex) { Log.Error("截图失败", ex); }
                        Shutdown();
                    }
                };
                t.Start();
            }
        }

        /// <summary>
        /// 抢单实例锁。抢不到就广播一条消息，让已经运行的那个实例把窗口弹出来。
        /// </summary>
        private bool AcquireSingleInstance()
        {
            try
            {
                _instanceLock = new Mutex(true, @"Local\WorkEncourager.SingleInstance", out bool createdNew);
                if (createdNew) return true;

                // 已有实例：请它显示窗口，然后本次进程退出
                Win32.PostMessage(Win32.HWND_BROADCAST, Win32.WM_SHOW_INSTANCE, IntPtr.Zero, IntPtr.Zero);
                _instanceLock.Dispose();
                _instanceLock = null;
                return false;
            }
            catch (Exception ex)
            {
                // 锁机制本身出错不应该阻止程序启动
                Log.Error("单实例锁创建失败", ex);
                return true;
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
            Log.Error("未处理异常", e.Exception);
            e.Handled = true;

            // 定时器每 200ms tick，一旦持续抛异常会变成弹窗风暴；10 秒内只提示一次
            if ((DateTime.Now - _lastErrorShown).TotalSeconds < 10) return;
            _lastErrorShown = DateTime.Now;

            MessageBox.Show("程序出了点小状况：\n" + e.Exception.Message + "\n\n详细信息已写入日志：\n" + Log.FilePath,
                "上班鼓励器", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void OnDomainUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex) Log.Error("致命异常", ex);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Info("退出，代码 " + e.ApplicationExitCode);
            try
            {
                _instanceLock?.ReleaseMutex();
                _instanceLock?.Dispose();
            }
            catch { }
            _instanceLock = null;
            base.OnExit(e);
        }
    }
}
