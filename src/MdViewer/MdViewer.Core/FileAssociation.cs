using System;
using System.Collections.Generic;

namespace MdViewer.Core
{
    /// <summary>一条要写进注册表的键值。Name 为空表示键的默认值。</summary>
    public sealed class AssocValue
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Value { get; set; }

        public override string ToString()
        {
            return Key + "\\" + (string.IsNullOrEmpty(Name) ? "(默认)" : Name) + " = " + Value;
        }
    }

    /// <summary>关联 / 取消关联要做的事，纯数据，不碰注册表（真正写注册表在主程序里）。</summary>
    public sealed class AssocPlan
    {
        /// <summary>要写入的键值。</summary>
        public List<AssocValue> Set { get; set; } = new List<AssocValue>();

        /// <summary>取消关联时整棵删除的键（只包含自己的 ProgId 子树）。</summary>
        public List<string> DeleteKeys { get; set; } = new List<string>();

        /// <summary>取消关联时要检查的扩展名键：默认值还是自己的 ProgId 才清掉，绝不动别人的。</summary>
        public List<string> ConditionalClear { get; set; } = new List<string>();

        /// <summary>因为已被别的程序占用而没有去抢的扩展名（界面上要如实告诉用户）。</summary>
        public List<string> SkippedDefaults { get; set; } = new List<string>();
    }

    /// <summary>
    /// 只写 HKCU\Software\Classes，不需要管理员。
    /// Windows 10/11 不允许程序静默抢默认程序，这里只负责"注册成候选"，用户还要在「打开方式」里勾一次。
    /// </summary>
    public static class FileAssociation
    {
        public const string ProgId = "MdViewer.md";
        public const string FriendlyName = "Markdown 文档";
        public const string ContentType = "text/markdown";

        /// <summary>要接管的扩展名（写入时全部注册）。</summary>
        public static readonly string[] Extensions = { ".md", ".markdown", ".mdown" };

        public static string ProgIdKey => @"Software\Classes\" + ProgId;

        public static string ExtensionKey(string extension)
        {
            string ext = extension ?? string.Empty;
            if (!ext.StartsWith(".", StringComparison.Ordinal)) ext = "." + ext;
            return @"Software\Classes\" + ext;
        }

        /// <summary>打开命令："C:\path\with space\MdViewer.exe" "%1"</summary>
        public static string CommandFor(string exePath)
        {
            return "\"" + (exePath ?? string.Empty) + "\" \"%1\"";
        }

        /// <summary>图标："C:\path\MdViewer.exe",0</summary>
        public static string IconFor(string exePath)
        {
            return "\"" + (exePath ?? string.Empty) + "\",0";
        }

        /// <summary>
        /// 生成注册计划。existingDefaults 传每个扩展名当前 HKCU 下的默认 ProgId：
        /// 如果已经指向别的程序，就只登记「用这个程序打开」的候选，不硬抢默认值。
        /// </summary>
        public static AssocPlan PlanRegister(string exePath, IDictionary<string, string> existingDefaults = null)
        {
            var plan = new AssocPlan();

            plan.Set.Add(new AssocValue { Key = ProgIdKey, Name = null, Value = FriendlyName });
            plan.Set.Add(new AssocValue { Key = ProgIdKey + @"\DefaultIcon", Name = null, Value = IconFor(exePath) });
            plan.Set.Add(new AssocValue { Key = ProgIdKey + @"\shell\open\command", Name = null, Value = CommandFor(exePath) });

            foreach (var ext in Extensions)
            {
                string key = ExtensionKey(ext);

                string existing = null;
                if (existingDefaults != null) existingDefaults.TryGetValue(ext, out existing);

                bool canTakeDefault = string.IsNullOrWhiteSpace(existing)
                                      || string.Equals(existing, ProgId, StringComparison.OrdinalIgnoreCase);
                if (canTakeDefault)
                {
                    plan.Set.Add(new AssocValue { Key = key, Name = null, Value = ProgId });
                }
                else
                {
                    plan.SkippedDefaults.Add(ext + " 现在指向 " + existing);
                }

                plan.Set.Add(new AssocValue { Key = key, Name = "Content Type", Value = ContentType });
                plan.Set.Add(new AssocValue { Key = key, Name = "PerceivedType", Value = "text" });
                plan.Set.Add(new AssocValue { Key = key + @"\OpenWithProgids", Name = ProgId, Value = string.Empty });
            }

            plan.DeleteKeys.Add(ProgIdKey);
            plan.ConditionalClear.AddRange(ExtensionsToKeys());
            return plan;
        }

        public static AssocPlan PlanUnregister()
        {
            var plan = new AssocPlan();
            plan.DeleteKeys.Add(ProgIdKey);
            plan.ConditionalClear.AddRange(ExtensionsToKeys());
            return plan;
        }

        private static IEnumerable<string> ExtensionsToKeys()
        {
            foreach (var ext in Extensions) yield return ExtensionKey(ext);
        }

        /// <summary>注册表里 command 的值是不是指向当前这个 exe（判断"已关联"用）。</summary>
        public static bool IsOurCommand(string commandValue, string exePath)
        {
            if (string.IsNullOrWhiteSpace(commandValue) || string.IsNullOrWhiteSpace(exePath)) return false;
            return commandValue.Trim().StartsWith("\"" + exePath.Trim() + "\"", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>一句人话，界面上必须显示（规范 8.7）。</summary>
        public const string UserHint =
            "Windows 10/11 不允许程序静默抢默认程序。注册后若双击 .md 还是被别的程序打开，" +
            "请右键文件 →「打开方式」→「选择其他应用」→ 勾选「始终使用此应用」→ 选「Markdown 阅读器」。";
    }
}
