using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FvmAssistant
{
    internal static class Log
    {
        private static readonly object Gate = new object();
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private static string _file;

        public static void Init(string file) => _file = file;

        public static void Info(string message) => Write("INFO", message);

        public static void Warn(string message) => Write("WARN", message);

        public static void Error(string message, Exception e = null) => Write("ERROR", e == null ? message : message + " :: " + e);

        private static void Write(string level, string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
            Debug.WriteLine(line);
            if (_file == null) return;
            lock (Gate)
            {
                try { File.AppendAllText(_file, line + Environment.NewLine, Utf8); }
                catch (IOException) { }
            }
        }
    }
}
