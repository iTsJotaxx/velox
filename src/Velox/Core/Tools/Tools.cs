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

    // MemoryTool (liberar RAM) está en MemoryTool.cs.

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
                // Se decide archivo a archivo: una carpeta vieja puede tener dentro archivos recién creados por un
                // programa en marcha (un instalador a medias...), y esos no se tocan
                try { CleanDir(new DirectoryInfo(root), limit, isRoot: true); }
                catch (Exception ex) { Log.Warn("Temp folder " + root + ": " + ex.Message); }
                freed += Math.Max(0, before - FolderSize(root));
            }

            Log.Info("Temp files: freed " + (freed / (1024.0 * 1024.0)).ToString("N0") + " MB.");
            return freed;
        }

        /// <summary>
        /// Borra los archivos con más de 24 h (por fecha de modificación) y las carpetas que queden vacías y se crearon
        /// hace más de 24 h. Nunca entra en enlaces ni uniones. Lo que esté en uso o sin permiso se salta.
        /// </summary>
        internal static void CleanDir(DirectoryInfo dir, DateTime limit, bool isRoot)
        {
            FileSystemInfo[] items;
            try { items = dir.GetFileSystemInfos(); } catch { return; }
            foreach (var item in items)
            {
                try
                {
                    if ((item.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (item is DirectoryInfo sub)
                    {
                        CleanDir(sub, limit, false);
                        continue;
                    }
                    if (item.LastWriteTime > limit || item.CreationTime > limit) continue;
                    if ((item.Attributes & FileAttributes.ReadOnly) != 0) item.Attributes = FileAttributes.Normal;
                    item.Delete();
                }
                catch { } // en uso o sin permiso
            }
            if (isRoot) return;
            try
            {
                dir.Refresh();
                if (dir.CreationTime <= limit && !dir.EnumerateFileSystemInfos().Any()) dir.Delete();
            }
            catch { }
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
                    return ProtectionOff() ? Fail(Loc.T("tools.restore.off")) : Fail(Loc.F("tools.restore.errCode", rc));

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
                // Con la Protección del sistema apagada, WMI lanza una excepción sin mensaje
                if (ProtectionOff()) { Log.Warn("Restore point: System Protection is off."); return Fail(Loc.T("tools.restore.off")); }
                Log.Error("Restore point", ex);
                return Fail(string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message);
            }
        }

        const string SrKey = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";
        const string SrPolicy = @"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore";

        /// <summary>¿La Protección del sistema está apagada (y se puede activar: no la bloquea una directiva)?</summary>
        public static bool ProtectionOff()
        {
            if (Reg.GetInt(SrPolicy, "DisableSR") == 1 || Reg.GetInt(SrPolicy, "DisableConfig") == 1) return false;
            return Reg.GetInt(SrKey, "RPSessionInterval") == 0; // 0 = apagada en todas las unidades
        }

        /// <summary>Activa la Protección del sistema en la unidad de Windows (como el botón «Configurar» de Windows).</summary>
        public static bool EnableProtection()
        {
            try
            {
                string drive = Path.GetPathRoot(Environment.SystemDirectory); // "C:\"
                var scope = new ManagementScope(@"\\.\root\default");
                scope.Connect();
                using (var cls = new ManagementClass(scope, new ManagementPath("SystemRestore"), null))
                using (var inParams = cls.GetMethodParameters("Enable"))
                {
                    inParams["Drive"] = drive;
                    using (var res = cls.InvokeMethod("Enable", inParams, null))
                    {
                        uint rc = res == null ? 0 : Convert.ToUInt32(res["ReturnValue"]);
                        Log.Info("System Protection enabled on " + drive + " (rc=" + rc + ").");
                        return rc == 0;
                    }
                }
            }
            catch (Exception ex) { Log.Error("Enable System Protection", ex); return false; }
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
