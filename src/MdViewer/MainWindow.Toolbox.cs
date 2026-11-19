using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

namespace MdViewer
{
    /// <summary>
    /// Web 工具箱入口。
    ///
    /// 工具箱本身是一份**单文件 HTML**（JS / CSS 全内联，classic script），
    /// 所以不需要本地服务器，双击就能跑 —— 这里把它嵌进 exe 的资源里，
    /// 点按钮时释放到 %LocalAppData%\MdViewer\web\toolbox.html，再用默认浏览器打开。
    ///
    /// 为什么不直接放个写死的路径：
    ///   程序是「单文件 exe」，可能被拷到桌面、U 盘、任意目录；
    ///   资源跟着 exe 走，释放到固定的用户目录，路径才永远有效。
    /// </summary>
    public partial class MainWindow
    {
        private const string ToolboxResource = "MdViewer.Assets.toolbox.html";

        /// <summary>释放出来的单文件路径，释放失败返回 null。</summary>
        private static string EnsureToolboxFile()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream res = asm.GetManifestResourceStream(ToolboxResource))
                {
                    if (res == null)
                    {
                        Log.Error("找不到内嵌的 " + ToolboxResource, new InvalidOperationException("resource missing"));
                        return null;
                    }

                    using (var buffer = new MemoryStream())
                    {
                        res.CopyTo(buffer);
                        byte[] bytes = buffer.ToArray();

                        string dir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "MdViewer", "web");
                        Directory.CreateDirectory(dir);
                        string path = Path.Combine(dir, "toolbox.html");

                        // 版本更新后 exe 里的资源换了，磁盘上那份要跟着换
                        if (!File.Exists(path) || !SameBytes(path, bytes))
                        {
                            File.WriteAllBytes(path, bytes);
                        }
                        return path;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("释放 Web 工具箱失败", ex);
                return null;
            }
        }

        private static bool SameBytes(string path, byte[] expected)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length != expected.Length) return false;

                byte[] actual = File.ReadAllBytes(path);
                if (actual.Length != expected.Length) return false;
                for (int i = 0; i < expected.Length; i++)
                {
                    if (actual[i] != expected[i]) return false;
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void Toolbox_Click(object sender, RoutedEventArgs e)
        {
            string path = EnsureToolboxFile();
            if (string.IsNullOrEmpty(path))
            {
                MessageBox.Show("Web 工具箱没能释放出来，日志里有详细原因。", "出错了");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                ShowToast("已在浏览器打开 Web 工具箱", "纯前端本地运行，数据不上传 · 收藏和最近使用存在浏览器里", path);
            }
            catch (Exception ex)
            {
                Log.Error("打开 Web 工具箱失败", ex);
                MessageBox.Show("打不开浏览器：\n" + path + "\n\n" + ex.Message, "出错了");
            }
        }
    }
}
