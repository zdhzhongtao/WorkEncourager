using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MdViewer
{
    /// <summary>
    /// 把任意窗口当前的样子存成 PNG，给 <c>--snapshot</c> 自检用。
    ///
    /// 单独抽出来是因为主窗口和设置窗口都要拍：设置窗口是模态打开的，
    /// 主窗口的 RenderTargetBitmap 拍不到它，只能各拍各的。
    /// 注意 WebView2 是原生子窗口，不走 WPF 渲染管线，正文区域会截成空白 ——
    /// 正文另用 MainWindow.SnapshotPageAsync 截。
    /// </summary>
    internal static class WindowSnapshot
    {
        public static void Capture(Window window, string path)
        {
            if (window == null) throw new ArgumentNullException(nameof(window));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("截图路径不能为空", nameof(path));

            window.UpdateLayout();

            DpiScale dpi = VisualTreeHelper.GetDpi(window);
            int width = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX);
            int height = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);
            if (width <= 0 || height <= 0) throw new InvalidOperationException("窗口还没有尺寸，截图失败。");

            // 先铺一层背景，否则窗口没画到的地方会是全透明
            var backdrop = new DrawingVisual();
            using (DrawingContext dc = backdrop.RenderOpen())
            {
                dc.DrawRectangle(window.Background ?? Brushes.White, null,
                    new Rect(0, 0, window.ActualWidth, window.ActualHeight));
            }

            var composite = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY,
                PixelFormats.Pbgra32);
            composite.Render(backdrop);
            composite.Render(window);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(composite));

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            using (var stream = File.Create(path))
                encoder.Save(stream);
        }
    }
}
