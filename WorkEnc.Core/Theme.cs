using System;
using System.Collections.Generic;
using System.Linq;

namespace WorkEnc.Core
{
    /// <summary>主题配色。</summary>
    public class Theme
    {
        public string Name { get; set; } = "light";

        public string ShellBg { get; set; } = "#EDFFFFFF";
        public string ShellBorder { get; set; } = "#E7ECF3";
        public string CardBg { get; set; } = "#FBFCFE";
        public string CardBorder { get; set; } = "#EFF2F7";
        public string TextPrimary { get; set; } = "#0F172A";
        public string TextSecondary { get; set; } = "#475467";
        public string TextMuted { get; set; } = "#8A94A6";
        public string TextFaint { get; set; } = "#A5AEBD";
        public string Accent { get; set; } = "#F97316";
        public string AccentDark { get; set; } = "#C2410C";
        public string AccentSoft { get; set; } = "#FFF7ED";
        public string AccentSoftBorder { get; set; } = "#FED7AA";
        public string EncBg { get; set; } = "#FFF8F1";
        public string EncBorder { get; set; } = "#FBE3C7";
        public string EncText { get; set; } = "#9A3412";
        public string BarBg { get; set; } = "#F1F4F8";
        public string Divider { get; set; } = "#F1F4F8";
        public string ChipWorkingBg { get; set; } = "#ECFDF3";
        public string ChipWorkingFg { get; set; } = "#067647";
        public string ChipWorkingDot { get; set; } = "#12B76A";
        public string ChipOffBg { get; set; } = "#F2F4F7";
        public string ChipOffFg { get; set; } = "#475467";
        public string ChipOffDot { get; set; } = "#98A2B3";
        public string ChipWaitBg { get; set; } = "#EFF6FF";
        public string ChipWaitFg { get; set; } = "#1D4ED8";
        public string ChipWaitDot { get; set; } = "#3B82F6";
        public string ChipPausedBg { get; set; } = "#FFFAEB";
        public string ChipPausedFg { get; set; } = "#B45309";
        public string ChipPausedDot { get; set; } = "#F59E0B";

        /// <summary>Tab 条底色。</summary>
        public string TabBarBg { get; set; } = "#F1F4F8";
        /// <summary>选中 Tab 的底色。</summary>
        public string TabActiveBg { get; set; } = "#FFFFFF";
        /// <summary>选中 Tab 的边框色。</summary>
        public string TabActiveBorder { get; set; } = "#FED7AA";
        /// <summary>选中 Tab 的文字色。</summary>
        public string TabActiveFg { get; set; } = "#C2410C";
        /// <summary>未选中 Tab 的文字色。</summary>
        public string TabIdleFg { get; set; } = "#8A94A6";

        // ---- 输入框 ----
        /// <summary>输入框底色。</summary>
        public string InputBg { get; set; } = "#FBFCFE";
        /// <summary>输入框边框色。</summary>
        public string InputBorder { get; set; } = "#E9EDF2";
        /// <summary>输入框文字色。</summary>
        public string InputFg { get; set; } = "#0F172A";
        /// <summary>输入框获得焦点时的底色。</summary>
        public string InputFocusBg { get; set; } = "#FFFFFF";
        /// <summary>悬停时的边框色（输入框、星期按钮、语气按钮共用）。</summary>
        public string BorderHover { get; set; } = "#D8DFE9";

        // ---- 次级按钮 ----
        /// <summary>次级按钮底色。</summary>
        public string GhostBg { get; set; } = "#FBFCFD";
        /// <summary>次级按钮边框色。</summary>
        public string GhostBorder { get; set; } = "#E9EDF2";
        /// <summary>次级按钮文字色。</summary>
        public string GhostFg { get; set; } = "#475467";
        /// <summary>次级按钮悬停底色。</summary>
        public string GhostHoverBg { get; set; } = "#FFFFFF";

        // ---- 交互态 ----
        /// <summary>悬停底色。</summary>
        public string HoverBg { get; set; } = "#F2F4F7";
        /// <summary>按下底色。</summary>
        public string PressedBg { get; set; } = "#E9EDF2";
        /// <summary>设置页字段标签文字色。</summary>
        public string FieldLabelFg { get; set; } = "#475467";
        /// <summary>强调块（选中态底色）悬停时的颜色。</summary>
        public string AccentSoftHover { get; set; } = "#FFE8D5";

        // ---- 提示色 ----
        /// <summary>错误提示色。</summary>
        public string Danger { get; set; } = "#EF4444";
        /// <summary>警告提示色。</summary>
        public string Warning { get; set; } = "#D97706";

        // ---- 滚动条 / 滑块 ----
        /// <summary>细滚动条滑块色。</summary>
        public string ScrollThumb { get; set; } = "#C9D0DB";
        /// <summary>细滚动条滑块悬停色。</summary>
        public string ScrollThumbHover { get; set; } = "#AEB7C4";
        /// <summary>滑块轨道底色。</summary>
        public string TrackBg { get; set; } = "#EFF2F7";
        /// <summary>滑块圆点描边色（要跟轨道底色区分开）。</summary>
        public string ThumbStroke { get; set; } = "#FFFFFF";

        public static Theme Light() => new Theme();

