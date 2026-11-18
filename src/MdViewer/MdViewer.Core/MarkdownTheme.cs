using System;
using System.Collections.Generic;

namespace MdViewer.Core
{
    /// <summary>
    /// 一套正文排版主题的元数据。
    ///
    /// 真正的正文配色写在 Assets/style.css 的 <c>html[data-theme="&lt;Id&gt;"]</c> 里，
    /// 这里留「界面需要知道」的东西：明暗、主色、预览色，以及 <see cref="Bg"/>/<see cref="Fg"/>/
    /// <see cref="Line"/>/<see cref="Card"/> 四个界面底色 —— 界面壳由这四个色派生（见 Theme.ForMarkdownTheme），
    /// 换主题时侧栏、工具栏、按钮跟着一起换，而不是只换个蓝色高亮。
    ///
    /// 两边靠 <see cref="Id"/> 对齐 —— 加一套主题 = 在 <see cref="MarkdownThemes"/> 里加一条 + 在 css 里加一块。
    /// </summary>
    public sealed class MarkdownTheme
    {
        /// <summary>对应 css 里 data-theme 的值，也是写进 settings.json 的东西。</summary>
        public string Id { get; set; }

        public string Name { get; set; }

        /// <summary>深色主题：决定界面走深色配色、以及「跟随系统」时该挑哪一组。</summary>
        public bool IsDark { get; set; }

        /// <summary>主色（链接、强调、进度条、界面高亮）。必须与 css 里的 --accent 一致。</summary>
        public string Accent { get; set; }

        public string AccentHover { get; set; }

        /// <summary>工具栏主题面板里的预览底色 / 前景色。</summary>
        public string SwatchBg { get; set; }
        public string SwatchFg { get; set; }

        /// <summary>正文底色，界面窗口底色的基准。必须与 css 里的 --bg 一致。</summary>
        public string Bg { get; set; }

        /// <summary>正文前景色，界面文字色与所有派生色的基准。必须与 css 里的 --fg 一致。</summary>
        public string Fg { get; set; }

        /// <summary>分隔线底色，界面边框与分隔线的基准。必须与 css 里的 --line 一致。</summary>
        public string Line { get; set; }

        /// <summary>卡片底色（代码块 / 引用块背景）。留空则由 Bg 与 Fg 混出来。</summary>
        public string Card { get; set; }

        /// <summary>
        /// 明暗配对：Ctrl+Shift+D 切换时跳到哪一套。
        /// 命名成对的（github-light/github-dark）可以靠后缀猜，Catppuccin 这种
        /// （latte / mocha）猜不出来，得显式写。
        /// </summary>
        public string PairId { get; set; }

        /// <summary>一句话风格说明，设置窗口里给个提示。</summary>
        public string Note { get; set; }

        public override string ToString()
        {
            return Name ?? Id ?? base.ToString();
        }
    }

    /// <summary>内置主题清单。顺序就是界面里的显示顺序（先浅色组，再深色组）。</summary>
    public static class MarkdownThemes
    {
        public const string DefaultLightId = "github-light";
        public const string DefaultDarkId = "github-dark";

