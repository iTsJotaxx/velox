using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Velox.Core
{
    public static class AppInfo
    {
        public static string Version
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
        }

        public const string RepositoryUrl = "https://github.com/iTsJotaxx/velox";
    }

    /// <summary>Carpetas de datos: %ProgramData%\Velox (copias y registro) y %AppData%\Velox (preferencias).</summary>
    public static class AppPaths
    {
        public static string DataDir { get; private set; }
        public static string BackupFile { get; private set; }
        public static string LogFile { get; private set; }
        public static string SettingsFile { get; private set; }
        public static string ReportsDir { get; private set; }

        /// <param name="dataDirOverride">Solo para pruebas: carpeta de datos alternativa.</param>
        public static void Initialize(string dataDirOverride = null)
        {
            DataDir = dataDirOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Velox");
            Directory.CreateDirectory(DataDir);
            BackupFile = Path.Combine(DataDir, "backup.xml");
            LogFile = Path.Combine(DataDir, "velox.log");
            ReportsDir = Path.Combine(DataDir, "Reports");
            var userDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Velox");
            Directory.CreateDirectory(userDir);
            SettingsFile = Path.Combine(userDir, "settings.xml");
        }
    }

    /// <summary>Trae al frente la ventana de Velox si ya hay una abierta.</summary>
    public static class NativeWindowActivator
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);

        public static void ActivateExisting(string title)
        {
            IntPtr h = FindWindow(null, title);
            if (h == IntPtr.Zero) return;
            if (IsIconic(h)) ShowWindow(h, 9); // SW_RESTORE
            SetForegroundWindow(h);
        }
    }
}
