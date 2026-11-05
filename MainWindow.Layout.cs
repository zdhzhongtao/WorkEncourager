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
        private void RememberBounds()
        {
            // 注意：不能写 _cfg.Opacity = Opacity。
            // 幽灵模式淡化时 Opacity 是临时值，存进去下次启动就变成"默认很淡"。
            // 滑块改动时已经实时同步过 _cfg.Opacity，这里保持原值即可。
            _cfg.Topmost = Topmost;
            _cfg.ViewMode = ViewState.ToText(_viewMode);
            _cfg.ActiveTab = ViewState.ToText(_tab);

            var screen = ScreenOfWindow();
            if (_viewMode == AppViewMode.Mini)
            {
                _cfg.MiniLeft = Left;
                _cfg.MiniTop = Top;
                _cfg.MiniWidth = ActualWidth;
                _cfg.MiniHeight = ActualHeight;
                if (screen != null) _cfg.MiniScreen = screen.DeviceName;
            }
            else
            {
                _cfg.WindowLeft = Left;
                _cfg.WindowTop = Top;
                _cfg.WindowWidth = ActualWidth;
                _cfg.WindowHeight = ActualHeight;
                if (screen != null) _cfg.WindowScreen = screen.DeviceName;
            }
        }

        /// <summary>当前窗口所在的显示器；句柄还没建时返回 null。</summary>
        private System.Windows.Forms.Screen ScreenOfWindow()
        {
            try
            {
                var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (h == IntPtr.Zero) return null;
                return System.Windows.Forms.Screen.FromHandle(h);
            }
            catch { return null; }
        }

        /// <summary>
        /// 取指定显示器的工作区（DIP）。找不到就回主屏。
        /// 副屏的物理像素按主屏缩放比换算成 DIP —— 多屏同缩放比时完全准确，
        /// 不同缩放比时略有偏差，但外面还有 IsOnAnyScreen 兜底，窗口不会丢。
        /// </summary>
        private static Rect WorkAreaOf(string deviceName)
        {
            Rect fallback = SystemParameters.WorkArea;
            if (string.IsNullOrEmpty(deviceName)) return fallback;

            try
            {
                var screens = System.Windows.Forms.Screen.AllScreens;
                var target = screens.FirstOrDefault(x =>
                    string.Equals(x.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
                if (target == null || target.Primary) return fallback;

                var primary = screens.FirstOrDefault(x => x.Primary);
                if (primary == null) return fallback;

                double kx = SystemParameters.WorkArea.Width / Math.Max(1.0, primary.WorkingArea.Width);
                double ky = SystemParameters.WorkArea.Height / Math.Max(1.0, primary.WorkingArea.Height);

                var a = target.WorkingArea;
                return new Rect(a.Left * kx, a.Top * ky, a.Width * kx, a.Height * ky);
            }
            catch { return fallback; }
        }

        // ---------------- 窗口边缘缩放 ----------------

        private enum ResizeDir { None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

        private static readonly Dictionary<string, ResizeDir> _gripDirs =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["GripLeft"] = ResizeDir.Left,
                ["GripRight"] = ResizeDir.Right,
                ["GripTop"] = ResizeDir.Top,
                ["GripBottom"] = ResizeDir.Bottom,
                ["GripTopLeft"] = ResizeDir.TopLeft,
                ["GripTopRight"] = ResizeDir.TopRight,
                ["GripBottomLeft"] = ResizeDir.BottomLeft,
                ["GripBottomRight"] = ResizeDir.BottomRight,
            };

        private ResizeDir _resizeDir = ResizeDir.None;
        private Point _resizeAnchor;                 // 按下时的屏幕坐标（物理像素）
        private double _resizeStartW, _resizeStartH, _resizeStartLeft, _resizeStartTop;

        private void OnGripMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (!_gripDirs.TryGetValue(fe.Name, out var dir)) return;

            _resizeDir = dir;
            _resizeAnchor = PointToScreen(e.GetPosition(this));
            _resizeStartW = Width;
            _resizeStartH = Height;
            _resizeStartLeft = Left;
            _resizeStartTop = Top;
            fe.CaptureMouse();
            e.Handled = true;
        }

        private void OnGripMouseMove(object sender, MouseEventArgs e)
        {
            if (_resizeDir == ResizeDir.None) return;
            if (e.LeftButton != MouseButtonState.Pressed) { EndResize(sender); return; }

            // PointToScreen 返回物理像素，窗口尺寸用的是 DIP，需要除以 DPI 缩放
            var dpi = VisualTreeHelper.GetDpi(this);
            var cur = PointToScreen(e.GetPosition(this));
            double dx = (cur.X - _resizeAnchor.X) / dpi.DpiScaleX;
            double dy = (cur.Y - _resizeAnchor.Y) / dpi.DpiScaleY;

            double w = _resizeStartW, h = _resizeStartH, l = _resizeStartLeft, t = _resizeStartTop;

            bool moveLeft = _resizeDir is ResizeDir.Left or ResizeDir.TopLeft or ResizeDir.BottomLeft;
            bool moveRight = _resizeDir is ResizeDir.Right or ResizeDir.TopRight or ResizeDir.BottomRight;
            bool moveTop = _resizeDir is ResizeDir.Top or ResizeDir.TopLeft or ResizeDir.TopRight;
            bool moveBottom = _resizeDir is ResizeDir.Bottom or ResizeDir.BottomLeft or ResizeDir.BottomRight;

            if (moveLeft) { w = _resizeStartW - dx; l = _resizeStartLeft + dx; }
            else if (moveRight) { w = _resizeStartW + dx; }
            if (moveTop) { h = _resizeStartH - dy; t = _resizeStartTop + dy; }
            else if (moveBottom) { h = _resizeStartH + dy; }

            // 夹到最小/最大尺寸，同时把对边拉回来，保证锚点那一侧不移动
            if (w < MinWidth) { if (moveLeft) l -= (MinWidth - w); w = MinWidth; }
            if (h < MinHeight) { if (moveTop) t -= (MinHeight - h); h = MinHeight; }
            if (!double.IsInfinity(MaxWidth) && MaxWidth > 0 && w > MaxWidth)
            { if (moveLeft) l += (w - MaxWidth); w = MaxWidth; }
            if (!double.IsInfinity(MaxHeight) && MaxHeight > 0 && h > MaxHeight)
            { if (moveTop) t += (h - MaxHeight); h = MaxHeight; }

            Width = w; Height = h; Left = l; Top = t;
            e.Handled = true;
        }

        private void OnGripMouseUp(object sender, MouseButtonEventArgs e)
        {
            EndResize(sender);
            e.Handled = true;
        }

        private void OnGripMouseLeave(object sender, MouseEventArgs e)
        {
            // 鼠标带着拖出窗口时松开，也要结束缩放
            if (_resizeDir != ResizeDir.None && e.LeftButton != MouseButtonState.Pressed)
                EndResize(sender);
        }

        private void EndResize(object sender)
        {
            if (_resizeDir == ResizeDir.None) return;
            _resizeDir = ResizeDir.None;
            if (sender is FrameworkElement fe) fe.ReleaseMouseCapture();
            SaveWindowBounds();
        }

        /// <summary>保存当前窗口位置与尺寸到配置（完整模式与迷你模式分开记）。</summary>
        private void SaveWindowBounds()
        {
            if (_viewMode == AppViewMode.Mini)
            {
                _userResizedMini = true;
                _cfg.MiniResized = true;
                _cfg.MiniLeft = Left;
                _cfg.MiniTop = Top;
                _cfg.MiniWidth = ActualWidth;
                _cfg.MiniHeight = ActualHeight;
            }
            else
            {
                _userResizedFull = true;
                _cfg.FullResized = true;
                _cfg.WindowLeft = Left;
                _cfg.WindowTop = Top;
                _cfg.WindowWidth = ActualWidth;
                _cfg.WindowHeight = ActualHeight;
            }
            SaveCfg();
        }

        // ---------------- 幽灵模式 / 边缘吸附 ----------------

        /// <summary>幽灵模式：鼠标离开窗口时淡下去，移回来恢复。</summary>
        private void ToggleGhostMode()
        {
            _cfg.GhostMode = !_cfg.GhostMode;
            SaveCfg();
            UpdateFeatureButtons();

            if (_cfg.GhostMode) SetGhostFaded(!IsMouseOver);   // 立刻按当前位置生效
            else Opacity = NormalOpacity();
        }

        /// <summary>把窗口切到"淡出"或"正常"。只在幽灵模式开启、且没开穿透时生效。</summary>
        private void SetGhostFaded(bool faded)
        {
            if (!_cfg.GhostMode) return;
            if (_cfg.ClickThrough) return;   // 穿透后收不到鼠标事件，淡出会一直卡在淡的状态

            double normal = NormalOpacity();
            double target = faded ? Math.Min(normal, Math.Max(0.12, _cfg.GhostOpacity)) : normal;
            if (Math.Abs(Opacity - target) > 0.001) Opacity = target;
        }

        /// <summary>用户设定的正常不透明度（夹到 0.3~1，与滑块下限一致）。</summary>
        private double NormalOpacity()
        {
            double op = _cfg.Opacity ?? 0.97;
            if (op < 0.3) op = 0.3;
            if (op > 1) op = 1;
            return op;
        }

        /// <summary>边缘吸附开关：只对迷你挂件生效。</summary>
        private void ToggleSnapEdge()
        {
            _cfg.SnapToEdge = !_cfg.SnapToEdge;
            SaveCfg();
            UpdateFeatureButtons();
            if (_cfg.SnapToEdge) SnapMiniToEdge();
        }

        /// <summary>
        /// 迷你挂件松手后，离屏幕边缘够近就贴上去。
        /// 用主屏工作区判断，副屏窗口直接跳过（避免坐标系统混淆）。
        /// </summary>
        private void SnapMiniToEdge()
        {
            if (!_cfg.SnapToEdge || _viewMode != AppViewMode.Mini) return;

            var wa = SystemParameters.WorkArea;
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            // 只处理完全落在主屏工作区里的窗口
            if (Left < wa.Left - 1 || Top < wa.Top - 1 ||
                Left + w > wa.Right + 1 || Top + h > wa.Bottom + 1) return;

            const double threshold = 28;   // 离边缘多近算"想贴边"
            const double gap = 6;          // 贴上后留的缝

            double left = Left, top = Top;

            if (left - wa.Left < threshold) left = wa.Left + gap;
            else if (wa.Right - (left + w) < threshold) left = wa.Right - w - gap;

            if (top - wa.Top < threshold) top = wa.Top + gap;
            else if (wa.Bottom - (top + h) < threshold) top = wa.Bottom - h - gap;

            if (Math.Abs(left - Left) > 0.5 || Math.Abs(top - Top) > 0.5)
            {
                Left = left;
                Top = top;
            }
        }

        /// <summary>同步设置页两个开关的选中态。</summary>
        private void UpdateFeatureButtons()
        {
            if (GhostModeBtn != null)
                GhostModeBtn.Style = (Style)FindResource(_cfg.GhostMode ? "ToggleActive" : "ToggleIdle");
            if (SnapEdgeBtn != null)
                SnapEdgeBtn.Style = (Style)FindResource(_cfg.SnapToEdge ? "ToggleActive" : "ToggleIdle");
        }

        // ---------------- 置顶 / 透明度 / 位置 ----------------

        private void ApplyTopmost()
        {
            Topmost = _cfg.Topmost;
            PinBtn.Foreground = Topmost ? MakeBrush("#F97316") : MakeBrush("#C3CAD6");
        }

        private void OnPinClick(object sender, RoutedEventArgs e)
        {
            _cfg.Topmost = !_cfg.Topmost;
            ApplyTopmost();
            SaveCfg();
        }

        private void ApplyOpacity()
        {
            double op = _cfg.Opacity ?? 0.97;
            if (op < 0.3) op = 0.3;
            if (op > 1) op = 1;
            Opacity = op;
            if (OpacitySlider != null)
            {
                OpacitySlider.Value = op;
                OpacityValueText.Text = Math.Round(op * 100).ToString() + "%";
            }
        }

        /// <summary>
        /// 只设定完整界面的宽度，高度交给内容决定。
        ///
        /// 以前是宽高一起定死，配上 Viewbox 的等比缩放，拖小了文字就糊成一团。
        /// 现在宽度自由、高度贴合内容，所以"尺寸预设"只改宽度。
        /// </summary>
        private void SetWidth(double w)
        {
            w = Math.Max(ViewState.FullMinWidth, w);

            _cfg.WindowWidth = w;
            _cfg.WindowHeight = null;
            _cfg.FullResized = false;
            _userResizedFull = false;      // 高度重新交给 FitCanvasHeight

            if (_viewMode == AppViewMode.Full)
            {
                Width = w;
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitCanvasHeight));
            }

            UpdateSizeText();
            SaveCfg();
        }

        /// <summary>画布宽度变了（用户拖宽窗口）→ 内容重排后高度会变，重算一次。</summary>
        private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_viewMode != AppViewMode.Full) return;
            if (Math.Abs(e.NewSize.Width - _lastCanvasWidth) < 0.5) return;   // 只关心宽度变化

            _lastCanvasWidth = e.NewSize.Width;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitCanvasHeight));
        }

        /// <summary>从配置恢复视图模式与 Tab（启动时调用）。</summary>
        private void RestoreViewState()
        {
            _viewMode = ViewState.ParseViewMode(_cfg.ViewMode);
            _tab = ViewState.ParseTab(_cfg.ActiveTab);

            HomePanel.Visibility = _tab == MainTab.Home ? Visibility.Visible : Visibility.Collapsed;
            DetailPanel.Visibility = _tab == MainTab.Detail ? Visibility.Visible : Visibility.Collapsed;
            StatsPanel.Visibility = _tab == MainTab.Stats ? Visibility.Visible : Visibility.Collapsed;
            SettingsScroll.Visibility = _tab == MainTab.Settings ? Visibility.Visible : Visibility.Collapsed;

            ApplyMinSize();

            if (_viewMode == AppViewMode.Mini)
            {
                var sz = ViewState.WindowSize(AppViewMode.Mini, _cfg.MiniWidth, _cfg.MiniHeight,
                                              MainTab.Home, _userResizedMini);
                Width = sz.W;
                Height = sz.H;
            }
            else
            {
                var sz = ViewState.WindowSize(AppViewMode.Full, _cfg.WindowWidth, _cfg.WindowHeight,
                                              _tab, _userResizedFull);
                Width = sz.W;
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitCanvasHeight));
            }
        }

        private void UpdateSizeText()
        {
            if (SizeText == null) return;
            if (_viewMode == AppViewMode.Mini)
            {
                SizeText.Text = "迷你挂件 · 双击展开完整界面";
                return;
            }
            SizeText.Text = Math.Round(ActualWidth) + " × " + Math.Round(ActualHeight) +
                            "（拖动窗体边框可缩放）";
        }

        private void OnDragArea(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            try { DragMove(); } catch { }
            SnapMiniToEdge();   // 迷你挂件松手后贴边（开关打开时才生效）

            if (_viewMode == AppViewMode.Mini)
            {
                _cfg.MiniLeft = Left;
                _cfg.MiniTop = Top;
            }
            else
            {
                _cfg.WindowLeft = Left;
                _cfg.WindowTop = Top;
            }
            SaveCfg();
        }

        /// <summary>迷你挂件上双击展开完整界面（单击仍用于拖动窗口）。</summary>
        private void OnMiniShellMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2 && e.ChangedButton == MouseButton.Left)
            {
                e.Handled = true;
                SetViewMode(AppViewMode.Full);
            }
        }
    }
}
