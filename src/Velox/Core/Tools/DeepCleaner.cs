using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Velox.Core.Sys;
using Velox.Localization;

namespace Velox.Core.Tools
{
    /// <summary>
    /// Un tipo de archivo que se puede borrar sin perder nada: solo cachés y restos que Windows o los programas vuelven a
    /// crear cuando los necesitan. Nunca documentos, descargas ni la papelera.
    /// </summary>
    public sealed class CleanCategory
    {
        public string Id { get; set; }
        /// <summary>Carpetas cuyo contenido se borra (la carpeta se queda) o archivos sueltos.</summary>
        public List<string> Roots { get; set; } = new List<string>();
        /// <summary>Solo los archivos que encajan («thumbcache_*.db»); null = todos.</summary>
        public string FilePattern { get; set; }
        /// <summary>Solo lo que lleva más de este tiempo sin tocarse (temporales: lo reciente puede estar en uso).</summary>
        public TimeSpan? MinAge { get; set; }
        /// <summary>Servicios que hay que parar para poder borrar (se vuelven a arrancar después si estaban en marcha).</summary>
        public string[] Services { get; set; } = new string[0];
        /// <summary>Si alguno de estos programas está abierto, sus cachés se dejan (archivos en uso, perfiles a medio escribir).</summary>
        public Dictionary<string, string[]> RootsByProcess { get; set; }
        /// <summary>Motivo por el que ahora no se puede limpiar (ya traducido), o null.</summary>
        public Func<string> Blocker { get; set; }
        public bool SelectedByDefault { get; set; } = true;
        public string Name => Loc.T("clean." + Id);
        public string Description => Loc.T("clean." + Id + ".desc");
    }

    public sealed class CleanMeasure
    {
        public CleanCategory Category { get; set; }
        public long Bytes { get; set; }
        public int Files { get; set; }
        /// <summary>Motivo por el que no se limpia ahora, o null.</summary>
        public string Blocked { get; set; }
        /// <summary>Programas abiertos cuyas cachés se omiten (navegadores).</summary>
        public List<string> SkippedApps { get; set; } = new List<string>();
    }

