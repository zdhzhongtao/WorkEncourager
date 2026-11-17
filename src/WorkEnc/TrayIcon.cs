using System;
using System.Drawing;
using System.Windows.Forms;

namespace WorkEncourager
{
    /// <summary>
    /// 系统托盘图标（通知区域）。
    ///
    /// 设计意图：主窗口不再占用任务栏（ShowInTaskbar=False），
    /// 关掉窗口只是隐藏到托盘，计时继续跑；真正退出走托盘右键菜单。
    /// </summary>
    public sealed class TrayIcon : IDisposable
    {
        private readonly NotifyIcon _ni;
        private readonly ToolStripMenuItem _toggleItem;
        private readonly ToolStripMenuItem _modeItem;
        private readonly ToolStripMenuItem _clickThroughItem;
        private readonly ToolStripMenuItem _pauseItem;
        private DateTime _lastClickUtc = DateTime.MinValue;
        private string _lastTip = "";
        private bool _disposed;

        /// <summary>左键单击：显示 / 隐藏主窗口。</summary>
        public event Action ToggleRequested;

        /// <summary>切换 迷你挂件 / 完整界面。</summary>
        public event Action ModeSwitchRequested;

        /// <summary>开关鼠标穿透（穿透后点不到窗口时的救命入口之一）。</summary>
        public event Action ClickThroughToggleRequested;

        /// <summary>暂停 / 恢复计时。</summary>
        public event Action PauseToggleRequested;

        /// <summary>把窗口尺寸和位置恢复默认（窗口被拖到不可用时的兜底）。</summary>
        public event Action ResetWindowRequested;

        /// <summary>直接打开设置页。</summary>
        public event Action SettingsRequested;

        /// <summary>退出程序。</summary>
        public event Action ExitRequested;

        public TrayIcon()
        {
            _toggleItem = new ToolStripMenuItem("隐藏到托盘", null, (s, e) => ToggleRequested?.Invoke());
            _modeItem = new ToolStripMenuItem("切换为迷你挂件", null, (s, e) => ModeSwitchRequested?.Invoke());
            _clickThroughItem = new ToolStripMenuItem("开启鼠标穿透", null, (s, e) => ClickThroughToggleRequested?.Invoke());
            _pauseItem = new ToolStripMenuItem("暂停计时", null, (s, e) => PauseToggleRequested?.Invoke());

            var menu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                Font = new Font("Microsoft YaHei UI", 9f)
            };
            menu.Items.Add(_toggleItem);
            menu.Items.Add(_modeItem);
            menu.Items.Add(_pauseItem);
            menu.Items.Add(_clickThroughItem);
            menu.Items.Add(new ToolStripMenuItem("重置窗口大小与位置", null, (s, e) => ResetWindowRequested?.Invoke()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("打开设置", null, (s, e) => SettingsRequested?.Invoke()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("退出", null, (s, e) => ExitRequested?.Invoke()));

            _ni = new NotifyIcon
            {
                Icon = LoadAppIcon(),
                Text = "上班鼓励器",
                Visible = true,
                ContextMenuStrip = menu
            };

            // 双击会连发两次 MouseClick，这里用时间窗把第二下吃掉，避免"显示又隐藏"
            _ni.MouseClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                var now = DateTime.UtcNow;
                if ((now - _lastClickUtc).TotalMilliseconds < 350) return;
                _lastClickUtc = now;
                ToggleRequested?.Invoke();
            };
        }

        /// <summary>更新悬停提示（调用方自己做节流，不必每帧调）。</summary>
        public void UpdateTip(string text)
        {
            if (_disposed || text == null) return;
            if (text.Length > 120) text = text.Substring(0, 120);
            if (text == _lastTip) return;
            _lastTip = text;
            try { _ni.Text = text; } catch { /* Text 过长等异常忽略 */ }
        }

        /// <summary>按窗口当前可见性更新菜单文案。</summary>
        public void SetWindowVisible(bool visible)
        {
            if (_disposed) return;
            _toggleItem.Text = visible ? "隐藏到托盘" : "显示界面";
        }

        /// <summary>按当前视图模式更新菜单文案。</summary>
        public void SetMode(bool mini)
        {
            if (_disposed) return;
            _modeItem.Text = mini ? "切换为完整界面" : "切换为迷你挂件";
        }

        /// <summary>按当前暂停状态更新菜单文案。</summary>
        public void SetPaused(bool paused)
        {
            if (_disposed) return;
            _pauseItem.Text = paused ? "恢复计时" : "暂停计时";
        }

        /// <summary>按当前穿透状态更新菜单文案。</summary>
        public void SetClickThrough(bool enabled)
        {
            if (_disposed) return;
            _clickThroughItem.Text = enabled ? "关闭鼠标穿透" : "开启鼠标穿透";
        }

        /// <summary>首次隐藏到托盘时给个气泡提示，告诉用户程序还在跑。</summary>
        public void ShowBalloon(string title, string body)
        {
            if (_disposed) return;
            try
            {
                _ni.ShowBalloonTip(2500, title, body, ToolTipIcon.Info);
            }
            catch { }
        }

        /// <summary>取 exe 自带的图标；取不到就退回系统默认图标。</summary>
        private static Icon LoadAppIcon()
        {
            try
            {
                string path = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    var ic = Icon.ExtractAssociatedIcon(path);
                    if (ic != null) return ic;
                }
            }
            catch { }
            return SystemIcons.Application;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _ni.Visible = false;
                if (_ni.ContextMenuStrip != null) _ni.ContextMenuStrip.Dispose();
                _ni.Dispose();
            }
            catch { }
        }
    }
}