        private static readonly MarkdownTheme[] Items =
        {
            // ───────────── 浅色 ─────────────
            new MarkdownTheme
            {
                Id = "github-light", Name = "GitHub 浅色", IsDark = false,
                Accent = "#0969DA", AccentHover = "#1F6FEB",
                SwatchBg = "#FFFFFF", SwatchFg = "#1F2328",
                Bg = "#FFFFFF", Fg = "#1F2328", Line = "#D8DEE4", Card = "#F6F8FA",
                PairId = "github-dark",
                Note = "最接近 GitHub 默认排版，通用"
            },
            new MarkdownTheme
            {
                Id = "vue", Name = "Vue 绿", IsDark = false,
                Accent = "#42B883", AccentHover = "#369E71",
                SwatchBg = "#FFFFFF", SwatchFg = "#2C3E50",
                Bg = "#FFFFFF", Fg = "#2C3E50", Line = "#E2E8F0", Card = "#F8FAFC",
                Note = "技术文档风，绿色强调"
            },
            new MarkdownTheme
            {
                Id = "one-light", Name = "One Light", IsDark = false,
                Accent = "#4078F2", AccentHover = "#2F63D8",
                SwatchBg = "#FAFAFA", SwatchFg = "#383A42",
                Bg = "#FAFAFA", Fg = "#383A42", Line = "#DBDBDC", Card = "#F0F0F1",
                PairId = "one-dark",
                Note = "Atom 浅色版，配色最克制的一档"
            },
            new MarkdownTheme
            {
                Id = "catppuccin-latte", Name = "Catppuccin Latte", IsDark = false,
                Accent = "#8839EF", AccentHover = "#7287FD",
                SwatchBg = "#EFF1F5", SwatchFg = "#4C4F69",
                Bg = "#EFF1F5", Fg = "#4C4F69", Line = "#DCE0E8", Card = "#E6E9EF",
                PairId = "catppuccin-mocha",
                Note = "低饱和灰紫，柔和但不清淡"
            },
            new MarkdownTheme
            {
                Id = "rose-pine-dawn", Name = "Rosé Pine Dawn", IsDark = false,
                Accent = "#907AA9", AccentHover = "#7C6795",
                SwatchBg = "#FAF4ED", SwatchFg = "#575279",
                Bg = "#FAF4ED", Fg = "#575279", Line = "#DFD8D0", Card = "#F2E9E1",
                Note = "暖粉紫，读散文和日记最舒服"
            },
            new MarkdownTheme
            {
                Id = "gruvbox-light", Name = "Gruvbox 浅", IsDark = false,
                Accent = "#AF3A03", AccentHover = "#8F2E02",
                SwatchBg = "#FBF1C7", SwatchFg = "#3C3836",
                Bg = "#FBF1C7", Fg = "#3C3836", Line = "#D5C4A1", Card = "#F2E5BC",
                PairId = "gruvbox-dark",
                Note = "暖黄纸感，对比度偏低"
            },
            new MarkdownTheme
            {
                Id = "solarized-light", Name = "Solarized 浅", IsDark = false,
                Accent = "#268BD2", AccentHover = "#1E7FBF",
                SwatchBg = "#FDF6E3", SwatchFg = "#586E75",
                Bg = "#FDF6E3", Fg = "#586E75", Line = "#E6DFC8", Card = "#F7F0DE",
                PairId = "solarized-dark",
                Note = "低对比暖白，长时间看眼睛省力"
            },
            new MarkdownTheme
            {
                Id = "sepia", Name = "护眼米黄", IsDark = false,
                Accent = "#B45309", AccentHover = "#92400E",
                SwatchBg = "#F8F1E3", SwatchFg = "#43382C",
                Bg = "#F8F1E3", Fg = "#43382C", Line = "#E6DAC5", Card = "#F3EADA",
                Note = "纸质书观感，夜里读长文舒服"
            },
            new MarkdownTheme
            {
                Id = "newsprint", Name = "报纸衬线", IsDark = false,
                Accent = "#8C2F1E", AccentHover = "#71261A",
                SwatchBg = "#FCFAF5", SwatchFg = "#1A1A1A",
                Bg = "#FCFAF5", Fg = "#1A1A1A", Line = "#DCD7CC", Card = "#F5F2EA",
                Note = "衬线字体 + 更松行距，适合文学类"
            },
            new MarkdownTheme
            {
                Id = "high-contrast", Name = "高对比度", IsDark = false,
                Accent = "#0B49C4", AccentHover = "#06349A",
                SwatchBg = "#FFFFFF", SwatchFg = "#000000",
                Bg = "#FFFFFF", Fg = "#000000", Line = "#000000", Card = "#F2F2F2",
                Note = "纯黑字白底、全边框，视力吃力时用这套"
            },
            new MarkdownTheme
            {
                Id = "monochrome", Name = "单色无彩", IsDark = false,
                Accent = "#333333", AccentHover = "#000000",
                SwatchBg = "#FFFFFF", SwatchFg = "#222222",
                Bg = "#FFFFFF", Fg = "#222222", Line = "#DDDDDD", Card = "#F6F6F6",
                Note = "不带任何色相，适合打印预览和纯文字"
            },

            // ───────────── 深色 ─────────────
            new MarkdownTheme
            {
                Id = "github-dark", Name = "GitHub 深色", IsDark = true,
                Accent = "#4493F8", AccentHover = "#58A6FF",
                SwatchBg = "#0D1117", SwatchFg = "#E6EDF3",
                Bg = "#0D1117", Fg = "#E6EDF3", Line = "#30363D", Card = "#161B22",
                PairId = "github-light",
                Note = "与 GitHub 深色主题一致"
            },
            new MarkdownTheme
            {
                Id = "nord", Name = "Nord", IsDark = true,
                Accent = "#88C0D0", AccentHover = "#8FBCBB",
                SwatchBg = "#2E3440", SwatchFg = "#ECEFF4",
                Bg = "#2E3440", Fg = "#ECEFF4", Line = "#3B4252", Card = "#272C36",
                Note = "冷调灰蓝，色彩克制"
            },
            new MarkdownTheme
            {
                Id = "dracula", Name = "Dracula", IsDark = true,
                Accent = "#BD93F9", AccentHover = "#CBA6FF",
                SwatchBg = "#282A36", SwatchFg = "#F8F8F2",
                Bg = "#282A36", Fg = "#F8F8F2", Line = "#44475A", Card = "#21222C",
                Note = "紫调高饱和，代码块最出彩"
            },
            new MarkdownTheme
            {
                Id = "one-dark", Name = "One Dark", IsDark = true,
                Accent = "#61AFEF", AccentHover = "#7FC0F5",
                SwatchBg = "#282C34", SwatchFg = "#ABB2BF",
                Bg = "#282C34", Fg = "#ABB2BF", Line = "#3E4451", Card = "#21252B",
                PairId = "one-light",
                Note = "Atom 经典配色，耐看"
            },
            new MarkdownTheme
            {
                Id = "tokyo-night", Name = "Tokyo Night", IsDark = true,
                Accent = "#7AA2F7", AccentHover = "#8FB4FF",
                SwatchBg = "#1A1B26", SwatchFg = "#C0CAF5",
                Bg = "#1A1B26", Fg = "#C0CAF5", Line = "#292E42", Card = "#16161E",
                Note = "深夜蓝紫，暗环境最耐看"
            },
            new MarkdownTheme
            {
                Id = "catppuccin-mocha", Name = "Catppuccin Mocha", IsDark = true,
                Accent = "#CBA6F7", AccentHover = "#DDB6F9",
                SwatchBg = "#1E1E2E", SwatchFg = "#CDD6F4",
                Bg = "#1E1E2E", Fg = "#CDD6F4", Line = "#313244", Card = "#181825",
                PairId = "catppuccin-latte",
                Note = "柔和深紫底，长时间盯着不累"
            },
            new MarkdownTheme
            {
                Id = "everforest-dark", Name = "Everforest 深", IsDark = true,
                Accent = "#A7C080", AccentHover = "#B8D49A",
                SwatchBg = "#2D353B", SwatchFg = "#D3C6AA",
                Bg = "#2D353B", Fg = "#D3C6AA", Line = "#3D484D", Card = "#272E33",
                Note = "森林绿灰，护眼系深色"
            },
            new MarkdownTheme
            {
                Id = "gruvbox-dark", Name = "Gruvbox 深", IsDark = true,
                Accent = "#FABD2F", AccentHover = "#FFC94D",
                SwatchBg = "#282828", SwatchFg = "#EBDBB2",
                Bg = "#282828", Fg = "#EBDBB2", Line = "#3C3836", Card = "#1D2021",
                PairId = "gruvbox-light",
                Note = "暖褐底 + 亮黄强调，像老终端"
            },
            new MarkdownTheme
            {
                Id = "solarized-dark", Name = "Solarized 深", IsDark = true,
                Accent = "#2AA198", AccentHover = "#35B8AE",
                SwatchBg = "#002B36", SwatchFg = "#93A1A1",
                Bg = "#002B36", Fg = "#93A1A1", Line = "#073642", Card = "#00252F",
                PairId = "solarized-light",
                Note = "低对比深青，暗环境不刺眼"
            }
        };

