using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WorkEncourager
{
    /// <summary>Win32 辅助：鼠标穿透、单实例唤醒。</summary>
    public static class Win32
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        /// <summary>不在任务栏 / Alt+Tab 显示的窗口样式。改由托盘常驻后由 ShowInTaskbar 控制，保留备用。</summary>
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        /// <summary>广播消息的目标句柄。</summary>
        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);

        /// <summary>
        /// 自定义广播消息："把已经运行的实例叫到前面来"。
        /// 第二个实例启动时广播一次，第一个实例收到后自行显示窗口。
        /// </summary>
        public static readonly int WM_SHOW_INSTANCE = unchecked((int)RegisterWindowMessage("WorkEncourager.ShowInstance.v2"));

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// 设置窗口鼠标穿透。穿透后点击会落到下层窗口。
        /// </summary>
        /// <returns>
        /// 窗口句柄尚未创建时返回 false —— 调用方必须等到 SourceInitialized 之后再调用，
        /// 否则穿透设置会被静默丢弃（v1.0.0 的 bug）。
        /// </returns>
        public static bool SetClickThrough(Window window, bool enable)
        {
            if (window == null) return false;
            var helper = new WindowInteropHelper(window);
            if (helper.Handle == IntPtr.Zero) return false;

            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            if (enable)
            {
                // TRANSPARENT 让鼠标事件穿透；NOACTIVATE 避免抢焦点
                exStyle |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
            }
            else
            {
                exStyle &= ~WS_EX_TRANSPARENT;
                exStyle &= ~WS_EX_NOACTIVATE;
            }
            // 分层样式（LAYERED）由 AllowsTransparency 负责，这里既不置也不清，避免把窗口画黑
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle);
            return true;
        }
    }
}
