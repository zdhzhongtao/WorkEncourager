using System;
using System.IO;
using System.Text;
using System.Windows;

namespace ToolTemplate
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string text = InputBox.Text ?? string.Empty;
            if (text.Length == 0)
            {
                StatusText.Text = "请输入点内容再保存。";
                return;
            }

            string path = Path.Combine(App.DataDir, "data.txt");
            try
            {
                SafeWrite(path, text);
                StatusText.Text = "已保存到：" + path;
            }
            catch (Exception ex)
            {
                StatusText.Text = "保存失败：" + ex.Message;
            }
        }

        /// <summary>
        /// 安全写入：临时文件 → 替换 → 保留 .bak。断电或崩溃不会把原文件写坏。
        /// 生产项目里建议直接抄 WorkEnc.Core/AtomicFile.cs，功能更全（含损坏隔离）。
        /// </summary>
        internal static void SafeWrite(string path, string content)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(true)); // 带 BOM，Excel 打开中文不乱码

            if (File.Exists(path))
            {
                string bak = path + ".bak";
                try { File.Copy(path, bak, true); } catch { /* 备份失败不阻断主流程 */ }
                File.Replace(tmp, path, null);
            }
            else
            {
                File.Move(tmp, path);
            }
        }
    }
}
