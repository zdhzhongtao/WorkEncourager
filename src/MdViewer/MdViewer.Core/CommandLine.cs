using System;
using System.Collections.Generic;

namespace MdViewer.Core
{
    /// <summary>命令行解析结果。</summary>
    public sealed class CommandLineOptions
    {
        /// <summary>要打开的 md 绝对/相对路径；没给就是 null。</summary>
        public string FilePath { get; set; }

        /// <summary>--demo：在数据目录生成样例文件后退出。</summary>
        public bool Demo { get; set; }

        /// <summary>--help / -h / /?</summary>
        public bool Help { get; set; }

        /// <summary>--snapshot &lt;png&gt;：渲染窗口后把界面存成图片，用于自检（不出界面给人看）。</summary>
        public string SnapshotPath { get; set; }

        /// <summary>--make-shortcut &lt;lnk&gt;：在指定位置建一个指向本程序的快捷方式。</summary>
        public string ShortcutPath { get; set; }

        public bool IsSnapshot
        {
            get { return !string.IsNullOrWhiteSpace(SnapshotPath); }
        }

        public bool IsMakeShortcut
        {
            get { return !string.IsNullOrWhiteSpace(ShortcutPath); }
        }

        /// <summary>参数不认识时的人话提示，没错误就是 null。</summary>
        public string Error { get; set; }

        public bool HasFile
        {
            get { return !string.IsNullOrWhiteSpace(FilePath); }
        }
    }

    /// <summary>
    /// 极简命令行：`MdViewer.exe "C:\a b\c.md"`、`MdViewer.exe --demo`。
    /// Windows 传进来的参数一般已经去掉了引号，但拖放/手工拼命令行时可能还带着，这里统一再削一次。
    /// </summary>
    public static class CommandLine
    {
        public static CommandLineOptions Parse(string[] args)
        {
            var options = new CommandLineOptions();
            if (args == null || args.Length == 0) return options;

            var unknown = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == null) continue;
                string arg = Unquote(args[i]);
                if (arg.Length == 0) continue;

                if (arg.StartsWith("--", StringComparison.Ordinal) || arg.StartsWith("/", StringComparison.Ordinal))
                {
                    string name = arg.TrimStart('-', '/').ToLowerInvariant();
                    if (name == "demo") { options.Demo = true; continue; }
                    if (name == "help" || name == "h" || name == "?" || name == "？") { options.Help = true; continue; }
                    if (name == "snapshot")
                    {
                        // 后面那个参数是输出图片路径
                        if (i + 1 < args.Length) options.SnapshotPath = Unquote(args[++i]);
                        continue;
                    }
                    if (name == "make-shortcut")
                    {
                        if (i + 1 < args.Length) options.ShortcutPath = Unquote(args[++i]);
                        continue;
                    }
                    unknown.Add(arg);
                    continue;
                }

                if (!options.HasFile) options.FilePath = arg;
            }

            if (unknown.Count > 0 && !options.HasFile && !options.Demo && !options.Help
                && !options.IsSnapshot && !options.IsMakeShortcut)
                options.Error = "不认识的参数：" + string.Join(" ", unknown.ToArray());

            return options;
        }

        /// <summary>去掉首尾空格与成对（或半个）引号。</summary>
        public static string Unquote(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string text = value.Trim();
            if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
                text = text.Substring(1, text.Length - 2);
            else if (text.Length >= 1 && text[0] == '"')
                text = text.Substring(1);
            if (text.Length >= 1 && text[text.Length - 1] == '"')
                text = text.Substring(0, text.Length - 1);
            return text.Trim();
        }
    }
}
