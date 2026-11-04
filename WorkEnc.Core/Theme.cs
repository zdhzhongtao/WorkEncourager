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
            TabIdleFg = "#8794A8"
        };

        public static Theme Get(string name) =>
            string.Equals(name, "dark", StringComparison.OrdinalIgnoreCase) ? Dark() : Light();
    }
}
