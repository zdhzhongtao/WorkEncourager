using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WorkEncourager
{
    /// <summary>Win32 辅助：鼠标穿透。</summary>
    public static class Win32
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        /// <summary>
        /// 设置窗口鼠标穿透。穿透后点击会落到下层窗口，但本窗口仍可被拖动（通过 Alt 修饰）。
        /// </summary>
        public static void SetClickThrough(Window window, bool enable)
        {
            if (window == null) return;
            var helper = new WindowInteropHelper(window);
            if (helper.Handle == IntPtr.Zero) return;

            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            if (enable)
            {
                // TRANSPARENT 让鼠标事件穿透；NOACTIVATE 避免抢焦点
                exStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE;
            }
            else
            {
                exStyle &= ~WS_EX_TRANSPARENT;
                exStyle &= ~WS_EX_NOACTIVATE;
            }
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle);
        }
    }
}