        public static Theme Dark() => new Theme
        {
            Name = "dark",
            ShellBg = "#F01C2530",
            ShellBorder = "#33FFFFFF",
            CardBg = "#F21A2230",
            CardBorder = "#2EFFFFFF",
            TextPrimary = "#F1F5F9",
            TextSecondary = "#B6C2D4",
            TextMuted = "#8794A8",
            TextFaint = "#6B7789",
            Accent = "#FB923C",
            AccentDark = "#FDBA74",
            AccentSoft = "#33FB923C",
            AccentSoftBorder = "#4DF97316",
            EncBg = "#2EF97316",
            EncBorder = "#4DF97316",
            EncText = "#FDBA74",
            BarBg = "#26FFFFFF",
            Divider = "#1FFFFFFF",
            ChipWorkingBg = "#3334D399",
            ChipWorkingFg = "#6EE7B7",
            ChipWorkingDot = "#34D399",
            ChipOffBg = "#26FFFFFF",
            ChipOffFg = "#94A3B8",
            ChipOffDot = "#64748B",
            ChipWaitBg = "#333B82F6",
            ChipWaitFg = "#93C5FD",
            ChipWaitDot = "#60A5FA",
            ChipPausedBg = "#33F59E0B",
            ChipPausedFg = "#FCD34D",
            ChipPausedDot = "#FBBF24",
            TabBarBg = "#1FFFFFFF",
            TabActiveBg = "#33FFFFFF",
            TabActiveBorder = "#4DF97316",
            TabActiveFg = "#FDBA74",
            TabIdleFg = "#8794A8",

            InputBg = "#E61C2637",
            InputBorder = "#3AFFFFFF",
            InputFg = "#F1F5F9",
            InputFocusBg = "#FF1F2A3D",
            BorderHover = "#59FFFFFF",

            GhostBg = "#26FFFFFF",
            GhostBorder = "#33FFFFFF",
            GhostFg = "#B6C2D4",
            GhostHoverBg = "#3DFFFFFF",

            HoverBg = "#33FFFFFF",
            PressedBg = "#4DFFFFFF",
            FieldLabelFg = "#B6C2D4",
            AccentSoftHover = "#4DF97316",

            Danger = "#F87171",
            Warning = "#FBBF24",

            ScrollThumb = "#4DFFFFFF",
            ScrollThumbHover = "#80FFFFFF",
            TrackBg = "#26FFFFFF",
            ThumbStroke = "#1C2530"
        };

        public static Theme Get(string name) =>
            string.Equals(name, "dark", StringComparison.OrdinalIgnoreCase) ? Dark() : Light();

        /// <summary>
        /// 主题色 → WPF 资源键。
        ///
        /// XAML 里统一用 <c>{DynamicResource ThemeXxx}</c> 引用这些键，
        /// 切主题时只要换掉资源字典，界面各处（包括那些没有 x:Name 的小标签）
        /// 都会跟着变，不必在代码里一个个控件去刷画刷。
        /// </summary>
        public Dictionary<string, string> ToResourceMap()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "ThemeShellBg", ShellBg },
                { "ThemeShellBorder", ShellBorder },
                { "ThemeCardBg", CardBg },
                { "ThemeCardBorder", CardBorder },
                { "ThemeTextPrimary", TextPrimary },
                { "ThemeTextSecondary", TextSecondary },
                { "ThemeTextMuted", TextMuted },
                { "ThemeTextFaint", TextFaint },
                { "ThemeAccent", Accent },
                { "ThemeAccentDark", AccentDark },
                { "ThemeAccentSoft", AccentSoft },
                { "ThemeAccentSoftBorder", AccentSoftBorder },
                { "ThemeAccentSoftHover", AccentSoftHover },
                { "ThemeEncBg", EncBg },
                { "ThemeEncBorder", EncBorder },
                { "ThemeEncText", EncText },
                { "ThemeBarBg", BarBg },
                { "ThemeDivider", Divider },
                { "ThemeInputBg", InputBg },
                { "ThemeInputBorder", InputBorder },
                { "ThemeInputFg", InputFg },
                { "ThemeInputFocusBg", InputFocusBg },
                { "ThemeBorderHover", BorderHover },
                { "ThemeGhostBg", GhostBg },
                { "ThemeGhostBorder", GhostBorder },
                { "ThemeGhostFg", GhostFg },
                { "ThemeGhostHoverBg", GhostHoverBg },
                { "ThemeHoverBg", HoverBg },
                { "ThemePressedBg", PressedBg },
                { "ThemeFieldLabelFg", FieldLabelFg },
                { "ThemeDanger", Danger },
                { "ThemeWarning", Warning },
                { "ThemeScrollThumb", ScrollThumb },
                { "ThemeScrollThumbHover", ScrollThumbHover },
                { "ThemeTrackBg", TrackBg },
                { "ThemeThumbStroke", ThumbStroke },
                { "ThemeTabBarBg", TabBarBg },
                { "ThemeTabActiveBg", TabActiveBg },
                { "ThemeTabActiveBorder", TabActiveBorder },
                { "ThemeTabActiveFg", TabActiveFg },
                { "ThemeTabIdleFg", TabIdleFg },
                { "ThemeChipWorkingBg", ChipWorkingBg },
                { "ThemeChipWorkingFg", ChipWorkingFg },
                { "ThemeChipWorkingDot", ChipWorkingDot },
                { "ThemeChipOffBg", ChipOffBg },
                { "ThemeChipOffFg", ChipOffFg },
                { "ThemeChipOffDot", ChipOffDot },
                { "ThemeChipWaitBg", ChipWaitBg },
                { "ThemeChipWaitFg", ChipWaitFg },
                { "ThemeChipWaitDot", ChipWaitDot },
                { "ThemeChipPausedBg", ChipPausedBg },
                { "ThemeChipPausedFg", ChipPausedFg },
                { "ThemeChipPausedDot", ChipPausedDot },
            };
        }
    }
}
