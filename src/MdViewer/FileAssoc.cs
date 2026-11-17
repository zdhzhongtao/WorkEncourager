using System;
using System.Runtime.InteropServices;
using MdViewer.Core;
using Microsoft.Win32;

namespace MdViewer
{
    /// <summary>
    /// 真正写注册表的地方：只写 HKCU\Software\Classes，不需要管理员。
    /// 键值清单由 Core 的 FileAssociation 给，这里只负责落地。
    /// </summary>
    internal static class FileAssoc
    {
        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST = 0x0000;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        /// <summary>当前用户下的 .md 是不是已经指向本程序。</summary>
        public static bool IsAssociated(string exePath)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(FileAssociation.ExtensionKey(".md")))
                {
                    if (key == null) return false;
                    string value = key.GetValue(null) as string;
                    if (!string.Equals(value, FileAssociation.ProgId, StringComparison.OrdinalIgnoreCase)) return false;
                }
                using (var key = Registry.CurrentUser.OpenSubKey(FileAssociation.ProgIdKey + @"\shell\open\command"))
                {
                    if (key == null) return false;
                    return FileAssociation.IsOurCommand(key.GetValue(null) as string, exePath);
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取关联状态失败", ex);
                return false;
            }
        }

        /// <summary>
        /// 注册到 HKCU。已经指向别的程序的扩展名只登记成候选，不硬抢默认值 —— 抢了也会被
        /// Windows 的 UserChoice 机制纠正回来，白白弄脏别人的设置。
        /// </summary>
        public static AssocPlan Register(string exePath)
        {
            var existing = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ext in FileAssociation.Extensions)
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(FileAssociation.ExtensionKey(ext)))
                    {
                        existing[ext] = key == null ? null : key.GetValue(null) as string;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("读取扩展名默认值失败：" + ext, ex);
                }
            }

            var plan = FileAssociation.PlanRegister(exePath, existing);
            foreach (var entry in plan.Set)
            {
                using (var key = Registry.CurrentUser.CreateSubKey(entry.Key))
                {
                    if (key == null) continue;
                    key.SetValue(entry.Name ?? string.Empty, entry.Value ?? string.Empty, RegistryValueKind.String);
                }
            }
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            return plan;
        }

        /// <summary>只删自己写的东西：自己的 ProgId 整棵删，扩展名只在自己还指向本程序时清掉。</summary>
        public static void Unregister()
        {
            var plan = FileAssociation.PlanUnregister();

            foreach (var keyPath in plan.DeleteKeys)
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(keyPath, false); }
                catch (Exception ex) { Log.Error("删除关联键失败：" + keyPath, ex); }
            }

            foreach (var keyPath in plan.ConditionalClear)
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(keyPath, true))
                    {
                        if (key == null) continue;
                        string value = key.GetValue(null) as string;
                        if (!string.Equals(value, FileAssociation.ProgId, StringComparison.OrdinalIgnoreCase)) continue;

                        key.DeleteValue(string.Empty, false);
                        key.DeleteValue("Content Type", false);
                        key.DeleteValue("PerceivedType", false);
                    }
                    using (var openWith = Registry.CurrentUser.OpenSubKey(keyPath + @"\OpenWithProgids", true))
                    {
                        if (openWith != null) openWith.DeleteValue(FileAssociation.ProgId, false);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("清理扩展名键失败：" + keyPath, ex);
                }
            }

            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
