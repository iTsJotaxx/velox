using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Velox.Core
{
    public enum LogLevel { Info, Warn, Error }

    public sealed class LogEntry
    {
        public DateTime Time { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; }
        public string Line => Time.ToString("yyyy-MM-dd HH:mm:ss") + " [" + Level.ToString().ToUpperInvariant() + "] " + Message;
    }

    /// <summary>Registro de actividad: archivo velox.log + evento para mostrarlo en pantalla. Seguro entre hilos.</summary>
    public static class Log
    {
        static readonly object Sync = new object();
        static readonly List<LogEntry> Recent = new List<LogEntry>();

        public static event Action<LogEntry> Written;

        public static void Initialize()
        {
            try
            {
                // Rota el archivo si pasa de 2 MB
                var fi = new FileInfo(AppPaths.LogFile);
                if (fi.Exists && fi.Length > 2 * 1024 * 1024)
                    File.Copy(AppPaths.LogFile, AppPaths.LogFile + ".old", true);
                if (fi.Exists && fi.Length > 2 * 1024 * 1024)
                    File.Delete(AppPaths.LogFile);
            }
            catch { }
        }

        public static void Info(string message) => Write(LogLevel.Info, message);
        public static void Warn(string message) => Write(LogLevel.Warn, message);
        public static void Error(string message) => Write(LogLevel.Error, message);
        public static void Error(string message, Exception ex) => Write(LogLevel.Error, message + ": " + ex.Message);

        public static void Write(LogLevel level, string message)
        {
            var e = new LogEntry { Time = DateTime.Now, Level = level, Message = message };
            lock (Sync)
            {
                Recent.Add(e);
                if (Recent.Count > 500) Recent.RemoveAt(0);
                try { File.AppendAllText(AppPaths.LogFile ?? "velox.log", e.Line + Environment.NewLine, Encoding.UTF8); } catch { }
            }
            try { Written?.Invoke(e); } catch { }
        }

        public static List<LogEntry> GetRecent()
        {
            lock (Sync) return new List<LogEntry>(Recent);
        }

        /// <summary>Últimas líneas del archivo (para ver sesiones anteriores).</summary>
        public static string[] ReadTail(int lines)
        {
            try
            {
                if (!File.Exists(AppPaths.LogFile)) return new string[0];
                var all = File.ReadAllLines(AppPaths.LogFile, Encoding.UTF8);
                int start = Math.Max(0, all.Length - lines);
                var res = new string[all.Length - start];
                Array.Copy(all, start, res, 0, res.Length);
                return res;
            }
            catch { return new string[0]; }
        }
    }
}
