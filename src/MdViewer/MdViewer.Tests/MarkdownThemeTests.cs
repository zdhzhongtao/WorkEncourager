using System;
using System.Collections.Generic;
using MdViewer.Core;

namespace MdViewer.Tests
{
    public class MarkdownThemeTests
    {
        [Fact]
        public void 至少十套主题且id唯一()
        {
            Assert.True(MarkdownThemes.All.Count >= 10, "内置主题少于 10 套");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var theme in MarkdownThemes.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(theme.Id));
                Assert.False(string.IsNullOrWhiteSpace(theme.Name));
                Assert.False(string.IsNullOrWhiteSpace(theme.Note));
                Assert.True(seen.Add(theme.Id), "主题 id 重复：" + theme.Id);
            }
        }

        [Fact]
        public void 每套主题的颜色都是合法六位十六进制()
        {
            foreach (var theme in MarkdownThemes.All)
            {
                Assert.Matches("^#[0-9A-Fa-f]{6}$", theme.Accent);
                Assert.Matches("^#[0-9A-Fa-f]{6}$", theme.AccentHover);
                Assert.Matches("^#[0-9A-Fa-f]{6}$", theme.SwatchBg);
                Assert.Matches("^#[0-9A-Fa-f]{6}$", theme.SwatchFg);
            }
        }

        [Fact]
        public void 浅色组与深色组都不为空且默认主题对得上()
        {
            Assert.NotEmpty(MarkdownThemes.OfMode(false));
            Assert.NotEmpty(MarkdownThemes.OfMode(true));
            Assert.False(MarkdownThemes.DefaultLight.IsDark);
            Assert.True(MarkdownThemes.DefaultDark.IsDark);
        }

        [Fact]
        public void 认不出来的id回落而不是抛()
        {
            Assert.Null(MarkdownThemes.Resolve("nope"));
            Assert.Null(MarkdownThemes.Resolve(null));
            Assert.Equal(MarkdownThemes.DefaultLightId, MarkdownThemes.ResolveOrFallback("nope", false).Id);
            Assert.Equal(MarkdownThemes.DefaultDarkId, MarkdownThemes.ResolveOrFallback(null, true).Id);
        }

        [Fact]
        public void 明暗写反的id也回落()
        {
            // 手改配置把深色主题塞进浅色槽位，必须回落到浅色默认，不能出来「深底配深壳」
            Assert.Equal(MarkdownThemes.DefaultLightId, MarkdownThemes.ResolveOrFallback("nord", false).Id);
        }

        [Fact]
        public void 明暗切换优先找同名家族()
        {
            Assert.Equal("github-dark", MarkdownThemes.FamilyFallback("github-light", true));
            Assert.Equal("github-light", MarkdownThemes.FamilyFallback("github-dark", false));
            Assert.Equal("solarized-dark", MarkdownThemes.FamilyFallback("solarized-light", true));

            // 没有配对的主题回落到该组默认
            Assert.Equal(MarkdownThemes.DefaultDarkId, MarkdownThemes.FamilyFallback("sepia", true));
        }

        [Fact]
        public void 界面配色跟着主题明暗与主色走()
        {
            Theme light = Theme.ForMarkdownTheme(MarkdownThemes.Resolve("sepia"));
            Assert.Equal("sepia", light.Name);
            Assert.Equal("#B45309", light.Accent);
            Assert.NotEqual("#1E293B", light.WindowBg);   // 浅色骨架

            Theme dark = Theme.ForMarkdownTheme(MarkdownThemes.Resolve("nord"));
            Assert.Equal("#88C0D0", dark.Accent);
            Assert.Equal("#2E3440", dark.WindowBg);       // 0.5.0 起壳色直接取主题自己的底色
            Assert.NotEqual("#FFFFFF", dark.OnAccent);    // 浅主色上要配深字，否则看不清
        }

        [Fact]
        public void 不同深色主题的壳色不再共用一套骨架()
        {
            // 0.4.x 所有深色主题都派生自 #1E293B 那套骨架，切主题只是换主色。
            // 0.5.0 要的就是「整个壳跟着主题走」，所以两套深色主题的窗口底色必须不同。
            Theme tokyo = Theme.ForMarkdownTheme(MarkdownThemes.Resolve("tokyo-night"));
            Theme mocha = Theme.ForMarkdownTheme(MarkdownThemes.Resolve("catppuccin-mocha"));

            Assert.NotEqual(tokyo.WindowBg, mocha.WindowBg);
            Assert.NotEqual(tokyo.NavBg, mocha.NavBg);
            Assert.NotEqual(tokyo.CardBg, mocha.CardBg);
        }

        [Fact]
        public void 壳色能派生出一整套可读的界面色()
        {
            foreach (var markdown in MarkdownThemes.All)
            {
                Theme theme = Theme.ForMarkdownTheme(markdown);

                Assert.Matches("^#[0-9A-Fa-f]{6}$", theme.WindowBg);
                Assert.Matches("^#[0-9A-Fa-f]{6}$", theme.TextPrimary);
                Assert.Matches("^#[0-9A-Fa-f]{6}$", theme.TextSecondary);
                Assert.Matches("^#[0-9A-Fa-f]{6}$", theme.TextMuted);

                Assert.NotEqual(theme.WindowBg, theme.TextPrimary);
                Assert.NotEqual(theme.CardBg, theme.TextPrimary);
                Assert.NotEqual(theme.WindowBg, theme.CardBg);
            }
        }

        [Fact]
        public void 界面配色覆盖了主色相关的派生色()
        {
            Theme theme = Theme.ForMarkdownTheme(MarkdownThemes.Resolve("dracula"));
            Assert.Equal("#BD93F9", theme.NavActiveFg);
            Assert.NotEqual(theme.CardBg, theme.NavActiveBg);
            Assert.NotEqual(theme.CardBg, theme.SelectionBg);
        }
    }
}
