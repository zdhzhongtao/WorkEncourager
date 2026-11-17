using System;
using MdViewer.Core;

namespace MdViewer.Tests
{
    public class CommandLineTests
    {
        [Fact]
        public void 带空格的路径原样保留()
        {
            var options = CommandLine.Parse(new[] { @"C:\我的 文档\说明 文件.md" });
            Assert.True(options.HasFile);
            Assert.Equal(@"C:\我的 文档\说明 文件.md", options.FilePath);
            Assert.Null(options.Error);
        }

        [Fact]
        public void 参数还带引号时被削掉()
        {
            var options = CommandLine.Parse(new[] { "\"C:\\a b\\c.md\"" });
            Assert.Equal(@"C:\a b\c.md", options.FilePath);

            var half = CommandLine.Parse(new[] { "\"C:\\a b\\c.md" });
            Assert.Equal(@"C:\a b\c.md", half.FilePath);
        }

        [Fact]
        public void 无参数时什么都不做()
        {
            var options = CommandLine.Parse(new string[0]);
            Assert.False(options.HasFile);
            Assert.False(options.Demo);
            Assert.False(options.Help);

            var nullArgs = CommandLine.Parse(null);
            Assert.False(nullArgs.HasFile);
        }

        [Fact]
        public void demo参数被识别()
        {
            var options = CommandLine.Parse(new[] { "--demo" });
            Assert.True(options.Demo);
            Assert.False(options.HasFile);
        }

        [Fact]
        public void 文件与demo可以同时出现()
        {
            var options = CommandLine.Parse(new[] { "--demo", @"C:\a.md" });
            Assert.True(options.Demo);
            Assert.Equal(@"C:\a.md", options.FilePath);
        }

        [Fact]
        public void 不认识的参数给出提示()
        {
            var options = CommandLine.Parse(new[] { "--nonsense" });
            Assert.NotNull(options.Error);
            Assert.Contains("--nonsense", options.Error);
        }

        [Fact]
        public void 多个文件只取第一个()
        {
            var options = CommandLine.Parse(new[] { @"C:\a.md", @"C:\b.md" });
            Assert.Equal(@"C:\a.md", options.FilePath);
        }

        [Fact]
        public void snapshot参数吃掉后面那个路径()
        {
            var options = CommandLine.Parse(new[] { "--snapshot", @"C:\out\界面 截图.png" });
            Assert.True(options.IsSnapshot);
            Assert.Equal(@"C:\out\界面 截图.png", options.SnapshotPath);
            Assert.False(options.HasFile);      // 截图路径不能被当成要打开的文件
            Assert.Null(options.Error);
        }

        [Fact]
        public void snapshot可以和文件一起用()
        {
            var options = CommandLine.Parse(new[] { "--snapshot", @"C:\out.png", @"C:\a.md" });
            Assert.Equal(@"C:\out.png", options.SnapshotPath);
            Assert.Equal(@"C:\a.md", options.FilePath);
        }

        [Fact]
        public void snapshot缺参数时不报未知参数()
        {
            var options = CommandLine.Parse(new[] { "--snapshot" });
            Assert.False(options.IsSnapshot);
            Assert.Null(options.Error);        // 是漏参数，不是不认识这个参数
        }

        [Fact]
        public void make_shortcut参数被识别()
        {
            var options = CommandLine.Parse(new[] { "--make-shortcut", @"C:\Users\a\Start Menu\Markdown 阅读器.lnk" });
            Assert.True(options.IsMakeShortcut);
            Assert.Equal(@"C:\Users\a\Start Menu\Markdown 阅读器.lnk", options.ShortcutPath);
            Assert.False(options.HasFile);
            Assert.Null(options.Error);
        }

        [Fact]
        public void 带路径的开关不会误占文件位置()
        {
            // --make-shortcut 的值不能被当成"要打开的文件"
            var options = CommandLine.Parse(new[] { "--make-shortcut", @"C:\x.lnk", @"C:\doc.md" });
            Assert.Equal(@"C:\x.lnk", options.ShortcutPath);
            Assert.Equal(@"C:\doc.md", options.FilePath);
        }
    }
}
