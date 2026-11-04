using System;

namespace WorkEnc.Core
{
    /// <summary>窗口视图模式。</summary>
    public enum AppViewMode
    {
        /// <summary>完整模式：多 Tab 展示全部功能。</summary>
        Full = 0,
        /// <summary>迷你模式：钉在桌面的小挂件，只看金额在涨。</summary>
        Mini = 1
    }

    /// <summary>完整模式下的主 Tab。</summary>
    public enum MainTab
    {
        /// <summary>主页：核心数字在涨。</summary>
        Home = 0,
        /// <summary>明细：速度、本月累计、时薪等次要数据。</summary>
        Detail = 1,
        /// <summary>统计：周/月/年历史。</summary>
        Stats = 2,
        /// <summary>设置。</summary>
        Settings = 3
    }

    /// <summary>
    /// 视图模式与尺寸的纯逻辑定义。抽到 Core 是为了能写单元测试，
    /// UI 层只负责把这里的数值搬到窗口上。
    /// </summary>
    public static class ViewState
    {
        /// <summary>内容设计宽度（不含窗口四周留白）。</summary>
        public const double DesignWidth = 400;

        /// <summary>窗口四周留白，给自绘的缩放热区腾位置（与 XAML 的 RootGrid Margin 一致）。</summary>
        public const double EdgeMargin = 16;

        /// <summary>迷你模式的内容设计宽度。</summary>
        public const double MiniDesignWidth = 220;

        /// <summary>
        /// 迷你模式的内容设计高度。必须与 MainWindow.xaml 里 MiniCanvas.Height 一致，
        /// 否则 Viewbox 会把内容压缩或拉伸，迷你挂件看起来比设计稿小一号。
        /// 80 = 状态行 20 + 金额行 32 + 进度条 10 + 上下留白 16。
        /// </summary>
        public const double MiniDesignHeight = 80;

        /// <summary>完整模式允许缩到的最小宽度。</summary>
        public const double FullMinWidth = 260;

        /// <summary>完整模式允许缩到的最小高度。</summary>
        public const double FullMinHeight = 130;

        /// <summary>迷你挂件允许缩到的最小宽度。</summary>
        public const double MiniMinWidth = 180;

        /// <summary>迷你挂件允许缩到的最小高度。</summary>
        public const double MiniMinHeight = 70;

        /// <summary>完整模式默认窗口宽度。</summary>
        public static double FullWindowWidth => DesignWidth + EdgeMargin * 2;

        /// <summary>完整模式默认窗口高度（内容高度由布局实测，这里给个保底值）。</summary>
        public static double FullWindowHeight(MainTab tab) => 356 + EdgeMargin * 2;

        /// <summary>迷你模式默认窗口宽度。</summary>
        public static double MiniWindowWidth => MiniDesignWidth + EdgeMargin * 2;

        /// <summary>迷你模式默认窗口高度。</summary>
        public static double MiniWindowHeight => MiniDesignHeight + EdgeMargin * 2;

        /// <summary>
        /// 计算目标窗口尺寸。
        ///
        /// 宽度：只要存过且不小于该模式的最小宽度，就沿用用户拖出来的值。
        /// 高度：只有用户真拖过边框（userResized）才沿用自定义高度，
        /// 否则用设计高度——完整模式的高度随后还会被内容实测校正（见 FitCanvasHeight）。
        /// </summary>
        public static (double W, double H) WindowSize(AppViewMode mode,
                                                     double? customW = null,
                                                     double? customH = null,
                                                     MainTab tab = MainTab.Home,
                                                     bool userResized = false)
        {
            bool mini = mode == AppViewMode.Mini;

            double defW = mini ? MiniWindowWidth : FullWindowWidth;
            double defH = mini ? MiniWindowHeight : FullWindowHeight(tab);
            double minW = mini ? MiniMinWidth : FullMinWidth;
            double minH = mini ? MiniMinHeight : FullMinHeight;

            double w = customW.HasValue && customW.Value >= minW ? customW.Value : defW;
            double h = userResized && customH.HasValue && customH.Value >= minH
                ? customH.Value : defH;
            return (w, h);
        }

        /// <summary>该模式下允许的最小窗口尺寸（用来设置 WPF 的 MinWidth/MinHeight）。</summary>
        public static (double W, double H) MinSize(AppViewMode mode) =>
            mode == AppViewMode.Mini ? (MiniMinWidth, MiniMinHeight) : (FullMinWidth, FullMinHeight);

        /// <summary>解析视图模式字符串，非法值回落到 Full。</summary>
        public static AppViewMode ParseViewMode(string s) =>
            string.Equals(s, "mini", StringComparison.OrdinalIgnoreCase)
                ? AppViewMode.Mini
                : AppViewMode.Full;

        /// <summary>视图模式转字符串，用于持久化。</summary>
        public static string ToText(AppViewMode mode) =>
            mode == AppViewMode.Mini ? "mini" : "full";

        /// <summary>解析 Tab 名称，非法值回落到 Home。</summary>
        public static MainTab ParseTab(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "detail": return MainTab.Detail;
                case "stats": return MainTab.Stats;
                case "settings": return MainTab.Settings;
                default: return MainTab.Home;
            }
        }

        /// <summary>Tab 转字符串，用于持久化。</summary>
        public static string ToText(MainTab tab) => tab switch
        {
            MainTab.Detail => "detail",
            MainTab.Stats => "stats",
            MainTab.Settings => "settings",
            _ => "home"
        };

        /// <summary>下一个 Tab（循环），用于键盘快捷键。</summary>
        public static MainTab NextTab(MainTab tab) => (MainTab)(((int)tab + 1) % 4);

        /// <summary>Tab 的中文标题。</summary>
        public static string TabTitle(MainTab tab) => tab switch
        {
            MainTab.Detail => "明细",
            MainTab.Stats => "统计",
            MainTab.Settings => "设置",
            _ => "主页"
        };
    }
}
