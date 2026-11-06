using WorkEnc.Core;
using Xunit;

namespace WorkEnc.Tests
{
    /// <summary>视图模式 / Tab 解析测试。</summary>
    public class ViewStateTests
    {
        [Theory]
        [InlineData("mini", AppViewMode.Mini)]
        [InlineData("MINI", AppViewMode.Mini)]
        [InlineData("Mini", AppViewMode.Mini)]
        [InlineData("full", AppViewMode.Full)]
        [InlineData("FULL", AppViewMode.Full)]
        public void ParseViewMode_KnownValues(string text, AppViewMode expected)
        {
            Assert.Equal(expected, ViewState.ParseViewMode(text));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("   ")]
        [InlineData("garbage")]
        [InlineData("mini2")]
        public void ParseViewMode_IllegalFallsBackToFull(string text)
        {
            Assert.Equal(AppViewMode.Full, ViewState.ParseViewMode(text));
        }

        [Fact]
        public void ViewMode_TextRoundTrip()
        {
            foreach (var mode in new[] { AppViewMode.Full, AppViewMode.Mini })
                Assert.Equal(mode, ViewState.ParseViewMode(ViewState.ToText(mode)));
        }

        [Fact]
        public void ToText_ViewMode_Lowercase()
        {
            Assert.Equal("full", ViewState.ToText(AppViewMode.Full));
            Assert.Equal("mini", ViewState.ToText(AppViewMode.Mini));
        }

        [Theory]
        [InlineData("home", MainTab.Home)]
        [InlineData("detail", MainTab.Detail)]
        [InlineData("stats", MainTab.Stats)]
        [InlineData("settings", MainTab.Settings)]
        [InlineData("SETTINGS", MainTab.Settings)]
        public void ParseTab_KnownValues(string text, MainTab expected)
        {
            Assert.Equal(expected, ViewState.ParseTab(text));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("unknown")]
        public void ParseTab_IllegalFallsBackToHome(string text)
        {
            Assert.Equal(MainTab.Home, ViewState.ParseTab(text));
        }

        [Fact]
        public void Tab_TextRoundTrip()
        {
            foreach (var tab in new[] { MainTab.Home, MainTab.Detail, MainTab.Stats, MainTab.Settings })
                Assert.Equal(tab, ViewState.ParseTab(ViewState.ToText(tab)));
        }
    }

    /// <summary>窗口尺寸计算测试。</summary>
    public class ViewStateSizeTests
    {
        [Fact]
        public void FullWindowSize_IsDesignSizePlusMargin()
        {
            var (w, h) = ViewState.WindowSize(AppViewMode.Full);
            Assert.Equal(ViewState.DesignWidth + ViewState.EdgeMargin * 2, w);
            Assert.Equal(ViewState.FullWindowHeight(MainTab.Home), h);
        }

        [Fact]
        public void MiniWindowSize_IsDesignSizePlusMargin()
        {
            var (w, h) = ViewState.WindowSize(AppViewMode.Mini);
            Assert.Equal(ViewState.MiniDesignWidth + ViewState.EdgeMargin * 2, w);
            Assert.Equal(ViewState.MiniDesignHeight + ViewState.EdgeMargin * 2, h);
        }

        [Fact]
        public void MiniWindow_IsSmallerThanFull()
        {
            var (fw, fh) = ViewState.WindowSize(AppViewMode.Full);
            var (mw, mh) = ViewState.WindowSize(AppViewMode.Mini);
            Assert.True(mw < fw);
            Assert.True(mh < fh);
        }

        [Fact]
        public void CustomWidth_IsUsed_WhenAboveMinimum()
        {
            var (w, _) = ViewState.WindowSize(AppViewMode.Full, 560, null);
            Assert.Equal(560, w);
        }

        [Fact]
        public void CustomWidth_BelowMinimum_FallsBackToDefault()
        {
            var (w, _) = ViewState.WindowSize(AppViewMode.Full, 100, null);
            Assert.Equal(ViewState.FullWindowWidth, w);
        }

        [Fact]
        public void CustomHeight_Ignored_WhenUserNeverResized()
        {
            var (_, h) = ViewState.WindowSize(AppViewMode.Full, null, 620);
            Assert.Equal(ViewState.FullWindowHeight(MainTab.Home), h);
        }

        [Fact]
        public void CustomHeight_Used_WhenUserResized()
        {
            var (_, h) = ViewState.WindowSize(AppViewMode.Full, null, 620, MainTab.Home, userResized: true);
            Assert.Equal(620, h);
        }

        [Fact]
        public void CustomHeight_BelowMinimum_FallsBackToDefault()
        {
            var (_, h) = ViewState.WindowSize(AppViewMode.Full, null, 50, MainTab.Home, userResized: true);
            Assert.Equal(ViewState.FullWindowHeight(MainTab.Home), h);
        }

        [Fact]
        public void MiniCustomHeight_Ignored_WhenUserNeverResized()
        {
            var (_, h) = ViewState.WindowSize(AppViewMode.Mini, null, 160);
            Assert.Equal(ViewState.MiniWindowHeight, h);
        }

        [Fact]
        public void MiniCustomHeight_Used_WhenUserResized()
        {
            var (_, h) = ViewState.WindowSize(AppViewMode.Mini, null, 160, MainTab.Home, userResized: true);
            Assert.Equal(160, h);
        }

        [Fact]
        public void MinSize_Full_IsSmallerThanFullDesign()
        {
            var (w, h) = ViewState.MinSize(AppViewMode.Full);
            Assert.True(w < ViewState.FullWindowWidth);
            Assert.True(h < ViewState.FullWindowHeight(MainTab.Home));
        }

        [Fact]
        public void MinSize_Mini_IsSmallerThanMiniDesign()
        {
            var (w, h) = ViewState.MinSize(AppViewMode.Mini);
            Assert.True(w < ViewState.MiniWindowWidth);
            Assert.True(h < ViewState.MiniWindowHeight);
        }

        [Fact]
        public void MiniMinSize_AllowsSmallerThanFullMinSize()
        {
            // 迷你挂件必须比完整模式的最小尺寸还小，
            // 否则 WPF 的 MinWidth/MinHeight 会把迷你窗口往上撑，破坏设计尺寸。
            var (mw, mh) = ViewState.MinSize(AppViewMode.Mini);
            var (fw, fh) = ViewState.MinSize(AppViewMode.Full);
            Assert.True(mw < fw);
            Assert.True(mh < fh);
        }

        [Fact]
        public void FullMinSize_KeepsContentClickable()
        {
            // 窗口缩到最小时，400 宽的设计内容会被 Viewbox 缩放成 (MinWidth - 边距) / 400。
            // 低于 0.7 时标题栏那三个 26px 按钮就小到几乎点不中——v1.0.1 用户实际踩到的坑。
            double scale = (ViewState.FullMinWidth - ViewState.EdgeMargin * 2) / ViewState.DesignWidth;
            Assert.True(scale >= 0.7, $"最小宽度下的内容缩放比只有 {scale:0.00}，按钮会小到点不到");
            Assert.True(ViewState.FullMinHeight >= 220);
        }

        [Fact]
        public void DesignHeight_MatchesWindowHeightFormula()
        {
            Assert.Equal(ViewState.MiniDesignHeight, ViewState.MiniWindowHeight - ViewState.EdgeMargin * 2);
            Assert.Equal(ViewState.DesignWidth, ViewState.FullWindowWidth - ViewState.EdgeMargin * 2);
        }
    }
}
