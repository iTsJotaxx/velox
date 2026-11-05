using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using Velox.Localization;

namespace Velox.Core.Tools
{
    public static class ProcessManager
    {
        // Procesos del sistema que no se pueden cerrar ni cambiar de prioridad desde la app
        static readonly HashSet<string> ProtectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "LsaIso",
            "svchost", "fontdrvhost", "dwm", "Memory Compression", "MsMpEng", "SecurityHealthService", "sihost", "ctfmon",
            "StartMenuExperienceHost", "ShellExperienceHost", "SearchHost", "TextInputHost", "audiodg", "NisSrv", "WUDFHost", "spoolsv",
        };

        static readonly ConcurrentDictionary<string, string> DescCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Mide la CPU ~0,6 s y agrupa por nombre, ordenado por RAM. Corre en un hilo del grupo, no bloquea la interfaz.</summary>
        public static Task<List<ProcEntry>> GetGroupsAsync() => Task.Run(() => GetGroups());

        static List<ProcEntry> GetGroups()
        {
            int cores = Math.Max(1, Environment.ProcessorCount);

            // Primera lectura del tiempo de CPU de cada proceso
            var t0 = new Dictionary<int, double>();
            var first = Process.GetProcesses();
            foreach (var p in first)
            {
                try { t0[p.Id] = p.TotalProcessorTime.TotalMilliseconds; } catch { }
                p.Dispose();
            }

            var sw = Stopwatch.StartNew();
            Thread.Sleep(600);
            double elapsed = Math.Max(1, sw.Elapsed.TotalMilliseconds);

            var paths = new Dictionary<int, string>();
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, ExecutablePath FROM Win32_Process"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementBaseObject o in results)
                    {
                        using (o)
                        {
                            string path = o["ExecutablePath"] as string;
                            if (!string.IsNullOrEmpty(path)) paths[Convert.ToInt32(o["ProcessId"])] = path;
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warn("Process paths unavailable: " + ex.Message); }

            var procs = Process.GetProcesses();
            try
            {
                var list = new List<ProcEntry>();
                foreach (var g in procs.Where(p => p.Id != 0).GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
                {
                    var group = g.ToList();
                    var e = new ProcEntry { Name = g.Key, Count = group.Count, Priority = "-", Description = "" };

                    double ram = 0, cpu = 0;
                    bool known = false;
                    foreach (var p in group)
                    {
                        try { ram += p.WorkingSet64; } catch { }
                        try
                        {
                            double before;
                            if (t0.TryGetValue(p.Id, out before)) { cpu += p.TotalProcessorTime.TotalMilliseconds - before; known = true; }
                        }
                        catch { }
                        string path;
                        if (e.Path == null && paths.TryGetValue(p.Id, out path)) e.Path = path;
                    }
                    e.RamMB = Math.Round(ram / (1024.0 * 1024.0));
                    e.Cpu = known ? Math.Round(Math.Max(0, cpu / elapsed / cores * 100), 1) : -1;

                    try { e.Priority = PriorityText(group[0].PriorityClass); } catch { }

                    if (!string.IsNullOrEmpty(e.Path))
                        e.Description = DescCache.GetOrAdd(e.Path, DescribeFile);

                    e.IsProtected = IsProtected(e.Name);
                    list.Add(e);
                }
                return list.OrderByDescending(x => x.RamMB).ToList();
            }
            finally { foreach (var p in procs) p.Dispose(); }
        }

        static string DescribeFile(string path)
        {
            try { return FileVersionInfo.GetVersionInfo(path).FileDescription ?? ""; }
            catch { return ""; }
        }

        /// <summary>Nombre de la prioridad ya traducido.</summary>
        public static string PriorityText(ProcessPriorityClass pc)
        {
            switch (pc)
            {
                case ProcessPriorityClass.Normal: return Loc.T("prio.normal");
                case ProcessPriorityClass.High: return Loc.T("prio.high");
                case ProcessPriorityClass.AboveNormal: return Loc.T("prio.abovenormal");
                case ProcessPriorityClass.BelowNormal: return Loc.T("prio.belownormal");
                case ProcessPriorityClass.Idle: return Loc.T("prio.idle");
                case ProcessPriorityClass.RealTime: return Loc.T("prio.realtime");
                default: return "-";
            }
        }

        /// <summary>Protegido: la lista del sistema más el propio proceso de Velox.</summary>
        public static bool IsProtected(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            name = name.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            if (ProtectedNames.Contains(name)) return true;
            using (var self = Process.GetCurrentProcess())
                return string.Equals(name, self.ProcessName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Devuelve cuántos procesos se cerraron. Lanza si el proceso está protegido.</summary>
        public static int Kill(string name)
        {
            if (IsProtected(name)) throw new InvalidOperationException(Loc.F("tools.err.protectedKill", name));
            int n = 0, selfId;
            using (var me = Process.GetCurrentProcess()) selfId = me.Id;
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    if (p.Id == selfId) continue;
                    try { p.Kill(); n++; } catch { }
                }
            }
            Log.Info("Closed \"" + name + "\" (" + n + " processes).");
            return n;
        }

        /// <summary>Cambia la prioridad de todos los procesos con ese nombre. Lanza si está protegido.</summary>
        public static int SetPriority(string name, ProcessPriorityClass priority)
        {
            if (IsProtected(name)) throw new InvalidOperationException(Loc.F("tools.err.protectedPriority", name));
            return ApplyPriority(name, priority);
        }

        // Sin comprobar la lista protegida (la usa GameOptimizer, que ya la comprueba antes)
        internal static int ApplyPriority(string name, ProcessPriorityClass priority)
        {
            int n = 0;
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    try { p.PriorityClass = priority; n++; } catch { }
                }
            }
            Log.Info("Priority of \"" + name + "\" -> " + priority + " (" + n + " processes).");
            return n;
        }
    }
}
