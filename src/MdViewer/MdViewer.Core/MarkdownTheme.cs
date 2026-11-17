using System;
using System.Collections.Generic;

namespace MdViewer.Core
{
    /// <summary>
    /// 一套正文排版主题的元数据。
    ///
    /// 真正的配色写在 Assets/style.css 的 <c>html[data-theme="&lt;Id&gt;"]</c> 里，
    /// 这里只留「界面需要知道」的那几项：明暗、主色、预览色。
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
            new MarkdownTheme
            {
                Id = "github-light", Name = "GitHub 浅色", IsDark = false,
                Accent = "#0969DA", AccentHover = "#1F6FEB",
                SwatchBg = "#FFFFFF", SwatchFg = "#1F2328",
                Note = "最接近 GitHub 默认排版，通用"
            },
            new MarkdownTheme
            {
                Id = "vue", Name = "Vue 绿", IsDark = false,
                Accent = "#42B883", AccentHover = "#369E71",
                SwatchBg = "#FFFFFF", SwatchFg = "#2C3E50",
                Note = "技术文档风，绿色强调"
            },
            new MarkdownTheme
            {
                Id = "solarized-light", Name = "Solarized 浅", IsDark = false,
                Accent = "#268BD2", AccentHover = "#1E7FBF",
                SwatchBg = "#FDF6E3", SwatchFg = "#586E75",
                Note = "低对比暖白，长时间看眼睛省力"
            },
            new MarkdownTheme
            {
                Id = "sepia", Name = "护眼米黄", IsDark = false,
                Accent = "#B45309", AccentHover = "#92400E",
                SwatchBg = "#F8F1E3", SwatchFg = "#43382C",
                Note = "纸质书观感，夜里读长文舒服"
            },
            new MarkdownTheme
            {
                Id = "newsprint", Name = "报纸衬线", IsDark = false,
                Accent = "#8C2F1E", AccentHover = "#71261A",
                SwatchBg = "#FCFAF5", SwatchFg = "#1A1A1A",
                Note = "衬线字体 + 更松行距，适合文学类"
            },
            new MarkdownTheme
            {
                Id = "github-dark", Name = "GitHub 深色", IsDark = true,
                Accent = "#4493F8", AccentHover = "#58A6FF",
                SwatchBg = "#0D1117", SwatchFg = "#E6EDF3",
                Note = "与 GitHub 深色主题一致"
            },
            new MarkdownTheme
            {
                Id = "nord", Name = "Nord", IsDark = true,
                Accent = "#88C0D0", AccentHover = "#8FBCBB",
                SwatchBg = "#2E3440", SwatchFg = "#ECEFF4",
                Note = "冷调灰蓝，色彩克制"
            },
            new MarkdownTheme
            {
                Id = "dracula", Name = "Dracula", IsDark = true,
                Accent = "#BD93F9", AccentHover = "#CBA6FF",
                SwatchBg = "#282A36", SwatchFg = "#F8F8F2",
                Note = "紫调高饱和，代码块最出彩"
            },
            new MarkdownTheme
            {
                Id = "one-dark", Name = "One Dark", IsDark = true,
                Accent = "#61AFEF", AccentHover = "#7FC0F5",
                SwatchBg = "#282C34", SwatchFg = "#ABB2BF",
                Note = "Atom 经典配色，耐看"
            },
            new MarkdownTheme
            {
                Id = "solarized-dark", Name = "Solarized 深", IsDark = true,
                Accent = "#2AA198", AccentHover = "#35B8AE",
                SwatchBg = "#002B36", SwatchFg = "#93A1A1",
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

        /// <summary>明暗切换时挑同组的邻近主题：优先同名家族，其次该组第一套。</summary>
        public static string FamilyFallback(string currentId, bool wantDark)
        {
            MarkdownTheme current = Resolve(currentId);
            if (current == null) return ForMode(wantDark).Id;
            if (current.IsDark == wantDark) return current.Id;

            // github-light ↔ github-dark、solarized-light ↔ solarized-dark 这类命名是成对的
            string family = current.Id.Replace("-light", string.Empty).Replace("-dark", string.Empty);
            MarkdownTheme paired = Resolve(family + (wantDark ? "-dark" : "-light"));
            if (paired != null && paired.IsDark == wantDark) return paired.Id;
            return ForMode(wantDark).Id;
        }
    }
}
