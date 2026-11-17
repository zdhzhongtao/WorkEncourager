using System;
using MdViewer.Core;

namespace MdViewer.Tests
{
    public class AssociationTests
    {
        private const string ExePath = @"C:\Program Files\Markdown 阅读器\MdViewer.exe";

        [Fact]
        public void ProgId固定且command带引号()
        {
            Assert.Equal("MdViewer.md", FileAssociation.ProgId);
            Assert.Equal("\"" + ExePath + "\" \"%1\"", FileAssociation.CommandFor(ExePath));
            Assert.StartsWith("\"" + ExePath + "\"", FileAssociation.CommandFor(ExePath));
            Assert.Contains("\"%1\"", FileAssociation.CommandFor(ExePath));
        }

        [Fact]
        public void 三个扩展名全部登记()
        {
            var plan = FileAssociation.PlanRegister(ExePath);
            foreach (var ext in new[] { ".md", ".markdown", ".mdown" })
            {
                string key = @"Software\Classes\" + ext;
                var entry = plan.Set.Find(v => v.Key == key && v.Name == null);
                Assert.NotNull(entry);
                Assert.Equal("MdViewer.md", entry.Value);
            }
            Assert.Equal(3, FileAssociation.Extensions.Length);
        }

        [Fact]
        public void 写入的键值齐全()
        {
            var plan = FileAssociation.PlanRegister(ExePath);
            Assert.Contains(plan.Set, v => v.Key == @"Software\Classes\MdViewer.md\shell\open\command"
                && v.Value == FileAssociation.CommandFor(ExePath));
            Assert.Contains(plan.Set, v => v.Key == @"Software\Classes\MdViewer.md\DefaultIcon"
                && v.Value == "\"" + ExePath + "\",0");
            Assert.Contains(plan.Set, v => v.Key == @"Software\Classes\MdViewer.md" && v.Value == "Markdown 文档");
        }

        [Fact]
        public void 取消关联只删自己的键()
        {
            var plan = FileAssociation.PlanUnregister();
            Assert.Single(plan.DeleteKeys);
            Assert.Equal(@"Software\Classes\MdViewer.md", plan.DeleteKeys[0]);
            Assert.Empty(plan.Set);

            // 扩展名键不整棵删，只在默认值还是自己时才清
            Assert.Equal(3, plan.ConditionalClear.Count);
            Assert.Contains(@"Software\Classes\.md", plan.ConditionalClear);
            Assert.Contains(@"Software\Classes\.markdown", plan.ConditionalClear);
            Assert.Contains(@"Software\Classes\.mdown", plan.ConditionalClear);
            Assert.DoesNotContain(@"Software\Classes\.txt", plan.ConditionalClear);
        }

        [Fact]
        public void 识别是否已关联到自己()
        {
            string command = FileAssociation.CommandFor(ExePath);
            Assert.True(FileAssociation.IsOurCommand(command, ExePath));
            Assert.False(FileAssociation.IsOurCommand(command, @"C:\other\MdViewer.exe"));
            Assert.False(FileAssociation.IsOurCommand(null, ExePath));
            Assert.False(FileAssociation.IsOurCommand("", ExePath));
        }

        [Fact]
        public void 扩展名键补点()
        {
            Assert.Equal(@"Software\Classes\.md", FileAssociation.ExtensionKey("md"));
            Assert.Equal(@"Software\Classes\.md", FileAssociation.ExtensionKey(".md"));
        }

        [Fact]
        public void 提示文案讲清不能被静默抢占()
        {
            Assert.Contains("打开方式", FileAssociation.UserHint);
            Assert.Contains("始终使用", FileAssociation.UserHint);
        }
    }
}
