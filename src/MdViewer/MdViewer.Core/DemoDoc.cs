using System;
using System.IO;

namespace MdViewer.Core
{
    /// <summary>
    /// `MdViewer.exe --demo` 用的样例文档：标题、表格、代码块、任务列表、图片、脚注都有，
    /// 方便一条条对照验收清单。文件写到 %APPDATA%\MdViewer\demo.md，不写用户目录。
    /// </summary>
    public static class DemoDoc
    {
        public const string DemoFileName = "demo.md";
        public const string ImageFolderName = "images";
        public const string ImageFileName = "demo.svg";

        public static string MarkdownText()
        {
            return string.Join("\r\n", new[]
            {
                "# Markdown 阅读器 样例文档",
                "",
                "这是 `--demo` 生成的样例，用来逐条核对验收清单。正文里有 **加粗**、*斜体*、~~删除线~~、",
                "行内代码 `Console.WriteLine(1)`，还有中文、English 和 emoji 😀🎉。",
                "",
                "> 引用块：左边一条竖线，浅底色。",
                "> 第二行也属于同一个引用。",
                "",
                "## 表格",
                "",
                "| 工具 | 形态 | 状态 | 说明 |",
                "|:-----|:----:|-----:|:-----|",
                "| 表格比对器 | WPF | 完成 | 比对两个 xlsx / csv |",
                "| 表格拆合器 | WPF | 完成 | 按列值拆分、合并多文件 |",
                "| 财务小算盘 | WPF | 完成 | 大写金额、工作日、含税换算 |",
                "| Markdown 阅读器 | WPF | 完成 | 就是你现在看到的这个 |",
                "",
                "## 宽表格",
                "",
                "这张表 12 列：窄栏下每列会被压得很窄，点工具栏的「宽屏」（Ctrl+Shift+W）",
                "让正文铺满窗口，表格就能左右滚动着看，不再挤成一列一个字。",
                "",
                "| 月份 | 期初 | 借方 | 贷方 | 期末 | 差异 | 币种 | 利润中心 | 部门 | 凭证号 | 状态 | 备注 |",
                "|:-----|-----:|-----:|-----:|-----:|-----:|:----:|:---------|:-----|:-------|:-----|:-----|",
                "| 2026-01 | 120,400.00 | 38,210.00 | 41,000.00 | 117,610.00 | -120.00 | CNY | PC-1001 | 财务 | JE-0001 | 已过账 | 正常 |",
                "| 2026-02 | 117,610.00 | 52,930.00 | 40,120.00 | 130,420.00 | 0.00 | CNY | PC-1002 | 采购 | JE-0002 | 已过账 | 正常 |",
                "| 2026-03 | 130,420.00 | 44,180.00 | 61,770.00 | 112,830.00 | 35.50 | CNY | PC-1001 | 销售 | JE-0003 | 待复核 | 有调整 |",
                "",
                "## 代码块",
                "",
                "```csharp",
                "public static string Hello(string name)",
                "{",
                "    return $\"你好，{name}\";",
                "}",
                "```",
                "",
                "```",
                "没有语言标记的代码块也要能看。",
                "```",
                "",
                "## 任务列表",
                "",
                "- [x] 双击 .md 能直接打开",
                "- [x] 目录侧栏能跳转",
                "- [ ] 搜索命中计数",
                "- [ ] 导出 PDF",
                "",
                "## 列表",
                "",
                "1. 有序列表第一项",
                "2. 第二项",
                "   - 嵌套无序",
                "   - 再来一条",
                "3. 第三项",
                "",
                "### 三级标题",
                "",
                "#### 四级标题（目录里不显示）",
                "",
                "## 图片",
                "",
                "下面这张图走相对路径 `./images/demo.svg`，能显示说明相对路径映射没问题。",
                "",
                "![样例图片](./images/demo.svg)",
                "",
                "再试一张不存在的图，应该显示占位框而不是空白：",
                "",
                "![不存在的图片](./images/not-exists.png)",
                "",
                "## 链接",
                "",
                "- 自动链接：https://example.com",
                "- 行内链接：[Markdown 语法](https://commonmark.org/)",
                "- 锚点链接：[跳到表格](#表格)",
                "",
                "## 脚注",
                "",
                "这里有一个脚注引用[^1]，还有第二个[^note]。",
                "",
                "[^1]: 脚注内容：纯本地渲染，不联网。",
                "[^note]: 第二个脚注，用来验证多条脚注的编号。",
                "",
                "---",
                "",
                "（样例结束）",
                ""
            });
        }

        public static string SvgText()
        {
            return string.Join("\r\n", new[]
            {
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"480\" height=\"120\" viewBox=\"0 0 480 120\">",
                "  <rect x=\"0\" y=\"0\" width=\"480\" height=\"120\" rx=\"12\" fill=\"#2563EB\" />",
                "  <text x=\"240\" y=\"68\" font-family=\"Microsoft YaHei UI, sans-serif\" font-size=\"22\"",
                "        fill=\"#FFFFFF\" text-anchor=\"middle\">相对路径图片加载成功</text>",
                "</svg>",
                ""
            });
        }

        /// <summary>在 dataDir 下生成 demo.md 与 images/demo.svg，返回 demo.md 的完整路径。</summary>
        public static string Ensure(string dataDir)
        {
            if (string.IsNullOrWhiteSpace(dataDir)) throw new ArgumentException("数据目录不能为空", "dataDir");

            Directory.CreateDirectory(dataDir);
            string imageDir = Path.Combine(dataDir, ImageFolderName);
            Directory.CreateDirectory(imageDir);

            string imagePath = Path.Combine(imageDir, ImageFileName);
            AtomicFile.WriteAllText(imagePath, SvgText());

            string docPath = Path.Combine(dataDir, DemoFileName);
            AtomicFile.WriteAllText(docPath, MarkdownText());
            return docPath;
        }
    }
}
