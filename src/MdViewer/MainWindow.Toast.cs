using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MdViewer
{
    /// <summary>
    /// 右下角的轻量通知。
    ///
    /// 「已导出 xxx」这种只是告诉你一声的事，不该用模态框拦着人点确定 ——
    /// 用户刚保存完文件，心思已经在别处了，再逼他点一下纯属添乱。
    /// 所以改成浮一小块：几秒后自己消失，鼠标移上去就停住，要打开文件也能直接点。
    /// </summary>
    public partial class MainWindow
    {
        private static readonly TimeSpan ToastLifetime = TimeSpan.FromSeconds(6.5);

        private DispatcherTimer _toastTimer;
        private string _toastPath;

        /// <summary>
        /// 弹一条通知。<paramref name="path"/> 给了就显示「打开 / 所在文件夹」，可以不给。
        /// </summary>
        public void ShowToast(string title, string detail = null, string path = null)
        {
            if (Toast == null) return;

            ToastTitle.Text = title ?? string.Empty;

            bool hasDetail = !string.IsNullOrWhiteSpace(detail);
            ToastDetail.Text = hasDetail ? detail : string.Empty;
            ToastDetail.Visibility = hasDetail ? Visibility.Visible : Visibility.Collapsed;

            _toastPath = path;
            bool hasPath = !string.IsNullOrWhiteSpace(path);
            ToastActions.Visibility = hasPath ? Visibility.Visible : Visibility.Collapsed;
            if (hasPath)
            {
                ToastOpen.Visibility = File.Exists(path) ? Visibility.Visible : Visibility.Collapsed;
                ToastReveal.Content = Directory.Exists(path) ? "打开文件夹" : "所在文件夹";
            }

            Toast.Visibility = Visibility.Visible;
            Toast.Opacity = 1;
            Toast.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
                {
                    FillBehavior = FillBehavior.Stop
                });

            RestartToastTimer(ToastLifetime);
        }

        private void RestartToastTimer(TimeSpan lifetime)
        {
            if (_toastTimer == null)
            {
                _toastTimer = new DispatcherTimer();
                _toastTimer.Tick += (sender, args) =>
                {
                    _toastTimer.Stop();
                    HideToast();
                };
            }

            _toastTimer.Stop();
            _toastTimer.Interval = lifetime;
            _toastTimer.Start();
        }

        private void HideToast()
        {
            if (Toast == null) return;
            if (_toastTimer != null) _toastTimer.Stop();
            Toast.Visibility = Visibility.Collapsed;
            _toastPath = null;
        }

        // 鼠标停在通知上就不让它消失 —— 多半是正想去点「打开」
        private void Toast_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_toastTimer != null) _toastTimer.Stop();
        }

        private void Toast_MouseLeave(object sender, MouseEventArgs e)
        {
            if (Toast.Visibility == Visibility.Visible) RestartToastTimer(TimeSpan.FromSeconds(3));
        }

        private void ToastClose_Click(object sender, RoutedEventArgs e)
        {
            HideToast();
        }

        private void ToastOpen_Click(object sender, RoutedEventArgs e)
        {
            string path = _toastPath;
            HideToast();
            if (!string.IsNullOrEmpty(path)) OpenExternal(path);
        }

        private void ToastReveal_Click(object sender, RoutedEventArgs e)
        {
            string path = _toastPath;
            HideToast();
            if (string.IsNullOrEmpty(path)) return;

            if (Directory.Exists(path)) OpenExternal(path);
            else RevealInExplorer(path);
        }
    }
}
