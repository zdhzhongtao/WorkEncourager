using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MdViewer.Core
{
    /// <summary>存 %APPDATA%\MdViewer\settings.json 的东西。Version 是配置格式版本。</summary>
    public sealed class AppSettings
    {
        /// <summary>配置格式版本。1 = 最初版；2 = 加最近条目时间、阅读宽度、自动刷新、更新设置。</summary>
        public int Version { get; set; } = SettingsStore.CurrentVersion;

        /// <summary>system / light / dark。system 表示跟随 Windows 的「应用模式」。</summary>
        public string Theme { get; set; } = "system";

        /// <summary>浅色模式下用哪套正文主题（见 MarkdownThemes）。</summary>
        public string LightThemeId { get; set; } = MarkdownThemes.DefaultLightId;

        /// <summary>深色模式下用哪套正文主题。</summary>
        public string DarkThemeId { get; set; } = MarkdownThemes.DefaultDarkId;

        /// <summary>正文字号 14~22。</summary>
        public int FontSize { get; set; } = 16;

        /// <summary>正文栏最大宽度（px）。640~1400，用来控制每行字数，太宽不好读。</summary>
        public double MaxContentWidth { get; set; } = 880;

        /// <summary>
        /// 宽屏模式：正文铺满整个窗口，不受 <see cref="MaxContentWidth"/> 限制。
        /// 给列数多的表格用 —— 窄栏里 30 列的表格每格只剩一个字宽。
        /// 不改 MaxContentWidth，关掉宽屏就回到原来那个值。
        /// </summary>
        public bool WideMode { get; set; }

        /// <summary>旧版字段：只有路径。保留是为了能从老配置迁移，新代码只写不用。</summary>
        public List<string> RecentFiles { get; set; } = new List<string>();

        /// <summary>最近打开（带时间），最新的在第 0 位。</summary>
        public List<RecentEntry> RecentEntries { get; set; } = new List<RecentEntry>();

        public double WindowWidth { get; set; } = 1200;
        public double WindowHeight { get; set; } = 820;
        public bool WindowMaximized { get; set; }
        public double TocWidth { get; set; } = 240;
        public bool TocVisible { get; set; } = true;
        public string LastDir { get; set; } = string.Empty;

        /// <summary>用户已经点过"关联 .md 文件"（仅用于界面提示，真实状态去读注册表）。</summary>
        public bool AssocHinted { get; set; }

        /// <summary>文档被别的程序改动后自动重新渲染。</summary>
        public bool AutoReload { get; set; } = true;

        /// <summary>重新打开同一篇文档时回到上次的阅读位置。</summary>
        public bool RememberScroll { get; set; } = true;

        /// <summary>每篇文档读到哪儿了（0~1 的比例），键是完整路径，最多留 40 条。</summary>
        public Dictionary<string, double> ScrollPositions { get; set; } = new Dictionary<string, double>();

        /// <summary>GitHub 仓库（owner/repo），自动更新从这里取最新 Release。</summary>
        public string UpdateRepo { get; set; } = "zdhzhongtao/WorkEncourager";

        /// <summary>启动时静默检查更新。默认关：本程序的底线是不主动联网。</summary>
        public bool CheckUpdateOnStart { get; set; }

        /// <summary>用户点过「跳过这个版本」，不再提示。</summary>
        public string SkippedVersion { get; set; } = string.Empty;

        /// <summary>上次检查更新的时间（ISO 8601）。</summary>
        public string LastUpdateCheck { get; set; } = string.Empty;
    }

    public static class SettingsStore
    {
        /// <summary>3 = 加正文主题 id（light / dark 各一套可自选）。</summary>
        public const int CurrentVersion = 3;

        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        public static string PathIn(string dataDir)
        {
            return Path.Combine(dataDir, "settings.json");
        }

        public static AppSettings Load(string dataDir)
        {
            try
            {
                var settings = AtomicFile.ReadJson<AppSettings>(PathIn(dataDir), JsonOptions);
                if (settings == null) return new AppSettings();
                Normalize(settings);
                return settings;
            }
            catch
            {
                return new AppSettings();
            }
        }

        /// <summary>把读出来的配置修补到合法范围，并把旧格式迁过来。</summary>
        public static void Normalize(AppSettings settings)
        {
            if (settings == null) return;

            int incomingVersion = settings.Version;

            if (settings.FontSize < 14 || settings.FontSize > 22) settings.FontSize = 16;

            if (double.IsNaN(settings.MaxContentWidth) || settings.MaxContentWidth < 640 || settings.MaxContentWidth > 1400)
                settings.MaxContentWidth = 880;

            string theme = (settings.Theme ?? string.Empty).Trim().ToLowerInvariant();
            if (theme != "light" && theme != "dark" && theme != "system") theme = "system";
            settings.Theme = theme;

            // 主题 id 必须真的存在，而且明暗要对得上 —— 手改配置写反了不能把界面带进深色正文配浅色壳
            MarkdownTheme lightTheme = MarkdownThemes.Resolve(settings.LightThemeId);
            if (lightTheme == null || lightTheme.IsDark) settings.LightThemeId = MarkdownThemes.DefaultLightId;

            MarkdownTheme darkTheme = MarkdownThemes.Resolve(settings.DarkThemeId);
            if (darkTheme == null || !darkTheme.IsDark) settings.DarkThemeId = MarkdownThemes.DefaultDarkId;

            if (settings.RecentEntries == null) settings.RecentEntries = new List<RecentEntry>();
            if (settings.RecentFiles == null) settings.RecentFiles = new List<string>();
            if (settings.ScrollPositions == null) settings.ScrollPositions = new Dictionary<string, double>();

            // v1 → v2：老配置只有 RecentFiles，迁到 RecentEntries（时间未知就留空）。
            // 只在版本号还是老的时候迁，否则用户手动清空列表会被旧字段又填回来。
            if (incomingVersion < 2 && settings.RecentEntries.Count == 0 && settings.RecentFiles.Count > 0)
                settings.RecentEntries = RecentFiles.FromLegacy(settings.RecentFiles);

            settings.RecentEntries = RecentFiles.Normalize(settings.RecentEntries);
            settings.RecentFiles = RecentFiles.ToPaths(settings.RecentEntries);

            if (string.IsNullOrWhiteSpace(settings.UpdateRepo)) settings.UpdateRepo = "zdhzhongtao/WorkEncourager";
            if (settings.SkippedVersion == null) settings.SkippedVersion = string.Empty;
            if (settings.LastUpdateCheck == null) settings.LastUpdateCheck = string.Empty;
            if (settings.LastDir == null) settings.LastDir = string.Empty;

            if (settings.Version < CurrentVersion) settings.Version = CurrentVersion;
        }

        /// <summary>
        /// 恢复默认设置。外观、阅读行为、更新配置全部回到出厂值。
        ///
        /// 但这些**保留**：最近打开、每篇的阅读位置、上次打开的目录、窗口大小与目录宽度、
        /// 关联提示 —— 它们是使用痕迹，不是「设置」；一并抹掉只会让人白丢东西。
        /// </summary>
        public static AppSettings Reset(AppSettings current)
        {
            var defaults = new AppSettings();
            if (current == null) return defaults;

            defaults.RecentEntries = current.RecentEntries ?? new List<RecentEntry>();
            defaults.RecentFiles = current.RecentFiles ?? new List<string>();
            defaults.ScrollPositions = current.ScrollPositions ?? new Dictionary<string, double>();
            defaults.AssocHinted = current.AssocHinted;

            defaults.WindowWidth = current.WindowWidth;
            defaults.WindowHeight = current.WindowHeight;
            defaults.WindowMaximized = current.WindowMaximized;
            defaults.TocWidth = current.TocWidth;

            defaults.LastDir = current.LastDir ?? string.Empty;

            Normalize(defaults);
            return defaults;
        }

        /// <summary>
        /// 把当前 settings.json 复制一份带时间戳的副本（重置、手工折腾之前的后悔药）。
        /// 返回副本路径；没有可备份的文件时返回 null。
        /// </summary>
        public static string Backup(string dataDir)
        {
            string source = PathIn(dataDir);
            if (!File.Exists(source)) return null;

            try
            {
                // 带上毫秒：连点两次重置也不会互相覆盖
                string dest = Path.Combine(dataDir,
                    "settings-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".bak.json");
                File.Copy(source, dest, true);
                TrimBackups(dataDir, keep: 5);
                return dest;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void TrimBackups(string dataDir, int keep)
        {
            try
            {
                var files = new List<string>(Directory.GetFiles(dataDir, "settings-*.bak.json"));
                if (files.Count <= keep) return;

                // 文件名带 yyyyMMdd-HHmmss，按名字倒序就是按时间倒序
                files.Sort(StringComparer.Ordinal);
                for (int i = 0; i < files.Count - keep; i++)
                {
                    try { File.Delete(files[i]); }
                    catch (Exception) { }
                }
            }
            catch (Exception)
            {
                // 清理失败无所谓，别影响备份本身
            }
        }

        public static void Save(string dataDir, AppSettings settings)
        {
            if (settings == null) return;
            Directory.CreateDirectory(dataDir);

            // 先把设置收敛到合法状态，再把最近列表镜像回旧字段（万一回退到老版本也还能用）
            Normalize(settings);
            settings.RecentFiles = RecentFiles.ToPaths(settings.RecentEntries);
            AtomicFile.WriteJson(PathIn(dataDir), settings, JsonOptions);
        }
    }
}
