using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Velox.Localization;
using Velox.Native;

namespace Velox.Core.Tools
{
    /// <summary>Foto de la memoria física (bytes).</summary>
    public sealed class MemSnapshot
    {
        public double Total { get; set; }
        public double Available { get; set; }
        public double Free { get; set; }
        public double Standby { get; set; }
        public double Modified { get; set; }
        /// <summary>false si no se pudieron leer las listas (sin permisos): solo Total y Available son fiables.</summary>
        public bool HasLists { get; set; }
        public double InUse => Math.Max(0, Total - (HasLists ? Free + Standby + Modified : Available));
        public double UsedPct => Total > 0 ? (Total - Available) * 100.0 / Total : 0;
        /// <summary>RAM vacía de verdad (si no hay listas, la disponible).</summary>
        public double FreeOrAvailable => HasLists ? Free : Available;
        public double AvailablePct => Total > 0 ? Available * 100.0 / Total : 0;

        public const double GB = 1024.0 * 1024.0 * 1024.0;
        public static string Gb(double bytes) => (bytes / GB).ToString(bytes / GB >= 10 ? "0.0" : "0.00") + " GB";
    }

    /// <summary>Qué hace «Liberar RAM» (se guarda en Settings).</summary>
    public sealed class MemCleanOptions
    {
        public bool WorkingSets { get; set; } = true;
        public bool Modified { get; set; } = true;
        public bool FileCache { get; set; } = true;
        public bool Standby { get; set; } = true;
        public bool Combine { get; set; } = true;

        public static MemCleanOptions FromSettings()
        {
            var s = Settings.Current;
            return new MemCleanOptions { WorkingSets = s.MemWorkingSets, Modified = s.MemModified, FileCache = s.MemFileCache, Standby = s.MemStandby, Combine = s.MemCombine };
        }

        public bool Any => WorkingSets || Modified || FileCache || Standby || Combine;
    }

    public sealed class MemCleanResult
    {
        public MemSnapshot Before { get; set; }
        public MemSnapshot After { get; set; }
        public List<string> Failed { get; } = new List<string>();
        public int StepsDone { get; set; }
        public long PagesCombined { get; set; }
        public DateTime When { get; set; } = DateTime.Now;
        /// <summary>RAM libre (vacía, lista para usar) que se ha ganado. Sin listas, se usa la disponible.</summary>
        public double Gained => After == null || Before == null ? 0
            : After.HasLists && Before.HasLists ? Math.Max(0, After.Free - Before.Free) : Math.Max(0, After.Available - Before.Available);
    }

    public sealed class ProcMem
    {
        public string Name { get; set; }
        public int Count { get; set; }
        public double Bytes { get; set; }
        public int[] Pids { get; set; }
    }

    /// <summary>
    /// Liberar RAM sin cerrar programas. Orden: 1) sacar las páginas de los procesos a la lista en espera
    /// (working sets), 2) escribir a disco la lista modificada, 3) vaciar la caché de archivos, 4) vaciar la lista
    /// en espera (aquí es cuando la RAM pasa a «libre»), 5) combinar páginas idénticas.
    /// Los programas no se cierran: recuperan sus páginas cuando las vuelven a usar (puede notarse un instante).
    /// </summary>
    public static class MemoryTool
    {
        static readonly object Gate = new object();

        public static MemSnapshot Snapshot()
        {
            var s = new MemSnapshot();
            try
            {
                if (MemoryStatus.Read(out double total, out double avail)) { s.Total = total; s.Available = avail; }
                if (Mem.GetDetail(out double free, out double standby, out double modified, out double low) == 0)
                {
                    s.Free = free; s.Standby = standby; s.Modified = modified; s.HasLists = true;
                }
            }
            catch (Exception ex) { Log.Warn("Memory snapshot: " + ex.Message); }
            return s;
        }

        public static MemCleanResult Clean(MemCleanOptions o)
        {
            lock (Gate)
            {
                var r = new MemCleanResult { Before = Snapshot() };
                // 1) Las páginas de los programas y de la caché de archivos pasan a «en espera» o «modificada»
                if (o.WorkingSets) Step(r, "mem.step.workingSets", () => Mem.EmptyWorkingSets());
                if (o.FileCache) Step(r, "mem.step.fileCache", () => Mem.TrimFileCache());
                // 2) Lo que está en espera se libera al instante
                if (o.Standby) Step(r, "mem.step.standby", () => Mem.PurgeStandby());
                // 3) Lo modificado se escribe a disco (pasa a «en espera») y se libera también
                if (o.Modified)
                {
                    Step(r, "mem.step.modified", () => Mem.FlushModified());
                    if (o.Standby) Step(r, "mem.step.standby", () => Mem.PurgeStandby(), countStep: false);
                }
                if (o.Combine)
                {
                    long pages = 0;
                    Step(r, "mem.step.combine", () => Mem.CombinePages(out pages));
                    r.PagesCombined = pages;
                }
                Thread.Sleep(250); // que Windows actualice los contadores
                r.After = Snapshot();
                Log.Info("RAM cleaned: free " + MemSnapshot.Gb(r.Before.FreeOrAvailable) + " -> " + MemSnapshot.Gb(r.After.FreeOrAvailable)
                         + ", available " + MemSnapshot.Gb(r.Before.Available) + " -> " + MemSnapshot.Gb(r.After.Available)
                         + " (" + r.StepsDone + " step(s)" + (r.Failed.Count > 0 ? ", failed: " + string.Join("; ", r.Failed) : "") + ").");
                if (r.StepsDone == 0 && r.Failed.Count > 0)
                    throw new InvalidOperationException(Loc.F("mem.err.allFailed", string.Join("\n", r.Failed)));
                return r;
            }
        }

