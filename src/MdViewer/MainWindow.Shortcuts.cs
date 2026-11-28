using System;
using System.Windows;
using System.Windows.Input;

namespace MdViewer
{
    /// <summary>
    /// 快捷键的唯一入口。
    ///
    /// 有两个来源：
    ///   1) WPF 自己的 PreviewKeyDown（焦点在工具栏 / 侧栏时）；
    ///   2) 页面通过 postMessage 转发过来的按键（焦点在正文时）。
    ///
    /// 为什么需要第 2 条：WebView2 是原生子窗口，键盘消息不会冒泡到 WPF 的
    /// Window.PreviewKeyDown。0.5.x 里只要在正文点一下，Ctrl+F / F5 / F11 就全失效。
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>执行一个快捷键动作；返回 false 表示这次不处理（例如 Esc 时没东西可关）。</summary>
        private bool HandleShortcut(string action)
        {
            switch (action)
            {
                case "open":
                    Open_Click(null, new RoutedEventArgs());
                    return true;
                case "recent":
                    Recent_Click(null, new RoutedEventArgs());
                    return true;
                case "find":
                    ShowFindBar();
                    return true;
                case "exportPdf":
                    ExportPdf_Click(null, new RoutedEventArgs());
                    return true;
                case "exportPng":
                    ExportPng_Click(null, new RoutedEventArgs());
                    return true;
                case "settings":
                    Settings_Click(null, new RoutedEventArgs());
                    return true;
                case "toc":
                    TocToggle.IsChecked = TocToggle.IsChecked != true;
                    UpdateChrome();
                    return true;
                case "theme":
                    Theme_Click(null, new RoutedEventArgs());
                    return true;
                case "wide":
                    SetWideMode(!App.Settings.WideMode);
                    return true;
                case "speak":
                    ToggleSpeak();
                    return true;
                case "bookmark":
                    AddBookmark();
                    return true;
                case "editor":
                    OpenInExternalEditor();
                    return true;
                case "fontUp":
                    ChangeFontSize(1);
                    return true;
                case "fontDown":
                    ChangeFontSize(-1);
                    return true;
                case "fontReset":
                    ChangeFontSize(16 - App.Settings.FontSize);
                    return true;
                case "zen":
                    ToggleZenMode();
                    return true;
                case "back":
                    Back_Click(null, new RoutedEventArgs());
                    return true;
                case "forward":
                    Forward_Click(null, new RoutedEventArgs());
                    return true;
                case "reload":
                    if (string.IsNullOrEmpty(_currentPath)) return false;
                    _ = ReloadCurrentAsync(false);
                    return true;
                case "escape":
                    if (FindBar.Visibility == Visibility.Visible)
                    {
                        HideFindBar();
                        return true;
                    }
                    if (IsZenMode)
                    {
                        ToggleZenMode();
                        return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        /// <summary>把 WPF 键盘事件翻译成动作名；不认识的返回 null。</summary>
        private static string ShortcutActionFor(KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;

            // 先判 Ctrl+Shift：Ctrl+Shift+E 必须优先于 Ctrl+E，否则永远打不开外部编辑器
            if (ctrl && shift)
            {
                switch (e.Key)
                {
                    case Key.E: return "editor";
                    case Key.D: return "theme";
                    case Key.W: return "wide";
                    case Key.R: return "speak";
                    case Key.B: return "bookmark";
                    case Key.S: return "exportPng";
                }
                return null;
            }

            if (ctrl)
            {
                switch (e.Key)
                {
                    case Key.O: return "open";
                    case Key.F: return "find";
                    case Key.P: return "exportPdf";
                    case Key.E: return "recent";
                    case Key.OemComma: return "settings";
                    case Key.T: return "toc";
                    case Key.OemPlus:
                    case Key.Add: return "fontUp";
                    case Key.OemMinus:
                    case Key.Subtract: return "fontDown";
                    case Key.D0:
                    case Key.NumPad0: return "fontReset";
                }
                return null;
            }

            if (e.Key == Key.F11) return "zen";
            if (e.Key == Key.F5) return "reload";
            if (e.Key == Key.Escape) return "escape";
            if (alt && e.Key == Key.Left) return "back";
            if (alt && e.Key == Key.Right) return "forward";
            return null;
        }

        /// <summary>页面转发过来的快捷键。</summary>
        private void HandlePageShortcut(string action)
        {
            if (string.IsNullOrEmpty(action)) return;
            if (!HandleShortcut(action))
            {
                // Esc 在页面上还有自己的用途（关灯泡/气泡），这里不处理就什么都不做
            }
        }
    }
}
