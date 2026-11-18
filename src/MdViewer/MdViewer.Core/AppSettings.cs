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

        /// <summary>正文字体名（系统字体）。空 = 用主题内置的默认字体栈。</summary>
        public string FontBody { get; set; } = string.Empty;

        /// <summary>代码字体名。空 = Consolas 那一套。</summary>
        public string FontCode { get; set; } = string.Empty;

        /// <summary>标题字体名。空 = 跟随正文（不受 <see cref="FontBody"/> 影响时也一样，因为 css 里是 inherit）。</summary>
        public string FontHeading { get; set; } = string.Empty;

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

        // ───────────────────────── 0.5.0 新增 ─────────────────────────

        /// <summary>
        /// 定时换肤：到了 <see cref="AutoDimFrom"/> 自动切深色，到 <see cref="AutoDimTo"/> 切回浅色。
        /// 比「跟随系统」更贴合实际 —— 很多人 Windows 的应用模式永远不切。
        /// </summary>
        public bool AutoDim { get; set; }

        /// <summary>切深色的时刻（HH:mm）。</summary>
        public string AutoDimFrom { get; set; } = "19:00";

        /// <summary>切回浅色的时刻（HH:mm）。</summary>
        public string AutoDimTo { get; set; } = "07:00";

        /// <summary>侧栏当前页签：toc（大纲）/ files（文件）/ notes（书签 + 标注）。</summary>
        public string SidebarTab { get; set; } = "toc";

        /// <summary>当前自定义皮肤（%APPDATA%\MdViewer\themes 下的 css 文件名，空 = 用内置主题）。</summary>
        public string SkinFile { get; set; } = string.Empty;

        /// <summary>文件夹树打开的根目录。</summary>
        public string FolderRoot { get; set; } = string.Empty;

        /// <summary>启动时打开上次看的那篇。</summary>
        public bool RestoreLastDocument { get; set; } = true;

        /// <summary>上次看的文档（会话恢复用）。</summary>
        public string LastDocument { get; set; } = string.Empty;

        /// <summary>上次打开着的标签（会话恢复用，最新的在最后）。</summary>
        public List<string> OpenTabs { get; set; } = new List<string>();

        /// <summary>「用其他程序编辑」用的命令（空 = 自动找 VS Code → Typora → 记事本）。</summary>
        public string ExternalEditor { get; set; } = string.Empty;

        /// <summary>朗读语速 0.5~2.0，1 = 正常。</summary>
        public double ReadRate { get; set; } = 1.0;
    }

    public static class SettingsStore
    {
        /// <summary>4 = 加字体、定时换肤、侧栏页签、会话恢复、外部编辑器、朗读语速。</summary>
        public const int CurrentVersion = 4;

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

            // ── 0.5.0 新增字段的收敛 ──
            settings.FontBody = CleanFontName(settings.FontBody);
            settings.FontCode = CleanFontName(settings.FontCode);
            settings.FontHeading = CleanFontName(settings.FontHeading);

            settings.AutoDimFrom = CleanClock(settings.AutoDimFrom, "19:00");
            settings.AutoDimTo = CleanClock(settings.AutoDimTo, "07:00");

            string tab = (settings.SidebarTab ?? string.Empty).Trim().ToLowerInvariant();
            if (tab == "marks") tab = "notes";   // 早期版本叫 marks，读老配置时改过来
            if (tab != "toc" && tab != "files" && tab != "notes") tab = "toc";
            settings.SidebarTab = tab;

            if (settings.FolderRoot == null) settings.FolderRoot = string.Empty;
            if (settings.LastDocument == null) settings.LastDocument = string.Empty;
            if (settings.ExternalEditor == null) settings.ExternalEditor = string.Empty;
            if (settings.SkinFile == null) settings.SkinFile = string.Empty;

            if (settings.OpenTabs == null) settings.OpenTabs = new List<string>();
            else
            {
                // 去空、去重（同一个文件不该在标签栏里出现两次），最多留 20 个
                var kept = new List<string>();
                foreach (var path in settings.OpenTabs)
                {
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    if (kept.Contains(path)) continue;
                    kept.Add(path);
                }
                if (kept.Count > 20) kept.RemoveRange(0, kept.Count - 20);
                settings.OpenTabs = kept;
            }

            if (double.IsNaN(settings.ReadRate) || settings.ReadRate < 0.5 || settings.ReadRate > 2.0)
                settings.ReadRate = 1.0;

            if (settings.Version < CurrentVersion) settings.Version = CurrentVersion;
        }

        /// <summary>字体名只留第一个名字，去掉引号和逗号 —— 这个值会被拼进 css 的 font-family。</summary>
        private static string CleanFontName(string value)
        {
            string text = (value ?? string.Empty).Trim();
            if (text.Length == 0) return string.Empty;

            int comma = text.IndexOf(',');
            if (comma > 0) text = text.Substring(0, comma).Trim();
            text = text.Replace("\"", string.Empty).Replace("'", string.Empty).Trim();
            if (text.Length > 120) text = text.Substring(0, 120);
            return text;
        }

        /// <summary>
        /// 把 "9:5"、"19:00"、"0700" 这类输入收敛成 "09:05" / "07:00"；认不出来就用默认值。
        /// 宽容一点是因为用户手改配置时经常省掉冒号。
        /// </summary>
        private static string CleanClock(string value, string fallback)
        {
            string text = (value ?? string.Empty).Trim().Replace('：', ':');
            if (text.Length == 0) return fallback;

            int hour, minute = 0;
            int colon = text.IndexOf(':');

            if (colon > 0)
            {
                if (!int.TryParse(text.Substring(0, colon), out hour)) return fallback;
                if (!int.TryParse(text.Substring(colon + 1), out minute)) return fallback;
            }
            else if (text.Length <= 2)
            {
                if (!int.TryParse(text, out hour)) return fallback;
            }
            else if (text.Length == 4
                     && int.TryParse(text.Substring(0, 2), out hour)
                     && int.TryParse(text.Substring(2, 2), out minute))
            {
                // "0700" 这种紧凑写法
            }
            else
            {
                return fallback;
            }

            if (hour < 0 || hour > 23 || minute < 0 || minute > 59) return fallback;
            return hour.ToString("00") + ":" + minute.ToString("00");
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

            // 会话痕迹也保留：下次打开还是那几篇、那个目录
            defaults.OpenTabs = current.OpenTabs ?? new List<string>();
            defaults.LastDocument = current.LastDocument ?? string.Empty;
            defaults.FolderRoot = current.FolderRoot ?? string.Empty;
            defaults.SidebarTab = current.SidebarTab ?? "toc";

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
