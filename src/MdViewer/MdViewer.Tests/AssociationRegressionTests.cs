using System;
using System.Collections.Generic;
using System.Linq;
using MdViewer.Core;

namespace MdViewer.Tests
{
    /// <summary>关联计划：不能悄悄抢别人已经占着的默认程序。</summary>
    public class AssociationRegressionTests
    {
        [Fact]
        public void 扩展名被别人占着时只登记候选_不改默认值()
        {
            var existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [".md"] = "VSCode.md",
                [".markdown"] = null,
                [".mdown"] = "MdViewer.md"
            };

            var plan = FileAssociation.PlanRegister(@"C:\Tools\MdViewer.exe", existing);

            var mdDefault = plan.Set.FirstOrDefault(v => v.Key == FileAssociation.ExtensionKey(".md") && v.Name == null);
            Assert.Null(mdDefault);                       // .md 不动

            Assert.Contains(plan.Set, v => v.Key == FileAssociation.ExtensionKey(".md") + @"\OpenWithProgids"
                                        && v.Name == FileAssociation.ProgId);

            Assert.Single(plan.SkippedDefaults);
            Assert.Contains(".md", plan.SkippedDefaults[0]);

            // 没被占的、以及本来就是自己的，照常写默认值
            Assert.Contains(plan.Set, v => v.Key == FileAssociation.ExtensionKey(".markdown") && v.Name == null);
            Assert.Contains(plan.Set, v => v.Key == FileAssociation.ExtensionKey(".mdown") && v.Name == null);
        }

        [Fact]
        public void 不传现有值的时候保持老行为_三个扩展名都写默认值()
        {
            var plan = FileAssociation.PlanRegister(@"C:\Tools\MdViewer.exe");

            foreach (var ext in FileAssociation.Extensions)
            {
                Assert.Contains(plan.Set, v => v.Key == FileAssociation.ExtensionKey(ext) && v.Name == null);
            }
            Assert.Empty(plan.SkippedDefaults);
        }

        [Fact]
        public void 取消关联不动别人的键()
        {
            var plan = FileAssociation.PlanUnregister();

            Assert.Contains(FileAssociation.ProgIdKey, plan.DeleteKeys);
            Assert.Equal(FileAssociation.Extensions.Length, plan.ConditionalClear.Count);
        }
    }
}
