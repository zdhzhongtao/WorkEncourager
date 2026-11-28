using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MdViewer
{
    /// <summary>
    /// 快捷键的「第二入口」：键盘焦点在 WebView2 正文里时，WPF 的 Window.PreviewKeyDown
    /// 收不到键（WebView2 是原生子窗口），而 `AreBrowserAcceleratorKeysEnabled=false`
    /// 又让 Chromium 不处理这些组合键。这里装一个本进程的 WH_KEYBOARD_LL 低级键盘钩子：
    /// 只在**本程序是前台**时生效，认出自己的快捷键就执行并吞掉，其余原样放行。
    ///
    /// 为什么不用 SetWindowSubclass：WebView2 的渲染窗口属于 msedgewebview2.exe，
    /// 跨进程无法子类化（实测 SetWindowSubclass 直接返回 false）。
    /// </summary>
    internal static class WebViewKeyboardHook
    {
        private const int WH_KEYBOARD_LL = 13;
        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_SYSKEYDOWN = 0x0104;

        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback,
            IntPtr module, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int nVirtKey);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        private static IntPtr _hook;
        private static LowLevelKeyboardProc _proc;
        private static Window _window;
        private static Action<string> _onAction;
        private static uint _ownPid;

        /// <summary>在主窗口 Loaded 之后调用一次。</summary>
        public static void Install(Window window, Action<string> onAction)
        {
            if (_hook != IntPtr.Zero || window == null || onAction == null) return;

            _window = window;
            _onAction = onAction;
            _ownPid = (uint)Process.GetCurrentProcess().Id;
            _proc = Callback;

            try
            {
                _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
                if (_hook == IntPtr.Zero)
                    Log.Error("安装键盘钩子失败，error=" + Marshal.GetLastWin32Error(), null);
                else
                    Log.Info("已安装低级键盘钩子（正文焦点下快捷键可用）");
            }
            catch (Exception ex)
            {
                Log.Error("安装键盘钩子异常", ex);
            }
        }

        public static void Uninstall()
        {
            if (_hook == IntPtr.Zero) return;
            try { UnhookWindowsHookEx(_hook); }
            catch (Exception) { }
            _hook = IntPtr.Zero;
        }

        private static IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                uint message = (uint)wParam.ToInt64();
                if (nCode >= 0 && (message == WM_KEYDOWN || message == WM_SYSKEYDOWN) && IsOursForeground())
                {
                    KBDLLHOOKSTRUCT data = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                    bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                    bool shift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                    bool alt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

                    string action = MapKey(data.vkCode, ctrl, shift, alt);
                    if (action != null)
                    {
                        Post(action);

                        // Esc 还要让页面自己关灯箱/气泡，不吞
                        if (action != "escape") return (IntPtr)1;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("键盘钩子处理失败", ex);
            }

            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        /// <summary>只有本程序在前台时才抢键，别影响别的程序。</summary>
        private static bool IsOursForeground()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;
            uint pid;
            GetWindowThreadProcessId(foreground, out pid);
            return pid == _ownPid;
        }

        private static void Post(string action)
        {
            Window window = _window;
            Action<string> handler = _onAction;
            if (window == null || handler == null) return;

            // 钩子回调里不要做重活，切回 UI 线程再执行
            _ = window.Dispatcher.BeginInvoke(new Action(() =>
            {
                try { handler(action); }
                catch (Exception ex) { Log.Error("执行快捷键失败：" + action, ex); }
            }));
        }

        private static string MapKey(int vk, bool ctrl, bool shift, bool alt)
        {
            const int VK_O = 0x4F, VK_P = 0x50, VK_F = 0x46, VK_E = 0x45, VK_T = 0x54;
            const int VK_D = 0x44, VK_W = 0x57, VK_R = 0x52, VK_B = 0x42, VK_S = 0x53;
            const int VK_OEM_COMMA = 0xBC, VK_OEM_PLUS = 0xBB, VK_OEM_MINUS = 0xBD, VK_0 = 0x30;
            const int VK_F5 = 0x74, VK_F11 = 0x7A, VK_ESCAPE = 0x1B, VK_LEFT = 0x25, VK_RIGHT = 0x27;

            if (ctrl && shift)
            {
                switch (vk)
                {
                    case VK_E: return "editor";
                    case VK_D: return "theme";
                    case VK_W: return "wide";
                    case VK_R: return "speak";
                    case VK_B: return "bookmark";
                    case VK_S: return "exportPng";
                }
                return null;
            }

            if (ctrl)
            {
                switch (vk)
                {
                    case VK_O: return "open";
                    case VK_F: return "find";
                    case VK_P: return "exportPdf";
                    case VK_E: return "recent";
                    case VK_T: return "toc";
                    case VK_OEM_COMMA: return "settings";
                    case VK_OEM_PLUS:
                    case 0x6B: return "fontUp";      // 小键盘 +
                    case VK_OEM_MINUS:
                    case 0x6D: return "fontDown";    // 小键盘 -
                    case VK_0:
                    case 0x60: return "fontReset";   // 小键盘 0
                }
                return null;
            }

            if (vk == VK_F11) return "zen";
            if (vk == VK_F5) return "reload";
            if (vk == VK_ESCAPE) return "escape";
            if (alt && vk == VK_LEFT) return "back";
            if (alt && vk == VK_RIGHT) return "forward";
            return null;
        }
    }
}
