using System;
using System.IO;
using System.Text;

namespace WorkEncourager
{
    /// <summary>
    /// 极简日志：只追加写，永不抛异常（日志本身不能成为故障源）。
    /// 文件位置：%APPDATA%\WorkEncourager\app.log（调试模式写到 debug 子目录）。
    /// </summary>
    public static class Log
    {
        /// <summary>单个日志文件的最大体积，超过就轮转成 app.1.log。</summary>
        private const long MaxBytes = 512 * 1024;

        private static readonly object Gate = new object();

        /// <summary>数据目录（与配置、历史记录同目录）。</summary>
        public static string Dir
        {
            get
            {
                var root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WorkEncourager");
                return App.DebugMode ? Path.Combine(root, "debug") : root;
            }
        }

        public static string FilePath => Path.Combine(Dir, "app.log");

        public static void Info(string message) => Write("INFO", message, null);

        public static void Error(string where, Exception ex) => Write("ERROR", where, ex);

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Dir);
                    string path = FilePath;

                    // 体积超限就轮转一次，避免长期运行把磁盘塞满
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > MaxBytes)
                    {
                        string backup = Path.Combine(Dir, "app.1.log");
                        try
                        {
                            if (File.Exists(backup)) File.Delete(backup);
                            File.Move(path, backup);
                        }
                        catch { /* 轮转失败不影响主流程 */ }
                    }

                    var sb = new StringBuilder();
                    sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                      .Append(" [").Append(level).Append("] ").Append(message);
                    if (ex != null)
                    {
                        sb.AppendLine();
                        sb.Append(ex.GetType().FullName).Append(": ").Append(ex.Message);
                        if (!string.IsNullOrEmpty(ex.StackTrace)) sb.AppendLine().Append(ex.StackTrace);
                        if (ex.InnerException != null)
                            sb.AppendLine().Append("--> ").Append(ex.InnerException.Message);
                    }

                    File.AppendAllText(path, sb.ToString() + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { /* 日志失败必须静默 */ }
        }
    }
}
