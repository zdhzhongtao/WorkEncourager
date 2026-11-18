using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MdViewer.Core;

namespace MdViewer
{
    /// <summary>
    /// 皮肤、字体、无干扰模式、第三方编辑、朗读、长图导出。
    ///
    /// 这些功能的共同点：都只影响「怎么看」，不改文档、不动渲染管线，
    /// 所以集中在一个文件里，出问题也容易回滚。
    /// </summary>
    public partial class MainWindow
    {
        // ───────────────────────── 自定义皮肤 ─────────────────────────

        private string _skinCacheKey;
        private string _skinCacheCss;

        /// <summary>内置皮肤：直接返回 null，表示「用内置主题」。</summary>
        private string CurrentSkinCss()
        {
            string file = App.Settings.SkinFile;
            if (string.IsNullOrWhiteSpace(file)) return null;

            string path = Path.Combine(App.ThemesDir, file);
            if (!File.Exists(path)) return null;

            try
            {
                var info = new FileInfo(path);
                string key = info.FullName + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length;
                if (string.Equals(key, _skinCacheKey, StringComparison.Ordinal) && _skinCacheCss != null)
                    return _skinCacheCss;

                string css = File.ReadAllText(path);
                _skinCacheKey = key;
                _skinCacheCss = css;
                return css;
            }
            catch (Exception ex)
            {
                Log.Error("读取自定义皮肤失败：" + path, ex);
                return null;
            }
        }

