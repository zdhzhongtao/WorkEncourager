using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MdViewer.Core;

namespace MdViewer
{
    /// <summary>
    /// 写批注的小窗口。用代码搭而不是 XAML —— 就四个控件，为它单开一个 xaml 文件不值。
    ///
    /// 配色全部走 {DynamicResource ThemeXxx}，深浅主题和自定义皮肤下都不会出现白底黑字。
    /// </summary>
    public sealed class NoteDialog : Window
    {
        private readonly TextBox _box;

        public string NoteText
        {
            get { return (_box.Text ?? string.Empty).Trim(); }
        }

        public NoteDialog(string quote)
        {
            Title = "添加批注";
            Width = 520;
            Height = 320;
            MinWidth = 380;
            MinHeight = 240;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Background = Res("ThemeWindowBg", Brushes.White);

            var root = new DockPanel { LastChildFill = true, Margin = new Thickness(16) };

            // 底部按钮
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            DockPanel.SetDock(buttons, Dock.Bottom);

            var cancel = new Button
            {
                Content = "取消",
                Padding = new Thickness(16, 5, 16, 5),
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true,
                Style = Res("GhostButton")
            };
            var ok = new Button
            {
                Content = "保存批注",
                Padding = new Thickness(16, 5, 16, 5),
                IsDefault = true,
                Style = Res("PrimaryButton")
            };
            ok.Click += (s, e) =>
            {
                if (NoteText.Length == 0) return;
                DialogResult = true;
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            root.Children.Add(buttons);

            // 顶部：被批注的原文
            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(head, Dock.Top);
            head.Children.Add(new TextBlock
            {
                Text = "批注这段文字",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Res("ThemeTextPrimary", Brushes.Black)
            });
            head.Children.Add(new Border
            {
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(10, 7, 10, 7),
                CornerRadius = new CornerRadius(6),
                Background = Res("ThemeHoverBg", Brushes.WhiteSmoke),
                Child = new TextBlock
                {
                    Text = Shorten(quote, 160),
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxHeight = 54,
                    FontSize = 12,
                    Foreground = Res("ThemeTextSecondary", Brushes.Gray)
                }
            });
            root.Children.Add(head);

            _box = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 13,
                Background = Res("ThemeInputBg", Brushes.White),
                Foreground = Res("ThemeInputFg", Brushes.Black),
                BorderBrush = Res("ThemeInputBorder", Brushes.Gray)
            };
            root.Children.Add(_box);

            Content = root;
            Loaded += (s, e) => _box.Focus();
        }

        private static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return flat.Length <= max ? flat : flat.Substring(0, max) + "…";
        }

        private static Brush Res(string key, Brush fallback)
        {
            object value = null;
            try { value = Application.Current?.TryFindResource(key); }
            catch (System.Exception) { }
            return value as Brush ?? fallback;
        }

        private static Style Res(string key)
        {
            try { return Application.Current?.TryFindResource(key) as Style; }
            catch (System.Exception) { return null; }
        }
    }
}
