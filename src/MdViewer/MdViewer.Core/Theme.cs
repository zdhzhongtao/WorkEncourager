using System;
using System.Collections.Generic;
using System.Globalization;

namespace MdViewer.Core
{
    /// <summary>
    /// 主题配色（浅色 / 深色两套）。
    ///
    /// XAML 里统一用 <c>{DynamicResource ThemeXxx}</c> 引用，切主题时由
    /// <see cref="ApplyTo"/> 把值刷进 Application.Resources，界面各处自动跟着变。
    /// 取值见 docs/共享约定.md。
    /// </summary>
    public class Theme
    {
        public string Name { get; set; } = "light";

        /// <summary>窗口底色。</summary>
        public string WindowBg { get; set; } = "#F8FAFC";
        /// <summary>卡片底色（比窗口底色差一档）。</summary>
        public string CardBg { get; set; } = "#FFFFFF";
        public string CardBorder { get; set; } = "#E2E8F0";
        public string TextPrimary { get; set; } = "#1E293B";
        public string TextSecondary { get; set; } = "#475569";
        public string TextMuted { get; set; } = "#94A3B8";
        public string Accent { get; set; } = "#2563EB";
        public string AccentHover { get; set; } = "#1D4ED8";
        public string OnAccent { get; set; } = "#FFFFFF";
        public string Danger { get; set; } = "#DC2626";
        public string Success { get; set; } = "#16A34A";
        public string Warning { get; set; } = "#D97706";
        public string Divider { get; set; } = "#E2E8F0";

        public string InputBg { get; set; } = "#FFFFFF";
        public string InputBorder { get; set; } = "#CBD5E1";
        public string InputFg { get; set; } = "#1E293B";

        public string BtnBg { get; set; } = "#FFFFFF";
        public string BtnBorder { get; set; } = "#CBD5E1";
        public string BtnFg { get; set; } = "#475569";
        public string HoverBg { get; set; } = "#F1F5F9";
        public string PressedBg { get; set; } = "#E2E8F0";
        public string DisabledFg { get; set; } = "#CBD5E1";

        /// <summary>表格表头底色。</summary>
        public string GridHeaderBg { get; set; } = "#F1F5F9";
        /// <summary>表格斑马纹底色。</summary>
        public string GridAltBg { get; set; } = "#FBFCFE";
        public string GridLine { get; set; } = "#E2E8F0";
        public string SelectionBg { get; set; } = "#DBEAFE";
        public string SelectionFg { get; set; } = "#1E293B";

        /// <summary>差异单元格底色（diff 语义色，不是涨跌色）。</summary>
        public string DiffCellBg { get; set; } = "#FEF3C7";
        public string DiffCellFg { get; set; } = "#92400E";
        /// <summary>仅 A 有 / 仅 B 有的行标色。</summary>
        public string OnlyAFg { get; set; } = "#DC2626";
        public string OnlyBFg { get; set; } = "#16A34A";

        /// <summary>侧栏 / 导航底色。</summary>
        public string NavBg { get; set; } = "#F1F5F9";
        public string NavActiveBg { get; set; } = "#DBEAFE";
        public string NavActiveFg { get; set; } = "#1D4ED8";
        public string NavFg { get; set; } = "#475569";

        /// <summary>滚动条滑块。</summary>
        public string ScrollThumb { get; set; } = "#CBD5E1";
        public string TrackBg { get; set; } = "#F1F5F9";

        public static Theme Light() => new Theme();

        public static Theme Dark() => new Theme
        {
            Name = "dark",
            WindowBg = "#1E293B",
            CardBg = "#0F172A",
            CardBorder = "#334155",
            TextPrimary = "#E2E8F0",
            TextSecondary = "#CBD5E1",
            TextMuted = "#94A3B8",
            Accent = "#3B82F6",
            AccentHover = "#60A5FA",
            OnAccent = "#FFFFFF",
            Danger = "#F87171",
            Success = "#4ADE80",
            Warning = "#FBBF24",
            Divider = "#334155",

            InputBg = "#0F172A",
            InputBorder = "#475569",
            InputFg = "#E2E8F0",

            BtnBg = "#1E293B",
            BtnBorder = "#475569",
            BtnFg = "#CBD5E1",
            HoverBg = "#27364D",
            PressedBg = "#334155",
            DisabledFg = "#64748B",

            GridHeaderBg = "#16233A",
            GridAltBg = "#16203A",
            GridLine = "#334155",
            SelectionBg = "#1D3A63",
            SelectionFg = "#E2E8F0",

            DiffCellBg = "#4A3A12",
            DiffCellFg = "#FDE68A",
            OnlyAFg = "#F87171",
            OnlyBFg = "#4ADE80",

            NavBg = "#16233A",
            NavActiveBg = "#1D3A63",
            NavActiveFg = "#93C5FD",
            NavFg = "#94A3B8",

            ScrollThumb = "#475569",
            TrackBg = "#1E293B"
        };

