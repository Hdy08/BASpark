using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace BASpark
{
    public sealed class AppLoggerTraceListener : TraceListener
    {
        public override void Write(string? message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                AppLogger.Debug(message);
            }
        }
        public override void WriteLine(string? message) => Write(message + Environment.NewLine);
    }

    public static class AppLogger
    {
        private const int MaxEntries = 800;
        private static readonly object LockObj = new();
        private static readonly List<string> Entries = new();
        private static bool _initialized;

        [ThreadStatic]
        private static bool _isLogging;

        public static event Action<string>? EntryAdded;

        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            Trace.Listeners.Add(new AppLoggerTraceListener());
            Info("AppLogger initialized.");
        }

        public static IReadOnlyList<string> GetEntries()
        {
            lock (LockObj)
            {
                return Entries.ToList();
            }
        }

        public static void Clear()
        {
            lock (LockObj)
            {
                Entries.Clear();
            }
        }

        public static void Debug(string message) => Log("DEBUG", message);

        public static void Info(string message) => Log("INFO", message);

        public static void Warn(string message) => Log("WARN", message);

        public static void Error(string message, Exception? ex = null)
        {
            if (ex == null)
            {
                Log("ERROR", message);
                return;
            }

            Log("ERROR", $"{message} | {ex.GetType().Name}: {ex.Message}");
        }

        private static void Log(string level, string message)
        {
            if (_isLogging)
            {
                return;
            }

            try
            {
                _isLogging = true;

                string sanitized = Sanitize(message);
                if (string.IsNullOrWhiteSpace(sanitized))
                {
                    return;
                }

                string line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {sanitized}";
                lock (LockObj)
                {
                    Entries.Add(line);
                    if (Entries.Count > MaxEntries)
                    {
                        Entries.RemoveRange(0, Entries.Count - MaxEntries);
                    }
                }

                System.Diagnostics.Debug.WriteLine(line);
                WriteToFile(line);
                EntryAdded?.Invoke(line);
            }
            finally
            {
                _isLogging = false;
            }
        }

        /// <summary>
        /// 把日志落到磁盘。仅内存日志在排查启动失败时毫无用处——窗口起不来时
        /// 用户看不到日志页，进程一退日志就没了。
        /// 写入失败必须静默，日志本身不能再引发崩溃。
        /// </summary>
        private static void WriteToFile(string line)
        {
            try
            {
                string path = LogFilePath;
                lock (FileLock)
                {
                    // 超过 1 MB 时截断，避免长期运行无限增长。
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > 1024 * 1024)
                    {
                        File.Delete(path);
                    }

                    File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // 日志写入失败不影响程序运行。
            }
        }

        private static readonly object FileLock = new();

        private static string? _logFilePath;

        public static string LogFilePath
        {
            get
            {
                if (_logFilePath == null)
                {
                    string directory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "BASpark");
                    Directory.CreateDirectory(directory);
                    _logFilePath = Path.Combine(directory, "baspark.log");
                }

                return _logFilePath;
            }
        }

        private static string Sanitize(string value)        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(Math.Min(value.Length, 2000));
            foreach (char ch in value)
            {
                if (char.IsControl(ch) && ch != '\t')
                {
                    continue;
                }

                builder.Append(ch);
                if (builder.Length >= 2000)
                {
                    break;
                }
            }

            return builder.ToString().Trim();
        }
    }
}