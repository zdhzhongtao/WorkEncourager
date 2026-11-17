using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MdViewer
{
    /// <summary>
    /// 创建 .lnk 快捷方式。
    ///
    /// 走 IShellLink COM 接口，而不是 WScript.Shell：后者在开了「UTF-8 全局代码页」的机器上
    /// 会把中文文件名写坏（名字里出现 ???，Save 直接失败），而且 PowerShell 里实例化 COM
    /// 还会被一些安全策略拦掉。这里全是 P/Invoke，行为稳定。
    /// </summary>
    internal static class Shortcuts
    {
        /// <summary>在 lnkPath 处建一个指向 targetPath 的快捷方式，父目录不存在会自动创建。</summary>
        public static void Create(string lnkPath, string targetPath, string workingDir, string description)
        {
            if (string.IsNullOrWhiteSpace(lnkPath)) throw new ArgumentException("快捷方式路径不能为空", "lnkPath");
            if (string.IsNullOrWhiteSpace(targetPath)) throw new ArgumentException("目标路径不能为空", "targetPath");

            string directory = Path.GetDirectoryName(lnkPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            object comObject = new ShellLinkCoClass();
            try
            {
                var link = (IShellLinkW)comObject;
                link.SetPath(targetPath);
                if (!string.IsNullOrWhiteSpace(workingDir)) link.SetWorkingDirectory(workingDir);
                if (!string.IsNullOrWhiteSpace(description)) link.SetDescription(description);
                link.SetIconLocation(targetPath, 0);

                var file = (IPersistFile)comObject;
                int hr = file.Save(lnkPath, true);
                if (hr != 0) throw new COMException("保存快捷方式失败（HRESULT 0x" + hr.ToString("X8") + "）：" + lnkPath, hr);
            }
            finally
            {
                if (Marshal.IsComObject(comObject)) Marshal.ReleaseComObject(comObject);
            }
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLinkCoClass
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
            [PreserveSig] int GetIDList(out IntPtr ppidl);
            [PreserveSig] int SetIDList(IntPtr pidl);
            [PreserveSig] int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            [PreserveSig] int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            [PreserveSig] int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            [PreserveSig] int GetHotkey(out short pwHotkey);
            [PreserveSig] int SetHotkey(short wHotkey);
            [PreserveSig] int GetShowCmd(out int piShowCmd);
            [PreserveSig] int SetShowCmd(int iShowCmd);
            [PreserveSig] int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            [PreserveSig] int Resolve(IntPtr hwnd, uint fFlags);
            [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("0000010b-0000-0000-C000-000000000046")]
        private interface IPersistFile
        {
            [PreserveSig] int GetClassID(out Guid pClassID);
            [PreserveSig] int IsDirty();
            [PreserveSig] int Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            [PreserveSig] int Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
            [PreserveSig] int SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            [PreserveSig] int GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }
    }
}
