using System;
using System.IO;
using System.Text;

namespace FvmAssistant.Automation
{
    /// <summary>
    /// Text files from 轨道编辑器. The editor writes GBK (the ANSI code page of a Chinese Windows);
    /// our copy in vendor\track has been converted to UTF-8 so it opens in any editor. Read both,
    /// and write UTF-8.
    /// </summary>
    internal static class EditorText
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly Encoding Gbk = Encoding.GetEncoding(936);

        public static string ReadAllText(string path) => Decode(File.ReadAllBytes(path));

        public static string[] ReadAllLines(string path) =>
            ReadAllText(path).Replace("\r\n", "\n").Split('\n');

        public static string Decode(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                // Chinese GBK text is practically never valid UTF-8.
                return Gbk.GetString(bytes);
            }
        }

        public static void WriteAllText(string path, string text) =>
            File.WriteAllText(path, text, new UTF8Encoding(false));
    }
}
