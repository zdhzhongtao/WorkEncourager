using System;
using System.IO;
using System.Text;

namespace MoneyCalc
{
    /// <summary>
    /// 极简日志：只追加写，永不抛异常（日志本身不能成为故障源）。
    /// 文件位置：%APPDATA%\TableDiff\app.log，超过 512 KB 轮转成 app.1.log。
    /// </summary>
    public static class Log
    {
        private const long MaxBytes = 512 * 1024;
        private static readonly object Gate = new object();
        private static string _dir = string.Empty;

        public static void Init(string dir)
        {
            _dir = dir ?? string.Empty;
            try { if (_dir.Length > 0) Directory.CreateDirectory(_dir); } catch { }
        }

        public static string Dir => _dir;
        public static string FilePath => Path.Combine(_dir, "app.log");

        public static void Info(string message) => Write("INFO", message, null);

        public static void Error(string where, Exception ex) => Write("ERROR", where, ex);

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                if (_dir.Length == 0) return;
                lock (Gate)
                {
                    Directory.CreateDirectory(_dir);
                    string path = FilePath;

                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > MaxBytes)
                    {
                        string backup = Path.Combine(_dir, "app.1.log");
                        try
                        {
                            if (File.Exists(backup)) File.Delete(backup);
                            File.Move(path, backup);
                        }
                        catch { }
                    }

                    var sb = new StringBuilder();
                    sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                      .Append(" [").Append(level).Append("] ").Append(message);
                    if (ex != null)
                    {
                        sb.AppendLine();
                        sb.Append(ex.GetType().FullName).Append(": ").Append(ex.Message);
                        if (!string.IsNullOrEmpty(ex.StackTrace)) sb.AppendLine().Append(ex.StackTrace);
                    }

                    File.AppendAllText(path, sb.ToString() + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