        public static Theme Get(string name) =>
            string.Equals(name, "dark", StringComparison.OrdinalIgnoreCase) ? Dark() : Light();

        /// <summary>
        /// 由正文主题派生整套界面配色。
        ///
        /// 0.5.0 起不再是「浅底 / 深底两套骨架 + 只换主色」：主题自带 Bg/Fg/Line/Card 四个壳色，
        /// 其余 30 多个字段全部由这四个色混出来（明度阶梯 0.02 / 0.04 / 0.07 / 0.12 ……）。
        /// 好处是「换一套主题 = 整机换肤」，而维护成本仍然只有主题里的四个色值；
        /// 代价是不能再给某套主题单独微调某一个字段 —— 需要微调就给它写更准的 Bg/Fg/Line/Card。
        /// </summary>
        public static Theme ForMarkdownTheme(MarkdownTheme markdown)
        {
            if (markdown == null) return Light();

            Theme theme = markdown.IsDark ? Dark() : Light();
            theme.Name = markdown.Id;

            // 老配置 / 手写主题可能没填壳色，退回骨架值，不至于变成一片黑
            string bg = Pick(markdown.Bg, markdown.SwatchBg, theme.CardBg);
            string fg = Pick(markdown.Fg, markdown.SwatchFg, theme.TextPrimary);
            string line = Pick(markdown.Line, theme.Divider);
            string card = Pick(markdown.Card, Mix(bg, fg, 0.04));

            theme.WindowBg = bg;
            theme.CardBg = card;
            theme.CardBorder = line;
            theme.Divider = line;
            theme.GridLine = line;

            theme.TextPrimary = fg;
            theme.TextSecondary = Mix(fg, bg, 0.20);
            theme.TextMuted = Mix(fg, bg, 0.42);

            theme.InputBg = Mix(bg, fg, 0.02);
            theme.InputBorder = Mix(bg, fg, 0.28);
            theme.InputFg = fg;

            theme.BtnBg = Mix(bg, fg, 0.04);
            theme.BtnBorder = Mix(bg, fg, 0.22);
            theme.BtnFg = Mix(fg, bg, 0.08);
            theme.HoverBg = Mix(bg, fg, 0.07);
            theme.PressedBg = Mix(bg, fg, 0.12);
            theme.DisabledFg = Mix(fg, bg, 0.55);

            theme.GridHeaderBg = Mix(bg, fg, 0.05);
            theme.GridAltBg = Mix(bg, fg, 0.025);
            theme.ScrollThumb = Mix(bg, fg, 0.28);
            theme.TrackBg = bg;

            string accent = string.IsNullOrWhiteSpace(markdown.Accent) ? theme.Accent : markdown.Accent.Trim();
            string hover = string.IsNullOrWhiteSpace(markdown.AccentHover) ? accent : markdown.AccentHover.Trim();

            theme.Accent = accent;
            theme.AccentHover = hover;
            theme.OnAccent = ContrastOn(accent, theme.OnAccent);
            theme.NavActiveFg = accent;
            theme.NavActiveBg = Mix(bg, accent, markdown.IsDark ? 0.24 : 0.12);
            theme.SelectionBg = Mix(bg, accent, markdown.IsDark ? 0.32 : 0.18);
            theme.SelectionFg = theme.TextPrimary;

            theme.NavBg = Mix(bg, fg, markdown.IsDark ? 0.05 : 0.035);
            theme.NavFg = theme.TextSecondary;

            // 语义色（对错 / 增删 / 差异）跟明暗走，不跟主题走：这是信号，不是风格
            return theme;
        }

