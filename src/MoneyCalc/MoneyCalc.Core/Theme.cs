using System;
using System.Collections.Generic;

namespace MoneyCalc.Core
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
