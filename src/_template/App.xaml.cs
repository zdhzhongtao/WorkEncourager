using System;
using System.IO;
using System.Text;
using System.Windows;

namespace ToolTemplate
{
    public partial class App : Application
    {
        /// <summary>
        /// 工具的数据目录。所有配置、历史、日志都写在这里，绝不写程序目录或仓库目录。
        /// 复制模板后把 "ToolTemplate" 换成你的产品名。
        /// </summary>
        public static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ToolTemplate");

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法创建数据目录：" + ex.Message, "启动失败");
                Shutdown(1);
                return;
            }

            base.OnStartup(e);
        }
    }
}
