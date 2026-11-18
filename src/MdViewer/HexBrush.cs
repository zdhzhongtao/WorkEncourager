using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MdViewer
{
    /// <summary>
    /// 把主题里的 "#RRGGBB" 字符串变成画刷。
    /// 只给设置窗口的「主题缩略图」用 —— Core 里不引 WPF，颜色一律存字符串。
    /// </summary>
    public sealed class HexBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = value as string;
            if (string.IsNullOrWhiteSpace(text)) return Brushes.Transparent;

            try
            {
                var color = (Color)ColorConverter.ConvertFromString(text);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch (Exception)
            {
                return Brushes.Transparent;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
