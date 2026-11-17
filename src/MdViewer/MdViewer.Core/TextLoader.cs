using System;
using System.IO;
using System.Text;

namespace MdViewer.Core
{
    /// <summary>
    /// 读 md 文本：先看 BOM，再按严格 UTF-8 解，失败退回 GB18030。
    /// 中文环境经常会碰到 GBK 保存的 md，直接按 UTF-8 读会全是问号。
    /// </summary>
    public static class TextLoader
    {
        static TextLoader()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static string ReadAllText(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("路径不能为空", "path");
            byte[] bytes = File.ReadAllBytes(path);
            return Decode(bytes);
        }

        public static string Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                try
                {
                    return Encoding.GetEncoding(18030).GetString(bytes);
                }
                catch (Exception)
                {
                    return Encoding.UTF8.GetString(bytes);
                }
            }
        }
    }
}