        public static IReadOnlyList<MarkdownTheme> All
        {
            get { return Items; }
        }

        public static MarkdownTheme DefaultLight
        {
            get { return Resolve(DefaultLightId); }
        }

        public static MarkdownTheme DefaultDark
        {
            get { return Resolve(DefaultDarkId); }
        }

        /// <summary>按 id 取；认不出来返回 null（调用方自己回落，别静默给个错的明暗）。</summary>
        public static MarkdownTheme Resolve(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            string key = id.Trim();
            for (int i = 0; i < Items.Length; i++)
            {
                if (string.Equals(Items[i].Id, key, StringComparison.OrdinalIgnoreCase)) return Items[i];
            }
            return null;
        }

        /// <summary>按 id 取，认不出来就回落到该明暗的默认主题。</summary>
        public static MarkdownTheme ResolveOrFallback(string id, bool dark)
        {
            MarkdownTheme theme = Resolve(id);
            if (theme != null && theme.IsDark == dark) return theme;
            return ForMode(dark);
        }

        public static MarkdownTheme ForMode(bool dark)
        {
            return dark ? DefaultDark : DefaultLight;
        }

        /// <summary>指定明暗这一组（设置窗口的两个下拉各自用）。</summary>
        public static List<MarkdownTheme> OfMode(bool dark)
        {
            var list = new List<MarkdownTheme>();
            for (int i = 0; i < Items.Length; i++)
            {
                if (Items[i].IsDark == dark) list.Add(Items[i]);
            }
            return list;
        }

        public static bool IsKnown(string id)
        {
            return Resolve(id) != null;
        }

        /// <summary>
        /// 明暗切换时挑同组的对应主题：优先显式配对（Catppuccin latte ↔ mocha），
        /// 其次按 -light / -dark 后缀猜（github-light ↔ github-dark），最后落到该组第一套。
        /// </summary>
        public static string FamilyFallback(string currentId, bool wantDark)
        {
            MarkdownTheme current = Resolve(currentId);
            if (current == null) return ForMode(wantDark).Id;
            if (current.IsDark == wantDark) return current.Id;

            MarkdownTheme paired = Resolve(current.PairId);
            if (paired != null && paired.IsDark == wantDark) return paired.Id;

            string family = current.Id.Replace("-light", string.Empty).Replace("-dark", string.Empty);
            MarkdownTheme bySuffix = Resolve(family + (wantDark ? "-dark" : "-light"));
            if (bySuffix != null && bySuffix.IsDark == wantDark) return bySuffix.Id;
            return ForMode(wantDark).Id;
        }
    }
}
