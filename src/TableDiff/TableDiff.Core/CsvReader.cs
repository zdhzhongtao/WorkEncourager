using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TableDiff.Core
{
    /// <summary>
    /// CSV / TSV 读取：编码探测 + 分隔符自动识别 + 引号内的逗号与换行。
    /// 不依赖任何第三方库，纯手写，方便单测。
    /// </summary>
    public static class CsvReader
    {
        static CsvReader()
        {
            // .NET 默认不带 GB18030，注册一次就能 Encoding.GetEncoding(936/54936)
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
        }

        /// <summary>
        /// 读文件并按内容判断编码：有 BOM 按 BOM；无 BOM 先严格 UTF-8 解码，失败退 GB18030。
        /// </summary>
        public static string ReadAllText(string path, out string encodingName)
        {
            byte[] bytes = File.ReadAllBytes(path);
            return DecodeText(bytes, out encodingName);
        }

        public static string DecodeText(byte[] bytes, out string encodingName)
        {
            if (bytes == null || bytes.Length == 0)
            {
                encodingName = "utf-8";
                return string.Empty;
            }

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                encodingName = "utf-8-bom";
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                encodingName = "utf-16le";
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                encodingName = "utf-16be";
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }

            // 严格 UTF-8：遇到非法序列会抛异常，就说明不是 UTF-8
            try
            {
                var strict = new UTF8Encoding(false, true);
                string text = strict.GetString(bytes);
                encodingName = "utf-8";
                return text;
            }
            catch (DecoderFallbackException)
            {
                // 中文 Windows 环境最常见的 CSV 编码
                encodingName = "gb18030";
                try { return Encoding.GetEncoding("GB18030").GetString(bytes); }
                catch
                {
                    encodingName = "latin1";
                    return Encoding.Latin1.GetString(bytes);
                }
            }
        }

        /// <summary>
        /// 自动判断分隔符：在引号外统计前若干行里逗号 / 制表符 / 分号的出现次数，取最多的。
        /// 平手时按 制表符 &gt; 逗号 &gt; 分号 的优先级（TSV 里逗号通常属于内容）。
        /// </summary>
        public static char DetectDelimiter(string text, string pathHint)
        {
            if (!string.IsNullOrEmpty(pathHint))
            {
                string ext = Path.GetExtension(pathHint) ?? string.Empty;
                if (ext.Equals(".tsv", StringComparison.OrdinalIgnoreCase)) return '\t';
            }

            int comma = 0, tab = 0, semi = 0, bar = 0;
            bool inQuotes = false;
            int lines = 0;

            for (int i = 0; i < text.Length && lines < 10; i++)
            {
                char ch = text[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < text.Length && text[i + 1] == '"') { i++; continue; }
                    inQuotes = !inQuotes;
                    continue;
                }
                if (inQuotes) continue;

                if (ch == '\n') { lines++; continue; }
                if (ch == ',') comma++;
                else if (ch == '\t') tab++;
                else if (ch == ';') semi++;
                else if (ch == '|') bar++;
            }

            if (tab >= comma && tab >= semi && tab > 0) return '\t';
            if (comma >= semi && comma >= bar && comma > 0) return ',';
            if (semi >= bar && semi > 0) return ';';
            if (bar > 0) return '|';
            return ',';
        }

        /// <summary>按 RFC4180 解析：双引号包裹、内部 "" 转义、引号内的换行与分隔符。</summary>
        public static List<string[]> Parse(string text, char delimiter)
        {
            var rows = new List<string[]>();
            if (string.IsNullOrEmpty(text)) return rows;

            var row = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            bool fieldHasContent = false;   // 当前字段是否已经写过东西（引号只有出现在字段开头才算转义）

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];

                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(ch);
                    continue;
                }

                if (ch == '"')
                {
                    // RFC4180 只在字段开头认引号；「27"」这种英寸符号必须当普通字符，
                    // 否则一张表里出现一个引号，后面所有行都会被吞进同一个单元格。
                    if (!fieldHasContent) inQuotes = true;
                    else sb.Append('"');
                    fieldHasContent = true;
                    continue;
                }
                if (ch == delimiter)
                {
                    row.Add(sb.ToString());
                    sb.Clear();
                    fieldHasContent = false;
                    continue;
                }
                if (ch == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(sb.ToString());
                    sb.Clear();
                    fieldHasContent = false;
                    rows.Add(row.ToArray());
                    row.Clear();
                    continue;
                }
                if (ch == '\n')
                {
                    row.Add(sb.ToString());
                    sb.Clear();
                    fieldHasContent = false;
                    rows.Add(row.ToArray());
                    row.Clear();
                    continue;
                }
                sb.Append(ch);
                fieldHasContent = true;
            }

            if (sb.Length > 0 || row.Count > 0)
            {
                row.Add(sb.ToString());
                rows.Add(row.ToArray());
            }

            // 干掉文件末尾的空行（只有一列且为空的最后一行为典型）
            while (rows.Count > 0)
            {
                var last = rows[rows.Count - 1];
                if (last.Length == 1 && string.IsNullOrWhiteSpace(last[0])) rows.RemoveAt(rows.Count - 1);
                else break;
            }

            return rows;
        }

        /// <summary>读一个 csv/tsv 文件成一张表。</summary>
        public static SheetData ReadSheet(string path)
        {
            string text = ReadAllText(path, out _);
            char delimiter = DetectDelimiter(text, path);
            var raw = Parse(text, delimiter);
            string name = Path.GetFileNameWithoutExtension(path);
            return SheetData.FromRows(name, raw, true);
        }
    }
}
