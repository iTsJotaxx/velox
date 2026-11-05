using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Velox.Core.Backup;
using Velox.Localization;
using Velox.Native;

namespace Velox.Core.Tools
{
    // StartupManager está en StartupManager.cs y ProcessManager en ProcessManager.cs.

    /// <summary>Juegos optimizados de forma permanente (prioridad alta IFEO + GPU de alto rendimiento).</summary>
    public static class GameOptimizer
    {
        const string IdPrefix = "game:";
        const string IfeoBase = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\";
        const string GpuPrefsKey = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences";

        public static List<string> List()
        {
            return BackupStore.Ids()
                .Where(id => id.StartsWith(IdPrefix, StringComparison.Ordinal))
                .Select(id => id.Substring(IdPrefix.Length))
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>Devuelve el nombre del .exe optimizado.</summary>
        public static string Optimize(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                throw new InvalidOperationException(Loc.T("tools.err.gamePath"));

            string exe = Path.GetFileName(exePath);
            string procName = Path.GetFileNameWithoutExtension(exe);
            // Nunca tocar un proceso del sistema aunque el usuario elija su ejecutable
            if (ProcessManager.IsProtected(procName))
                throw new InvalidOperationException(Loc.F("tools.err.protectedPriority", procName));

            string id = IdPrefix + exe;
            Tracked.SetReg(id, IfeoBase + exe + @"\PerfOptions", "CpuPriorityClass", 3, RegistryValueKind.DWord);
            Tracked.SetReg(id, GpuPrefsKey, exePath, "GpuPreference=2;", RegistryValueKind.String);
            ProcessManager.ApplyPriority(procName, ProcessPriorityClass.High);
            Log.Info("Game optimized: " + exe + " (permanent high priority and high-performance GPU).");
            return exe;
        }

        public static void Remove(string exeName)
        {
            BackupStore.Restore(IdPrefix + exeName);
            Log.Info("Game optimization removed: " + exeName + ".");
        }
    }

    public static class MemoryTool
    {
        const double GB = 1024.0 * 1024.0 * 1024.0;

        /// <summary>Consulta directa al kernel (no depende de los contadores de rendimiento). Ceros si falla.</summary>
        public static MemoryLists GetLists()
        {
            var r = new MemoryLists();
            try
            {
                double standby, free;
                if (Mem.GetLists(out standby, out free) == 0)
                {
                    r.StandbyGB = Math.Round(standby / GB, 2);
                    r.FreeGB = Math.Round(free / GB, 2);
                }
            }
            catch (Exception ex) { Log.Warn("Memory lists unavailable: " + ex.Message); }
            return r;
        }

        /// <summary>Vacía la lista en espera. Devuelve (antes, después).</summary>
        public static Tuple<MemoryLists, MemoryLists> PurgeStandby()
        {
            var before = GetLists();
            Mem.FlushModified();
            int status = Mem.PurgeStandby();
            if (status != 0)
                throw new InvalidOperationException(Loc.F("tools.err.memRejected", status.ToString("X8")));
            var after = GetLists();
            Log.Info("Standby memory freed.");
            return Tuple.Create(before, after);
        }
    }

    public static class TempCleaner
    {
        static readonly Regex DriveRootRx = new Regex(@"^[A-Za-z]:$", RegexOptions.Compiled);