        /// <summary>取第一个非空的颜色值。</summary>
        private static string Pick(params string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(candidates[i])) return candidates[i].Trim();
            }
            return "#808080";
        }

        // ───────────── 颜色小工具（Core 不引用 WPF，所以自己算） ─────────────

        private struct Rgb
        {
            public int R;
            public int G;
            public int B;
        }

        private static bool TryParseColor(string hex, out Rgb value)
        {
            value = new Rgb();
            if (string.IsNullOrWhiteSpace(hex)) return false;

            string text = hex.Trim();
            if (text.Length > 0 && text[0] == '#') text = text.Substring(1);
            if (text.Length == 3)
                text = new string(new[] { text[0], text[0], text[1], text[1], text[2], text[2] });
            if (text.Length != 6) return false;

            int parsed;
            if (!int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed)) return false;

            value = new Rgb
            {
                R = (parsed >> 16) & 0xFF,
                G = (parsed >> 8) & 0xFF,
                B = parsed & 0xFF
            };
            return true;
        }

        /// <summary>把 accent 按 amount 比例混进背景色，得到「压低饱和度的高亮底色」。</summary>
        private static string Mix(string background, string accent, double amount)
        {
            Rgb back, front;
            if (!TryParseColor(background, out back) || !TryParseColor(accent, out front))
                return string.IsNullOrWhiteSpace(accent) ? background : accent;

            double ratio = Math.Max(0, Math.Min(1, amount));
            return "#"
                + Blend(back.R, front.R, ratio)
                + Blend(back.G, front.G, ratio)
                + Blend(back.B, front.B, ratio);
        }

        private static string Blend(int from, int to, double amount)
        {
            int value = (int)Math.Round(from + (to - from) * amount);
            if (value < 0) value = 0;
            if (value > 255) value = 255;
            return value.ToString("X2", CultureInfo.InvariantCulture);
        }

        /// <summary>主色偏亮（Nord 的冰蓝、Dracula 的浅紫）时白字看不清，得换成深色字。</summary>
        private static string ContrastOn(string accent, string fallback)
        {
            Rgb color;
            if (!TryParseColor(accent, out color)) return fallback;

            double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
            return luminance > 0.62 ? "#0B1220" : "#FFFFFF";
        }

        /// <summary>主题字段名 → WPF 资源键（XAML 里写 {DynamicResource ThemeXxx}）。</summary>
        public Dictionary<string, string> ToResourceMap()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "ThemeWindowBg", WindowBg },
                { "ThemeCardBg", CardBg },
                { "ThemeCardBorder", CardBorder },
                { "ThemeTextPrimary", TextPrimary },
                { "ThemeTextSecondary", TextSecondary },
                { "ThemeTextMuted", TextMuted },
                { "ThemeAccent", Accent },
                { "ThemeAccentHover", AccentHover },
                { "ThemeOnAccent", OnAccent },
                { "ThemeDanger", Danger },
                { "ThemeSuccess", Success },
                { "ThemeWarning", Warning },
                { "ThemeDivider", Divider },
                { "ThemeInputBg", InputBg },
                { "ThemeInputBorder", InputBorder },
                { "ThemeInputFg", InputFg },
                { "ThemeBtnBg", BtnBg },
                { "ThemeBtnBorder", BtnBorder },
                { "ThemeBtnFg", BtnFg },
                { "ThemeHoverBg", HoverBg },
                { "ThemePressedBg", PressedBg },
                { "ThemeDisabledFg", DisabledFg },
                { "ThemeGridHeaderBg", GridHeaderBg },
                { "ThemeGridAltBg", GridAltBg },
                { "ThemeGridLine", GridLine },
                { "ThemeSelectionBg", SelectionBg },
                { "ThemeSelectionFg", SelectionFg },
                { "ThemeDiffCellBg", DiffCellBg },
                { "ThemeDiffCellFg", DiffCellFg },
                { "ThemeOnlyAFg", OnlyAFg },
                { "ThemeOnlyBFg", OnlyBFg },
                { "ThemeNavBg", NavBg },
                { "ThemeNavActiveBg", NavActiveBg },
                { "ThemeNavActiveFg", NavActiveFg },
                { "ThemeNavFg", NavFg },
                { "ThemeScrollThumb", ScrollThumb },
                { "ThemeTrackBg", TrackBg }
            };
        }
    }
}