        /// <summary>可用皮肤列表：内置（null 表示）在前，然后是 themes 目录里的 css。</summary>
        public static List<SkinEntry> ListSkins()
        {
            var list = new List<SkinEntry>();

            foreach (MarkdownTheme theme in MarkdownThemes.All)
            {
                list.Add(new SkinEntry
                {
                    File = string.Empty,
                    Name = theme.Name,
                    IsBuiltIn = true,
                    IsDark = theme.IsDark,
                    ThemeId = theme.Id
                });
            }

            try
            {
                if (Directory.Exists(App.ThemesDir))
                {
                    foreach (string path in Directory.GetFiles(App.ThemesDir, "*.css"))
                    {
                        list.Add(new SkinEntry
                        {
                            File = Path.GetFileName(path),
                            Name = Path.GetFileNameWithoutExtension(path),
                            IsBuiltIn = false
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("枚举自定义皮肤失败", ex);
            }
            return list;
        }

        /// <summary>换皮肤：写设置 → 刷界面配色（自定义皮肤不改界面，只改正文）→ 重渲染。</summary>
        private void ApplySkin(string fileName)
        {
            App.Settings.SkinFile = fileName ?? string.Empty;
            _skinCacheKey = null;
            _skinCacheCss = null;
            App.SaveSettings();

            UpdateChrome();
            if (!string.IsNullOrEmpty(_currentPath)) Render();

            StatusRight.Text = string.IsNullOrWhiteSpace(fileName)
                ? "已切回内置主题：" + App.EffectiveMarkdownTheme.Name
                : "已应用自定义皮肤：" + Path.GetFileNameWithoutExtension(fileName);
        }

        /// <summary>把设置里的字体推给页面（不用重渲染，滚动位置不动）。</summary>
        private void PostFontsToPage()
        {
            if (!_webReady) return;
            try
            {
                var payload = new FontPayload
                {
                    body = Stack(App.Settings.FontBody,
                        "'Microsoft YaHei UI', 'Microsoft YaHei', -apple-system, 'Segoe UI', sans-serif"),
                    code = Stack(App.Settings.FontCode,
                        "Consolas, 'Cascadia Mono', 'Cascadia Code', monospace"),
                    heading = Stack(App.Settings.FontHeading, "inherit")
                };
                string json = JsonSerializer.Serialize(new FontMessage { kind = "fonts", payload = payload });
                View.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                Log.Error("推送字体失败", ex);
            }
        }

        private static string Stack(string name, string fallback)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return "'" + name.Trim() + "', " + fallback;
        }

        private sealed class FontMessage
        {
            public string kind { get; set; }
            public FontPayload payload { get; set; }
        }

        private sealed class FontPayload
        {
            public string body { get; set; }
            public string code { get; set; }
            public string heading { get; set; }
        }

        // ───────────────────────── 无干扰模式 ─────────────────────────

        private WindowState _zenPrevState;
        private bool _zen;

        /// <summary>
        /// F11：藏掉工具栏、搜索条、状态栏，只留正文。长文阅读时工具栏那一条其实一直在分心。
        /// 再按一次 F11 或 Esc 恢复；全屏状态下退出时还原窗口状态。
        /// </summary>
        private void ToggleZenMode()
        {
            _zen = !_zen;

            if (_zen)
            {
                _zenPrevState = WindowState;
                Toolbar.Visibility = Visibility.Collapsed;
                StatusBar.Visibility = Visibility.Collapsed;
                FindBar.Visibility = Visibility.Collapsed;
                TocPanel.Visibility = Visibility.Collapsed;
                TocColumn.Width = new GridLength(0);
                TocColumn.MinWidth = 0;
                TocColumn.MaxWidth = 0;
                if (WindowState != WindowState.Maximized) WindowState = WindowState.Maximized;
            }
            else
            {
                Toolbar.Visibility = Visibility.Visible;
                StatusBar.Visibility = Visibility.Visible;
                if (_zenPrevState != WindowState.Maximized) WindowState = _zenPrevState;
                UpdateChrome();          // 目录的可见性交回给 UpdateChrome 判断
                ApplyTocVisibility();
            }

            PostToPage("zen", _zen ? "1" : "0");

            // 无干扰模式本来就是为了铺满屏幕看，宽屏一起打开
            if (_zen && !App.Settings.WideMode) SetWideMode(true);
        }

        public bool IsZenMode
        {
            get { return _zen; }
        }

        // ───────────────────────── 用其他程序编辑 ─────────────────────────

        /// <summary>
        /// 阅读器不该假装能编辑，但「我想改一下」的需求真实存在。
        /// 优先用设置里指定的程序，其次自动找 VS Code / Typora，最后退回记事本。
        /// </summary>
        private void OpenInExternalEditor()
        {
            if (string.IsNullOrEmpty(_currentPath)) return;

            string editor = App.Settings.ExternalEditor;
            if (string.IsNullOrWhiteSpace(editor)) editor = FindEditor();

            if (string.IsNullOrWhiteSpace(editor))
            {
                MessageBox.Show("没找到可用的编辑器。可以在「设置 → 阅读」里指定一个程序，\n" +
                                "或者在资源管理器里右键这个文件手动打开。", "用其他程序编辑");
                return;
            }

            try
            {
                var info = new ProcessStartInfo(editor) { UseShellExecute = true };
                info.ArgumentList.Add(_currentPath);
                Process.Start(info);
                StatusRight.Text = "已用 " + Path.GetFileNameWithoutExtension(editor) + " 打开";
            }
            catch (Exception ex)
            {
                Log.Error("用外部程序打开失败：" + editor, ex);
                MessageBox.Show("打不开编辑器：" + editor + "\n\n" + ex.Message, "出错了");
            }
        }

        /// <summary>按常见安装位置找编辑器，找不到就用记事本兜底。</summary>
        public static string FindEditor()
        {
            var candidates = new List<string>();

            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            candidates.Add(Path.Combine(localApp, @"Programs\Microsoft VS Code\Code.exe"));
            candidates.Add(Path.Combine(programFiles, @"Microsoft VS Code\Code.exe"));
            candidates.Add(Path.Combine(programFilesX86, @"Microsoft VS Code\Code.exe"));
            candidates.Add(Path.Combine(localApp, @"Programs\Typora\Typora.exe"));
            candidates.Add(Path.Combine(programFiles, @"Typora\Typora.exe"));
            candidates.Add(Path.Combine(localApp, @"Programs\Notepad++\notepad++.exe"));
            candidates.Add(Path.Combine(programFiles, @"Notepad++\notepad++.exe"));
            candidates.Add(Path.Combine(programFiles, @"Sublime Text\sublime_text.exe"));

            foreach (string path in candidates)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;
                }
                catch (Exception)
                {
                    // 路径里有非法字符之类，跳过
                }
            }

            // 记事本一定在
            return "notepad.exe";
        }

        // ───────────────────────── 朗读 ─────────────────────────

        private string _speakState = "stopped";

        /// <summary>换文档、退出这种「顺带停一下」的场合，不往状态栏写「朗读结束」。</summary>
        private bool _stopSilent;

        /// <summary>播放 / 暂停 / 停止。状态由页面回报，按钮文案跟着走。</summary>
        private void ToggleSpeak()
        {
            if (!_webReady)
            {
                StatusRight.Text = "页面还没准备好，暂时不能朗读";
                return;
            }

            string action;
            if (_speakState == "playing") action = "pause";
            else if (_speakState == "paused") action = "resume";
            else action = "play";

            _stopSilent = false;
            PostSpeak(action);
            if (action == "play")
                StatusRight.Text = "开始朗读，语速 " + App.Settings.ReadRate.ToString("0.0") + "×";
        }

        /// <summary>
        /// 停朗读。quiet = true 用在换文档、退出这种「顺带停一下」的地方，
        /// 不然状态栏会莫名其妙跳一句「朗读结束」。
        /// </summary>
        private void StopSpeak(bool quiet = false)
        {
            bool wasActive = _speakState != "stopped";
            _speakState = "stopped";
            UpdateSpeakButton();

            // 本来就没在读：不发指令，也别留「静默」标记 —— 否则会吃掉下一次
            // 正常读完时那句「朗读结束」。
            if (!wasActive || !_webReady)
            {
                _stopSilent = false;
                return;
            }

            _stopSilent = quiet;
            PostSpeak("stop");
        }

        private void PostSpeak(string action)
        {
            if (!_webReady) return;
            try
            {
                var payload = new SpeakPayload
                {
                    action = action,
                    rate = App.Settings.ReadRate
                };
                string json = JsonSerializer.Serialize(new SpeakMessage { kind = "speak", payload = payload });
                View.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                Log.Error("朗读指令发送失败", ex);
            }
        }

        private void UpdateSpeakButton()
        {
            if (SpeakButton == null) return;

            switch (_speakState)
            {
                case "playing":
                    SpeakIcon.Data = Geometry_Pause();
                    SpeakButton.ToolTip = "暂停朗读 (Ctrl+Shift+R)";
                    break;
                case "paused":
                    SpeakIcon.Data = Geometry_Play();
                    SpeakButton.ToolTip = "继续朗读 (Ctrl+Shift+R)";
                    break;
                default:
                    SpeakIcon.Data = Geometry_Play();
                    SpeakButton.ToolTip = "从开头朗读全文 (Ctrl+Shift+R)";
                    break;
            }

            // 暂停得明确写「已暂停」。以前这里只有「● 朗读中」一种文案，
            // 暂停后状态栏还挂着这四个字，看起来就像没停下来。
            if (SpeakHint == null) return;

            switch (_speakState)
            {
                case "playing":
                    SpeakHint.Text = "● 朗读中";
                    SpeakHint.SetResourceReference(TextBlock.ForegroundProperty, "ThemeAccent");
                    SpeakHint.Visibility = Visibility.Visible;
                    break;
                case "paused":
                    SpeakHint.Text = "已暂停";
                    SpeakHint.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextMuted");
                    SpeakHint.Visibility = Visibility.Visible;
                    break;
                default:
                    SpeakHint.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private static System.Windows.Media.Geometry Geometry_Play()
        {
            return System.Windows.Media.Geometry.Parse("M4.6,2.6 L12.4,8 L4.6,13.4 Z");
        }

        private static System.Windows.Media.Geometry Geometry_Pause()
        {
            return System.Windows.Media.Geometry.Parse("M4.4,2.8 H6.8 V13.2 H4.4 Z M9.2,2.8 H11.6 V13.2 H9.2 Z");
        }

        private sealed class SpeakMessage
        {
            public string kind { get; set; }
            public SpeakPayload payload { get; set; }
        }

        private sealed class SpeakPayload
        {
            public string action { get; set; }
            public double rate { get; set; }
        }

        // ───────────────────────── 导出长图 ─────────────────────────

        private async void ExportPng_Click(object sender, RoutedEventArgs e)
        {
            if (!_webReady || View.CoreWebView2 == null)
            {
                MessageBox.Show("页面还没准备好，暂时导不出长图。", "导出长图");
                return;
            }
            if (string.IsNullOrEmpty(_currentPath)) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出长图",
                Filter = "PNG 图片|*.png",
                FileName = Path.GetFileNameWithoutExtension(_currentPath) + ".png",
                InitialDirectory = Directory.Exists(App.Settings.LastDir) ? App.Settings.LastDir : null
            };
            if (dialog.ShowDialog(this) != true) return;

            StatusRight.Text = "正在生成长图…";
            bool ok = await ExportPngAsync(dialog.FileName);
            if (ok)
            {
                StatusRight.Text = "已导出长图：" + Path.GetFileName(dialog.FileName);
                ShowToast("已导出长图", Path.GetFileName(dialog.FileName), dialog.FileName);
            }
            else
            {
                StatusRight.Text = string.Empty;
                MessageBox.Show("生成失败，可能是文档太长超出截图上限。可以先减小字号再试。", "导出长图");
            }
        }

        /// <summary>
        /// 整页 PNG。WebView2 的 CapturePreviewAsync 只能截可视区域，
        /// 所以走 DevTools 协议的 Page.captureScreenshot + captureBeyondViewport。
        /// </summary>
        private async Task<bool> ExportPngAsync(string path)
        {
            if (!_webReady || View.CoreWebView2 == null) return false;

            try
            {
                await WaitForImagesAsync();
                await View.CoreWebView2.ExecuteScriptAsync(
                    "document.body.classList.add('capturing')");

                string metricsJson = await View.CoreWebView2.CallDevToolsProtocolMethodAsync(
                    "Page.getLayoutMetrics", "{}");

                double width = 1200, height = 900;
                using (JsonDocument doc = JsonDocument.Parse(metricsJson))
                {
                    JsonElement root = doc.RootElement;
                    JsonElement size;
                    if (root.TryGetProperty("cssContentSize", out size) || root.TryGetProperty("contentSize", out size))
                    {
                        if (size.TryGetProperty("width", out var w)) width = w.GetDouble();
                        if (size.TryGetProperty("height", out var h)) height = h.GetDouble();
                    }
                }

                width = Math.Max(320, Math.Min(8000, width));
                height = Math.Max(200, height);

                // Chromium 的截图有尺寸上限（约 16384px），太长就整体缩一点，
                // 宁可出图小一点，也不要整张失败。
                double scale = 1.0;
                const double maxSide = 16000;
                if (height * scale > maxSide) scale = maxSide / height;
                if (scale < 0.25) scale = 0.25;

                string clip = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{{\"x\":0,\"y\":0,\"width\":{0:0.##},\"height\":{1:0.##},\"scale\":{2:0.###}}}",
                    width, height, scale);

                string args = "{\"format\":\"png\",\"captureBeyondViewport\":true,\"fromSurface\":true,\"clip\":" + clip + "}";
                string result = await View.CoreWebView2.CallDevToolsProtocolMethodAsync(
                    "Page.captureScreenshot", args);

                string base64 = null;
                using (JsonDocument doc = JsonDocument.Parse(result))
                {
                    JsonElement data;
                    if (doc.RootElement.TryGetProperty("data", out data)) base64 = data.GetString();
                }
                if (string.IsNullOrEmpty(base64)) return false;

                byte[] bytes = Convert.FromBase64String(base64);
                File.WriteAllBytes(path, bytes);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("导出 PNG 失败", ex);
                return false;
            }
            finally
            {
                try
                {
                    if (_webReady)
                        await View.CoreWebView2.ExecuteScriptAsync("document.body.classList.remove('capturing')");
                }
                catch (Exception)
                {
                    // 收尾失败无所谓
                }
            }
        }
    }

    /// <summary>设置窗口里的皮肤下拉项。内置主题和自定义 css 混在一张表里。</summary>
    public sealed class SkinEntry
    {
        public string File { get; set; }
        public string Name { get; set; }
        public bool IsBuiltIn { get; set; }
        public bool IsDark { get; set; }
        public string ThemeId { get; set; }

        public override string ToString()
        {
            return IsBuiltIn ? Name : Name + "（自定义）";
        }
    }
}
