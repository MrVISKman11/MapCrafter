using System;
using System.Collections.Generic;
using System.IO;
using ExileCore;

namespace MapCrafter.Services
{
    public enum LogLevel
    {
        Debug,
        Info,
        Step,
        Success,
        Warning,
        Error
    }

    public record LogMessage(DateTime Timestamp, LogLevel Level, string Stage, string Message)
    {
        public string Formatted => $"[{Timestamp:HH:mm:ss.fff}] [{Stage}] {Message}";
    }

    public static class CraftLogger
    {
        private static readonly object _lock = new();
        private static readonly List<LogMessage> _entries = new();
        private static string _logFilePath = string.Empty;
        private const int MaxEntries = 600;

        public static bool EnableFileLogging { get; set; } = true;

        public static void Initialize(string pluginDir, bool enableFileLogging = true)
        {
            EnableFileLogging = enableFileLogging;
            lock (_lock)
            {
                _logFilePath = Path.Combine(pluginDir, "crafting.log");
                if (EnableFileLogging)
                {
                    try
                    {
                        File.AppendAllText(_logFilePath, $"\n--- MAP CRAFTER LOG INITIALIZED: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---\n");
                    }
                    catch { }
                }
            }
        }

        public static void Log(LogLevel level, string stage, string message)
        {
            var entry = new LogMessage(DateTime.Now, level, stage, message);

            lock (_lock)
            {
                _entries.Add(entry);
                if (_entries.Count > MaxEntries)
                {
                    _entries.RemoveAt(0);
                }

                if (EnableFileLogging && !string.IsNullOrEmpty(_logFilePath))
                {
                    try
                    {
                        File.AppendAllText(_logFilePath, entry.Formatted + Environment.NewLine);
                    }
                    catch { }
                }
            }

            // Also mirror to ExileCore debug window for warnings/errors/steps
            if (level is LogLevel.Error)
            {
                DebugWindow.LogError($"[MapCrafter] [{stage}] {message}");
            }
            else if (level is LogLevel.Warning)
            {
                DebugWindow.LogMsg($"[MapCrafter] [{stage}] {message}");
            }
        }

        public static void Debug(string stage, string message) => Log(LogLevel.Debug, stage, message);
        public static void Info(string stage, string message) => Log(LogLevel.Info, stage, message);
        public static void Step(string stage, string message) => Log(LogLevel.Step, stage, message);
        public static void Success(string stage, string message) => Log(LogLevel.Success, stage, message);
        public static void Warn(string stage, string message) => Log(LogLevel.Warning, stage, message);
        public static void Error(string stage, string message, Exception? ex = null)
        {
            string full = ex != null ? $"{message} (Exception: {ex.Message}\n{ex.StackTrace})" : message;
            Log(LogLevel.Error, stage, full);
        }

        public static List<LogMessage> GetEntriesSnapshot()
        {
            lock (_lock)
            {
                return new List<LogMessage>(_entries);
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
            }
        }
    }
}
