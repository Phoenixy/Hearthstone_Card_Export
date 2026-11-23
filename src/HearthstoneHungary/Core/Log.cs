using System;
using System.IO;
using System.Text;

namespace HearthstoneHungary.Core
{
    /// <summary>Egyszerű napló fájlba: %AppData%\HearthstoneHungary\log.txt (1 MB felett a régi log.old.txt lesz).</summary>
    internal static class Log
    {
        private const long MaxBytes = 1024 * 1024;
        private static readonly object FileLock = new object();

        public static string FilePath => Path.Combine(AppSettings.DataDir, "log.txt");

        public static void Info(string message) => Write("INFO", message);

        public static void Warn(string message) => Write("WARN", message);

        public static void Error(string message) => Write("ERROR", message);

        private static void Write(string level, string message)
        {
            try
            {
                lock (FileLock)
                {
                    Directory.CreateDirectory(AppSettings.DataDir);
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > MaxBytes)
                    {
                        var old = Path.Combine(AppSettings.DataDir, "log.old.txt");
                        File.Delete(old);
                        File.Move(FilePath, old);
                    }
                    File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}", Encoding.UTF8);
                }
            }
            catch
            {
                // A naplózás hibája ne állítsa meg a programot.
            }
        }
    }
}