        static void Step(MemCleanResult r, string key, Func<int> action, bool countStep = true)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                int code = action();
                if (code == 0) { if (countStep) r.StepsDone++; }
                else r.Failed.Add(Loc.T(key) + ": 0x" + code.ToString("X8"));
                Log.Info("RAM step " + key.Substring(9) + ": " + (code == 0 ? "ok" : "code 0x" + code.ToString("X8")) + " in " + sw.ElapsedMilliseconds + " ms.");
            }
            catch (Exception ex) { r.Failed.Add(Loc.T(key) + ": " + ex.Message); }
        }

        /// <summary>Lo que hace la limpieza automática: solo la lista en espera (no toca a los programas).</summary>
        public static MemCleanResult PurgeStandbyOnly()
        {
            return Clean(new MemCleanOptions { WorkingSets = false, Modified = false, FileCache = false, Standby = true, Combine = false });
        }

        /// <summary>Procesos agrupados por nombre que más memoria física usan.</summary>
        public static List<ProcMem> TopProcesses(int count)
        {
            var groups = new Dictionary<string, ProcMem>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        if (p.Id == 0 || p.Id == 4) continue;
                        string n = p.ProcessName;
                        if (n == "Memory Compression" || n == "Registry" || n == "Idle") continue;
                        if (!groups.TryGetValue(n, out var g)) groups[n] = g = new ProcMem { Name = n, Pids = new int[0] };
                        g.Count++;
                        g.Bytes += p.WorkingSet64;
                        g.Pids = g.Pids.Concat(new[] { p.Id }).ToArray();
                    }
                    catch { }
                }
            }
            return groups.Values.OrderByDescending(g => g.Bytes).Take(count).ToList();
        }

        /// <summary>Recorta la memoria de todos los procesos con ese nombre. Devuelve los bytes que soltaron.</summary>
        public static double TrimProcesses(ProcMem g)
        {
            double before = 0, after = 0;
            int ok = 0;
            foreach (var pid in g.Pids)
            {
                try
                {
                    using (var p = Process.GetProcessById(pid))
                    {
                        before += p.WorkingSet64;
                        if (Mem.TrimProcess(pid)) ok++;
                        p.Refresh();
                        after += p.WorkingSet64;
                    }
                }
                catch { }
            }
            if (ok == 0) throw new InvalidOperationException(Loc.F("mem.err.trim", g.Name));
            Log.Info("RAM trimmed for " + g.Name + ": " + MemSnapshot.Gb(Math.Max(0, before - after)) + ".");
            return Math.Max(0, before - after);
        }
    }

    /// <summary>
    /// Limpieza automática mientras Velox está abierto: si la RAM disponible baja del umbral y hay memoria en espera,
    /// vacía la lista en espera (como ISLC). Como mucho una vez por minuto.
    /// </summary>
    public static class MemoryAutoCleaner
    {
        static Timer _timer;
        static DateTime _last = DateTime.MinValue;
        static int _running;

        public static event Action<MemCleanResult> Cleaned;
        public static MemCleanResult LastResult { get; private set; }

        /// <summary>Arranca o para según Settings.</summary>
        public static void Apply()
        {
            if (Settings.Current.AutoMemClean)
            {
                if (_timer == null) _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            }
            else Stop();
        }

        public static void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        /// <summary>
        /// ¿Toca vaciar la caché en espera? Solo cuando queda poca RAM libre de verdad (la «disponible» de Windows ya
        /// cuenta la caché, así que vaciarla no la sube) y la caché ocupa lo bastante como para notarse: al menos 1 GB
        /// o el 10 % de la RAM. Sin esa segunda condición se vaciaría una y otra vez sin ganar nada.
        /// </summary>
        internal static bool ShouldClean(MemSnapshot s, int thresholdPct)
        {
            if (s == null || s.Total <= 0 || !s.HasLists) return false; // sin las listas no se sabe qué hay libre
            if (s.Free * 100.0 / s.Total >= thresholdPct) return false;
            return s.Standby >= Math.Max(MemSnapshot.GB, s.Total * 0.10);
        }

        static void Tick()
        {
            if (Interlocked.Exchange(ref _running, 1) == 1) return;
            try
            {
                if ((DateTime.Now - _last).TotalSeconds < 60) return;
                var s = MemoryTool.Snapshot();
                if (s.Total <= 0) return;
                int threshold = Math.Max(5, Math.Min(60, Settings.Current.AutoMemThreshold));
                if (!ShouldClean(s, threshold)) return;
                _last = DateTime.Now;
                var r = MemoryTool.PurgeStandbyOnly();
                LastResult = r;
                Log.Info("Automatic RAM clean (free under " + threshold + " %).");
                Cleaned?.Invoke(r);
            }
            catch (Exception ex) { Log.Warn("Automatic RAM clean: " + ex.Message); }
            finally { Interlocked.Exchange(ref _running, 0); }
        }
    }
}
