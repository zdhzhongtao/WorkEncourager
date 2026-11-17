using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WorkEnc.Core;

namespace WorkEncourager
{
    public partial class MainWindow : Window
{
        // ---------------- 托盘常驻 ----------------

        private void InitTray()
        {
            try
            {
                _tray = new TrayIcon();
                _tray.ToggleRequested += ToggleWindowVisibility;
                _tray.ModeSwitchRequested += () =>
                {
                    ShowFromTray();
                    SetViewMode(_viewMode == AppViewMode.Mini ? AppViewMode.Full : AppViewMode.Mini);
                };
                _tray.SettingsRequested += () =>
                {
                    ShowFromTray();
                    // 穿透状态下窗口根本点不到，先进设置就得恢复可点击，否则是个死路
                    if (_cfg.ClickThrough)
                    {
                        _cfg.ClickThrough = false;
                        ApplyClickThrough();
                        SaveCfg();
                    }
                    SelectTab(MainTab.Settings);
                };
                _tray.ClickThroughToggleRequested += ToggleClickThrough;
                _tray.PauseToggleRequested += () => OnToggleClick(null, null);
                _tray.ResetWindowRequested += ResetWindowLayout;
                _tray.ExitRequested += ExitApp;
                _tray.SetWindowVisible(IsVisible);
                _tray.SetMode(_viewMode == AppViewMode.Mini);
                _tray.SetClickThrough(_cfg.ClickThrough);
                _tray.SetPaused(_paused);
            }
            catch (Exception ex)
            {
                // 托盘创建失败不能导致主程序起不来
                Log.Error("创建托盘图标失败", ex);
            }
        }

        /// <summary>左键点托盘：可见就收起来，隐藏就弹回来。</summary>
        private void ToggleWindowVisibility()
        {
            if (IsVisible) HideToTray();
            else ShowFromTray();
        }

        /// <summary>
        /// 隐藏到托盘：只是不显示窗口，计时器继续跑。
        /// 这是"关闭按钮"的新语义——真正退出走托盘右键菜单。
        /// </summary>
        private void HideToTray()
        {
            if (!IsVisible) return;

            // 先把当前几何状态落盘（Hide 不会触发 Closing）
            RememberBounds();
            SaveCfg();

            Hide();
            _tray?.SetWindowVisible(false);

            if (!_hiddenOnce)
            {
                _hiddenOnce = true;
                _tray?.ShowBalloon("还在后台计时",
                    "窗口已隐藏，计时不会停。左键点托盘图标可以唤回来，右键可退出。");
            }
        }

        /// <summary>从托盘恢复窗口并置前。</summary>
        private void ShowFromTray()
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

            // 隐藏期间可能换过显示器/改了分辨率
            EnsureWindowVisible();

            Topmost = false;
            Topmost = _cfg.Topmost;
            Activate();
            _tray?.SetWindowVisible(true);
        }

        /// <summary>真正退出（走托盘右键菜单）。</summary>
        private void ExitApp()
        {
            _reallyExit = true;
            Close();
        }