    /// <summary>Limpieza profunda: primero mide (cuánto ocupa cada tipo) y luego borra solo lo que se marque.</summary>
    public static class DeepCleaner
    {
        static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        static string Win => Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        public static List<CleanCategory> Categories()
        {
            var browsers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            void Chromium(string process, params string[] userData)
            {
                var dirs = new List<string>();
                foreach (var ud in userData)
                {
                    string root = Path.Combine(Local, ud);
                    if (!Directory.Exists(root)) continue;
                    foreach (var profile in SafeDirs(root))
                        foreach (var sub in new[] { @"Cache\Cache_Data", "Code Cache", "GPUCache" })
                            dirs.Add(Path.Combine(profile, sub));
                }
                if (dirs.Count > 0) browsers[process] = dirs.ToArray();
            }
            Chromium("chrome", @"Google\Chrome\User Data");
            Chromium("msedge", @"Microsoft\Edge\User Data");
            Chromium("brave", @"BraveSoftware\Brave-Browser\User Data");
            Chromium("vivaldi", @"Vivaldi\User Data");
            Chromium("opera", @"Opera Software\Opera GX Stable", @"Opera Software\Opera Stable");
            string ff = Path.Combine(Local, @"Mozilla\Firefox\Profiles");
            if (Directory.Exists(ff)) browsers["firefox"] = SafeDirs(ff).Select(p => Path.Combine(p, "cache2")).ToArray();

            return new List<CleanCategory>
            {
                new CleanCategory { Id = "temp", Roots = { Path.GetTempPath(), Path.Combine(Win, "Temp") }, MinAge = TimeSpan.FromHours(24) },
                new CleanCategory
                {
                    Id = "wu", Roots = { Path.Combine(Win, @"SoftwareDistribution\Download") }, Services = new[] { "wuauserv", "bits" },
                    // Con una actualización a medio instalar, esos archivos todavía hacen falta
                    Blocker = () => Drivers.DriverSafety.RebootPendingReason() != null ? Loc.T("clean.block.reboot") : null,
                },
                new CleanCategory { Id = "do", Roots = { Path.Combine(Win, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache") }, Services = new[] { "DoSvc" } },
                new CleanCategory { Id = "browsers", RootsByProcess = browsers },
                new CleanCategory { Id = "shaders", Roots = { Path.Combine(Local, @"NVIDIA\DXCache"), Path.Combine(Local, @"NVIDIA\GLCache"), Path.Combine(Local, "D3DSCache"), Path.Combine(Local, @"AMD\DxCache"), Path.Combine(Local, @"AMD\DxcCache") } },
                new CleanCategory { Id = "thumbs", Roots = { Path.Combine(Local, @"Microsoft\Windows\Explorer") }, FilePattern = "thumbcache_*.db", SelectedByDefault = false },
                new CleanCategory
                {
                    Id = "reports",
                    Roots =
                    {
                        Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportArchive"), Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportQueue"),
                        Path.Combine(Local, @"Microsoft\Windows\WER\ReportArchive"), Path.Combine(Local, @"Microsoft\Windows\WER\ReportQueue"),
                        Path.Combine(Win, "Minidump"), Path.Combine(Win, "MEMORY.DMP"), Path.Combine(Local, "CrashDumps"),
                    },
                },
            };
        }

        static IEnumerable<string> SafeDirs(string root)
        {
            try { return new DirectoryInfo(root).GetDirectories().Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0).Select(d => d.FullName).ToList(); }
            catch { return new string[0]; }
        }

        static bool Running(string process) { try { var p = Process.GetProcessesByName(process); foreach (var x in p) x.Dispose(); return p.Length > 0; } catch { return false; } }

        /// <summary>Raíces que se tocarían ahora (en navegadores, solo los que están cerrados).</summary>
        static List<string> ActiveRoots(CleanCategory c, List<string> skippedApps)
        {
            var roots = new List<string>(c.Roots);
            if (c.RootsByProcess != null)
                foreach (var kv in c.RootsByProcess)
                {
                    if (Running(kv.Key)) { skippedApps?.Add(kv.Key); continue; }
                    roots.AddRange(kv.Value);
                }
            return roots;
        }

        // ------------------------------------------------------------ Medir

        public static CleanMeasure Measure(CleanCategory c)
        {
            var m = new CleanMeasure { Category = c };
            try { m.Blocked = c.Blocker?.Invoke(); } catch { }
            DateTime? limit = c.MinAge.HasValue ? DateTime.Now - c.MinAge.Value : (DateTime?)null;
            foreach (var root in ActiveRoots(c, m.SkippedApps))
                Walk(root, c.FilePattern, limit, f => { m.Bytes += f.Length; m.Files++; }, null);
            return m;
        }

        // ------------------------------------------------------------ Limpiar

        /// <summary>Borra lo medido. Lo que está en uso se salta. Devuelve los bytes liberados. No lanza.</summary>
        public static long Clean(CleanCategory c)
        {
            try { if (c.Blocker?.Invoke() != null) return 0; } catch { return 0; }
            var restart = new List<string>();
            long freed = 0;
            try
            {
                foreach (var s in c.Services)
                    if (Services.StatusText(s) == "Running") { Services.TryStop(s); restart.Add(s); }
                DateTime? limit = c.MinAge.HasValue ? DateTime.Now - c.MinAge.Value : (DateTime?)null;
                foreach (var root in ActiveRoots(c, null))
                    Walk(root, c.FilePattern, limit, f =>
                    {
                        long len = f.Length;
                        try { f.Attributes = FileAttributes.Normal; f.Delete(); freed += len; } catch { }
                    },
                    // Carpetas vacías que quedan dentro (la raíz no se borra nunca)
                    d => { try { if (!d.EnumerateFileSystemInfos().Any() && (limit == null || d.LastWriteTime < limit)) d.Delete(); } catch { } });
            }
            catch (Exception ex) { Log.Warn("Deep clean " + c.Id + ": " + ex.Message); }
            finally
            {
                // Los servicios que estaban en marcha vuelven a arrancar pase lo que pase
                foreach (var s in restart) Services.TryStart(s);
            }
            Log.Info("Deep clean " + c.Id + ": " + (freed / 1048576.0).ToString("N0") + " MB freed.");
            return freed;
        }

        /// <summary>
        /// Recorre una raíz (carpeta o archivo suelto) sin seguir enlaces ni uniones. onDir se llama al salir de cada
        /// subcarpeta (nunca con la raíz).
        /// </summary>
        internal static void Walk(string root, string pattern, DateTime? olderThan, Action<FileInfo> onFile, Action<DirectoryInfo> onDir)
        {
            if (string.IsNullOrEmpty(root)) return;
            if (File.Exists(root))
            {
                var fi = new FileInfo(root);
                if ((fi.Attributes & FileAttributes.ReparsePoint) == 0 && (olderThan == null || fi.LastWriteTime < olderThan)) onFile(fi);
                return;
            }
            var di = new DirectoryInfo(root);
            if (!di.Exists || (di.Attributes & FileAttributes.ReparsePoint) != 0) return;
            WalkDir(di, pattern, olderThan, onFile, onDir, isRoot: true);
        }

        static void WalkDir(DirectoryInfo di, string pattern, DateTime? olderThan, Action<FileInfo> onFile, Action<DirectoryInfo> onDir, bool isRoot)
        {
            FileInfo[] files;
            try { files = pattern == null ? di.GetFiles() : di.GetFiles(pattern); } catch { files = new FileInfo[0]; }
            foreach (var f in files)
            {
                if ((f.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (olderThan != null && f.LastWriteTime >= olderThan) continue;
                onFile(f);
            }
            if (pattern != null) return; // con patrón solo se mira la propia carpeta (miniaturas)
            DirectoryInfo[] dirs;
            try { dirs = di.GetDirectories(); } catch { dirs = new DirectoryInfo[0]; }
            foreach (var d in dirs)
            {
                if ((d.Attributes & FileAttributes.ReparsePoint) != 0) continue; // nunca seguir enlaces
                WalkDir(d, pattern, olderThan, onFile, onDir, isRoot: false);
                onDir?.Invoke(d); // d nunca es la raíz: la raíz no se borra
            }
        }
    }
}