        /// <summary>Borra temporales de más de 24 h sin seguir enlaces. Devuelve bytes liberados.</summary>
        public static long Clean()
        {
            var roots = new List<string> { Path.GetTempPath(), Path.Combine(Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows", "Temp") }
                .Select(r => r.TrimEnd('\\'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            DateTime limit = DateTime.Now.AddHours(-24);
            long freed = 0;

            foreach (var root in roots)
            {
                // TEMP en la raíz de una unidad: no se toca
                if (root.Length == 0 || DriveRootRx.IsMatch(root)) continue;
                if (!Directory.Exists(root)) continue;

                long before = FolderSize(root);
                try
                {
                    foreach (var item in new DirectoryInfo(root).GetFileSystemInfos())
                    {
                        try
                        {
                            if (item.LastWriteTime > limit) continue;
                            // Nunca seguir enlaces ni uniones (ni borrar su contenido)
                            if ((item.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                            var dir = item as DirectoryInfo;
                            if (dir != null) Directory.Delete(dir.FullName, true); // no entra en los enlaces de dentro
                            else
                            {
                                if ((item.Attributes & FileAttributes.ReadOnly) != 0) item.Attributes = FileAttributes.Normal;
                                item.Delete();
                            }
                        }
                        catch { } // en uso o sin permiso: se salta
                    }
                }
                catch (Exception ex) { Log.Warn("Temp folder " + root + ": " + ex.Message); }
                freed += Math.Max(0, before - FolderSize(root));
            }

            Log.Info("Temp files: freed " + (freed / (1024.0 * 1024.0)).ToString("N0") + " MB.");
            return freed;
        }

        // Suma el tamaño de los archivos sin entrar en carpetas que sean enlaces
        static long FolderSize(string path)
        {
            long total = 0;
            var stack = new Stack<string>();
            stack.Push(path);
            while (stack.Count > 0)
            {
                DirectoryInfo di;
                try { di = new DirectoryInfo(stack.Pop()); } catch { continue; }
                try
                {
                    foreach (var fi in di.GetFiles()) { try { total += fi.Length; } catch { } }
                    foreach (var sub in di.GetDirectories())
                        if ((sub.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push(sub.FullName);
                }
                catch { }
            }
            return total;
        }
    }

    public static class RestorePointTool
    {
        static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(4);

        public static async Task<RestorePointOutcome> CreateAsync(string description)
        {
            var work = Task.Run(() => Create(string.IsNullOrWhiteSpace(description) ? "Velox" : description));
            var done = await Task.WhenAny(work, Task.Delay(MaxWait)).ConfigureAwait(false);
            if (done != work)
            {
                Log.Warn("Restore point: gave up waiting after 4 minutes.");
                return new RestorePointOutcome { Result = RestorePointResult.Failed, Message = Loc.T("tools.restore.timeout") };
            }
            return await work.ConfigureAwait(false);
        }

        static RestorePointOutcome Create(string description)
        {
            try
            {
                var scope = new ManagementScope(@"\\.\root\default");
                scope.Connect();
                long before = MaxSequence(scope);

                uint rc;
                using (var cls = new ManagementClass(scope, new ManagementPath("SystemRestore"), null))
                using (var inParams = cls.GetMethodParameters("CreateRestorePoint"))
                {
                    inParams["Description"] = description;
                    inParams["RestorePointType"] = 12; // MODIFY_SETTINGS
                    inParams["EventType"] = 100;       // BEGIN_SYSTEM_CHANGE
                    using (var res = cls.InvokeMethod("CreateRestorePoint", inParams, new InvokeMethodOptions(null, MaxWait)))
                        rc = res == null ? 0 : Convert.ToUInt32(res["ReturnValue"]);
                }
                if (rc != 0)
                    return Fail(Loc.F("tools.restore.errCode", rc));

                // Si Windows saltó el punto por la regla de 24 h, devuelve 0 pero no crea ninguno
                long after = MaxSequence(scope);
                if (after > before)
                {
                    Log.Info("Restore point created.");
                    return new RestorePointOutcome { Result = RestorePointResult.Created, Message = Loc.T("tools.restore.created") };
                }

                if (Reg.GetInt(@"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore", "DisableSR") == 1)
                    return Fail(Loc.T("tools.restore.disabled"));

                Log.Info("Restore point skipped: one from the last 24 hours already exists.");
                return new RestorePointOutcome { Result = RestorePointResult.SkippedRecent, Message = Loc.T("tools.restore.skipped") };
            }
            catch (Exception ex)
            {
                Log.Error("Restore point", ex);
                return Fail(ex.Message);
            }
        }

        static RestorePointOutcome Fail(string detail)
        {
            Log.Warn("Restore point failed: " + detail);
            return new RestorePointOutcome { Result = RestorePointResult.Failed, Message = Loc.F("tools.restore.failed", detail) };
        }

        // Mayor SequenceNumber de los puntos de restauración existentes (-1 si no hay ninguno)
        static long MaxSequence(ManagementScope scope)
        {
            long max = -1;
            using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT SequenceNumber FROM SystemRestore")))
            using (var results = searcher.Get())
            {
                foreach (ManagementBaseObject o in results)
                {
                    using (o)
                    {
                        var v = o["SequenceNumber"];
                        if (v != null) max = Math.Max(max, Convert.ToInt64(v));
                    }
                }
            }
            return max;
        }
    }

    public static class ExplorerTool
    {
        /// <summary>
        /// Cierra el Explorador de la sesión actual y espera hasta 10 s a que Windows lo reabra solo (AutoRestartShell).
        /// No se lanza desde aquí porque quedaría con permisos de administrador. true si volvió.
        /// </summary>
        public static bool Restart()
        {
            int session;
            using (var me = Process.GetCurrentProcess()) session = me.SessionId;

            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                using (p)
                {
                    try
                    {
                        if (p.SessionId != session) continue;
                        p.Kill();
                        p.WaitForExit(3000); // que no quede el antiguo contando como "reabierto"
                    }
                    catch { }
                }
            }

            for (int i = 0; i < 100; i++)
            {
                Thread.Sleep(100);
                if (IsRunning(session)) return true;
            }
            return false;
        }

        static bool IsRunning(int session)
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                using (p)
                {
                    try { if (p.SessionId == session && !p.HasExited) return true; } catch { }
                }
            }
            return false;
        }
    }
}
