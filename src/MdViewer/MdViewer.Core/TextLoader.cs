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

        /// <summary>没有 BOM 时猜 UTF-16：奇数位 NUL 多 = LE，偶数位 NUL 多 = BE。返回 0/1/2。</summary>
        private static int SniffBomlessUtf16(byte[] bytes)
        {
            int count = Math.Min(bytes.Length, 4096);
            if (count < 4) return 0;

            int zeroEven = 0, zeroOdd = 0;
            for (int i = 0; i < count; i++)
            {
                if (bytes[i] != 0) continue;
                if ((i & 1) == 0) zeroEven++;
                else zeroOdd++;
            }

            if (zeroEven + zeroOdd < 2) return 0;
            if (zeroOdd >= zeroEven * 3) return 1;   // UTF-16LE
            if (zeroEven >= zeroOdd * 3) return 2;   // UTF-16BE
            return 0;
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

            // 没有 BOM 的 UTF-16：靠 NUL 字节分布判断（ASCII/换行每隔一个字节就是 0）
            int utf16 = SniffBomlessUtf16(bytes);
            if (utf16 == 1) return Encoding.Unicode.GetString(bytes);
            if (utf16 == 2) return Encoding.BigEndianUnicode.GetString(bytes);

            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                try
                {
                    // GB18030 的代码页是 54936；18030 不是合法的 .NET 代码页
                    // （GetEncoding(18030) 必抛 NotSupportedException，之前这条兜底等于死代码）。
                    return Encoding.GetEncoding(54936).GetString(bytes);
                }
                catch (Exception)
                {
                    return Encoding.UTF8.GetString(bytes);
                }
            }
        }
    }
}
