using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WorkEncourager
{
    /// <summary>Win32 辅助：鼠标穿透、全局热键、单实例唤醒。</summary>
    public static class Win32
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        /// <summary>不在任务栏 / Alt+Tab 显示的窗口样式。保留备用。</summary>
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        /// <summary>命中测试消息。返回 HTTRANSPARENT 可让点击落到下层窗口。</summary>
        public const int WM_NCHITTEST = 0x0084;

        /// <summary>"这次点击不归我"，交给下层窗口。</summary>
        public const int HTTRANSPARENT = -1;

        /// <summary>全局热键触发的消息。</summary>
        public const int WM_HOTKEY = 0x0312;

        public const int VK_MENU = 0x12;      // Alt
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_NOREPEAT = 0x4000;

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

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        /// <summary>
        /// 应用"鼠标穿透"的窗口样式部分。
        ///
        /// v1.0.1 之前用 WS_EX_TRANSPARENT 一票否决，窗口连 WM_NCHITTEST 都收不到，
        /// 于是穿透一开就再也点不到任何按钮、关不掉、也切不回来——彻底卡死。
        /// 现在穿透由 WM_NCHITTEST 返回 HTTRANSPARENT 实现（见 MainWindow.WndProc）：
        /// 点击照样穿透到下层，但窗口仍能收到命中测试消息，从而保住"标题栏可点"和 Alt 逃生。
        /// </summary>
        /// <returns>窗口句柄尚未创建时返回 false。</returns>
        public static bool SetClickThrough(Window window, bool enable)
        {
            if (window == null) return false;
            var helper = new WindowInteropHelper(window);
            if (helper.Handle == IntPtr.Zero) return false;

            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);

            // 必须清掉历史遗留的 WS_EX_TRANSPARENT，否则新逻辑收不到 NCHITTEST
            exStyle &= ~WS_EX_TRANSPARENT;

            // 穿透时不要抢焦点，否则点标题栏会把前台窗口切走
            if (enable) exStyle |= WS_EX_NOACTIVATE;
            else exStyle &= ~WS_EX_NOACTIVATE;

            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle);
            return true;
        }
    }
}
