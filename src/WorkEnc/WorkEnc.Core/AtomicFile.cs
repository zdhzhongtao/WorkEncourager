using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace WorkEnc.Core
{
    /// <summary>
    /// 安全的文件读写。
    ///
    /// 为什么需要：v1.0.0 直接用 File.WriteAllText 覆盖 settings.json / history.json，
    /// 写到一半断电就得到一个语法损坏的 JSON；而 Load 里 catch 后静默返回默认值，
    /// 用户看到的就是"设置没了、历史清空了"。
    ///
    /// 这里做三件事：
    /// 1. 写入走 临时文件 → 原子替换，磁盘上永远只有"完整的旧版本"或"完整的新版本"；
    /// 2. 每次写入前把旧文件留一份 .bak；
    /// 3. 读取失败时隔离坏文件（改名保留证据）并自动回退到 .bak。
    /// </summary>
    public static class AtomicFile
    {
        /// <summary>出错时的回调（由主程序接到日志上；为 null 则静默）。</summary>
        public static Action<string, Exception> ErrorHandler;

        /// <summary>原子写入文本（UTF-8）。失败抛异常，由调用方决定是否吞掉。</summary>
        public static void WriteAllText(string path, string content)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path 不能为空", nameof(path));

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));

            // 旧文件留一份 .bak（失败不影响主流程）
            try
            {
                if (File.Exists(path))
                {
                    string bak = path + ".bak";
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Copy(path, bak, true);
                }
            }
            catch (Exception ex) { Report("备份失败: " + path, ex); }

            // 原子替换：同一卷上的 File.Move 是原子操作
            File.Move(tmp, path, true);
        }

        /// <summary>
        /// 反序列化 JSON。主文件损坏时隔离它并回退到 .bak；都不可用返回 null。
        /// </summary>
        public static T ReadJson<T>(string path, JsonSerializerOptions options) where T : class
        {
            if (TryDeserialize(path, options, out T value)) return value;

            Quarantine(path);

            if (TryDeserialize(path + ".bak", options, out value))
            {
                Report("已从备份恢复: " + path, null);
                return value;
            }
            return null;
        }

        /// <summary>序列化为 JSON 并原子写入。</summary>
        public static void WriteJson<T>(string path, T value, JsonSerializerOptions options)
        {
            WriteAllText(path, JsonSerializer.Serialize(value, options));
        }

        private static bool TryDeserialize<T>(string path, JsonSerializerOptions options, out T value) where T : class
        {
            value = null;
            try
            {
                if (!File.Exists(path)) return false;
                string s = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(s)) return false;
                value = JsonSerializer.Deserialize<T>(s, options);
                return value != null;
            }
            catch (Exception ex)
            {
                Report("JSON 解析失败: " + path, ex);
                return false;
            }
        }

        /// <summary>把损坏文件改名留档（最多保留 5 份，防止无限堆积）。</summary>
        private static void Quarantine(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                string dir = Path.GetDirectoryName(path) ?? ".";
                string name = Path.GetFileName(path);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Move(path, Path.Combine(dir, name + ".corrupt-" + stamp), true);

                // 清理过旧的损坏文件
                var olds = Directory.GetFiles(dir, name + ".corrupt-*");
                if (olds.Length > 5)
                {
                    Array.Sort(olds, StringComparer.Ordinal);
                    for (int i = 0; i < olds.Length - 5; i++)
                    {
                        try { File.Delete(olds[i]); } catch { }
                    }
                }
            }
            catch (Exception ex) { Report("隔离损坏文件失败: " + path, ex); }
        }

        private static void Report(string message, Exception ex)
        {
            try { ErrorHandler?.Invoke(message, ex); } catch { }
        }
    }
}