        /// <summary>
        /// 收到第二个实例的广播后，把窗口显示出来（单实例注册在 App 里）。
        /// </summary>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var src = (System.Windows.Interop.HwndSource)PresentationSource.FromVisual(this);
                if (src != null)
                {
                    _showInstanceMsg = Win32.WM_SHOW_INSTANCE;
                    src.AddHook(WndProc);
                    _hwnd = src.Handle;
                    RegisterClickThroughHotkey(src.Handle);
                }
            }
            catch (Exception ex) { Log.Error("注册窗口消息钩子失败", ex); }
        }

        /// <summary>
        /// 注册全局热键 Ctrl+Alt+X 切换穿透。
        /// 这是最硬的一道逃生门：即使标题栏被拖动、窗口位置不合适，热键仍然能把穿透关掉。
        /// </summary>
        private void RegisterClickThroughHotkey(IntPtr hwnd)
        {
            try
            {
                _hotkeyRegistered = Win32.RegisterHotKey(hwnd, HotkeyIdToggleClickThrough,
                    Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT, VkX);
                if (!_hotkeyRegistered)
                    Log.Info("全局热键 Ctrl+Alt+X 注册失败（可能被其它程序占用），仅保留标题栏与托盘入口");
            }
            catch (Exception ex) { Log.Error("注册全局热键失败", ex); }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_hotkeyRegistered && _hwnd != IntPtr.Zero)
            {
                try { Win32.UnregisterHotKey(_hwnd, HotkeyIdToggleClickThrough); } catch { }
                _hotkeyRegistered = false;
            }
            base.OnClosed(e);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // 第二个实例启动 → 把自己叫出来
            if (_showInstanceMsg != 0 && msg == _showInstanceMsg)
            {
                ShowFromTray();
                handled = true;
                return IntPtr.Zero;
            }

            // 全局热键：Ctrl+Alt+X 切换穿透
            if (msg == Win32.WM_HOTKEY && wParam.ToInt32() == HotkeyIdToggleClickThrough)
            {
                ToggleClickThrough();
                handled = true;
                return IntPtr.Zero;
            }

            // 穿透：除了顶部标题条，其余区域都让给下层窗口
            if (msg == Win32.WM_NCHITTEST && _cfg.ClickThrough)
                return HitTestClickThrough(lParam, ref handled);

            return IntPtr.Zero;
        }

        /// <summary>
        /// 穿透状态下的命中测试。
        /// 返回 HTTRANSPARENT = 这次点击不归我（穿透到下层窗口）。
        /// 顶部标题条、以及按住 Alt 时返回正常，保证用户任何时候都能点回去。
        /// </summary>
        private IntPtr HitTestClickThrough(IntPtr lParam, ref bool handled)
        {
            try
            {
                // 按住 Alt 临时恢复正常交互，方便把整个窗口拖走
                if ((Win32.GetKeyState(Win32.VK_MENU) & 0x8000) != 0) return IntPtr.Zero;

                long lp = lParam.ToInt64();
                int sx = (short)(lp & 0xFFFF);
                int sy = (short)((lp >> 16) & 0xFFFF);

                var local = PointFromScreen(new Point(sx, sy));
                double zone = _viewMode == AppViewMode.Mini ? MiniHeaderZone : FullHeaderZone;

                if (local.Y > zone)
                {
                    handled = true;
                    return new IntPtr(Win32.HTTRANSPARENT);
                }
            }
            catch (Exception ex) { Log.Error("穿透命中测试失败", ex); }
            return IntPtr.Zero;
        }

        // ---------------- 鼠标穿透 ----------------

        private void ToggleClickThrough()
        {
            _cfg.ClickThrough = !_cfg.ClickThrough;
            ApplyClickThrough();
            SaveCfg();

            if (_cfg.ClickThrough)
            {
                // 第一次开启必须把怎么关掉说清楚，否则就是把人锁在门外
                if (!_clickThroughHinted)
                {
                    _clickThroughHinted = true;
                    _tray?.ShowBalloon("鼠标穿透已开启",
                        "内容区点击会穿到下层，顶部标题栏仍可操作。要关掉：点标题栏的穿透按钮、按 Ctrl+Alt+X、或右键托盘图标。");
                }
            }
            else if (!IsVisible)
            {
                // 用热键关掉穿透时窗口可能藏着，直接弹出来给个反馈
                ShowFromTray();
            }
        }

        /// <summary>把配置里的穿透状态应用到窗口，并同步按钮 / 托盘菜单文案。</summary>
        private void ApplyClickThrough()
        {
            if (!Win32.SetClickThrough(this, _cfg.ClickThrough) && _cfg.ClickThrough)
                Log.Info("鼠标穿透延后应用：窗口句柄尚未创建");

            UpdateClickThroughButton();
            _tray?.SetClickThrough(_cfg.ClickThrough);
        }

        /// <summary>穿透开关的选中态。v1.0.0 完全没同步，用户看不出开没开。</summary>
        private void UpdateClickThroughButton()
        {
            if (ClickThroughToggleBtn == null) return;
            ClickThroughToggleBtn.Style = (Style)FindResource(_cfg.ClickThrough ? "ToggleActive" : "ToggleIdle");
        }

        /// <summary>
        /// 把窗口尺寸、位置、缩放标记全部恢复默认，并顺手关掉穿透。
        /// 专门救"被拖得极小 / 拖到屏幕外，已经没法操作"的事故现场。
        /// </summary>
        private void ResetWindowLayout()
        {
            ShowFromTray();

            _userResizedFull = false;
            _userResizedMini = false;
            _cfg.FullResized = false;
            _cfg.MiniResized = false;
            _cfg.WindowWidth = null;
            _cfg.WindowHeight = null;
            _cfg.MiniWidth = null;
            _cfg.MiniHeight = null;
            _cfg.WindowLeft = null;
            _cfg.WindowTop = null;
            _cfg.MiniLeft = null;
            _cfg.MiniTop = null;

            if (_cfg.ClickThrough)
            {
                _cfg.ClickThrough = false;
                ApplyClickThrough();
            }

            ApplyMinSize();
            _viewMode = AppViewMode.Full;
            _cfg.ViewMode = ViewState.ToText(AppViewMode.Full);
            ApplyViewMode();   // 内部会 RestoreFullPosition + FitCanvasHeight

            SaveCfg();
            Render();
            Log.Info("已通过托盘重置窗口大小与位置");
        }
    }
}
