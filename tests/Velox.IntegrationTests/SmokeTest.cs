using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using Velox.Core;
using Velox.Core.Apps;
using Velox.Core.Diagnostics;
using Velox.Core.Disk;
using Velox.Core.Drivers;
using Velox.Core.Scan;
using Velox.Core.Tools;
using Velox.Core.Update;
using Velox.Localization;

namespace Velox.IntegrationTests
{
    /// <summary>
    /// Prueba funcional de todas las secciones (como administrador): ejecuta de verdad cada función y deja todo como
    /// estaba (lo que crea, lo borra). No instala ni desinstala programas ni drivers.
    /// Uso: Velox.IntegrationTests.exe [salida.txt] --smoke
    /// </summary>
    static class SmokeTest
    {
        static readonly StringBuilder Out = new StringBuilder();
        static int _fails, _total;

        static void W(string s) { Console.WriteLine(s); Out.AppendLine(s); }

        static void Step(string name, Func<string> action)
        {
            _total++;
            var sw = Stopwatch.StartNew();
            try
            {
                string info = action();
                W("[OK] " + name.PadRight(34) + " " + info + "  (" + sw.ElapsedMilliseconds + " ms)");
            }
            catch (Exception ex)
            {
                _fails++;
                W("[XX] " + name.PadRight(34) + " " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        static void Assert(bool ok, string why) { if (!ok) throw new Exception(why); }

        public static int Run(string outFile)
        {
            string dir = Path.Combine(Path.GetTempPath(), "Velox-Smoke");
            AppPaths.Initialize(dir);
            Log.Initialize();
            SystemInfo sys = null;

            Step("Escaneo del equipo", () =>
            {
                sys = SystemScanner.Scan(null);
                Assert(sys != null && sys.RamTotalGB > 0, "sin datos");
                return sys.Cpu + " | " + sys.RamTotalGB + " GB | " + sys.DiskType + " | build " + sys.Build;
            });
            Step("Hallazgos", () => Findings.Build(sys).Count + " hallazgo(s)");

            // ---------------------------------------------------------------- Diagnósticos (los que corren dentro de Velox)
            foreach (var id in new[] { "ping", "netinfo", "flushdns", "diskhealth", "battery" })
            {
                var a = DiagnosticsCatalog.All.FirstOrDefault(x => x.Id == id);
                if (a == null) continue;
                if (!DiagnosticsCatalog.IsAvailable(a, sys)) { W("[--] Diagnóstico " + id + ": no aplica en este equipo"); continue; }
                Step("Diagnóstico " + id, () =>
                {
                    var r = DiagRunners.RunInApp(id);
                    Assert(r != null, "sin resultado");
                    Assert(r.Success, r.Message);
                    return (r.Message ?? "").Replace("\n", " ").Substring(0, Math.Min(90, (r.Message ?? "").Length));
                });
            }

            // ---------------------------------------------------------------- Memoria
            Step("Memoria: lectura de listas", () =>
            {
                var s = MemoryTool.Snapshot();
                Assert(s.Total > 0 && s.HasLists, "no se leen las listas");
                return "libre " + MemSnapshot.Gb(s.Free) + ", en espera " + MemSnapshot.Gb(s.Standby) + ", modificada " + MemSnapshot.Gb(s.Modified);
            });
            Step("Memoria: liberar RAM (todo)", () =>
            {
                var r = MemoryTool.Clean(new MemCleanOptions());
                Assert(r.Failed.Count == 0, string.Join("; ", r.Failed));
                return "+" + MemSnapshot.Gb(r.Gained) + " libres (" + MemSnapshot.Gb(r.Before.Free) + " -> " + MemSnapshot.Gb(r.After.Free) + ")";
            });
            Step("Memoria: recortar un proceso", () =>
            {
                var top = MemoryTool.TopProcesses(15).FirstOrDefault(p => !ProcessManager.IsProtected(p.Name) && p.Name != "Velox.IntegrationTests");
                Assert(top != null, "sin procesos");
                return top.Name + " soltó " + MemSnapshot.Gb(MemoryTool.TrimProcesses(top));
            });

            // ---------------------------------------------------------------- Herramientas
            Step("Limpiar temporales", () => (TempCleaner.Clean() / 1048576.0).ToString("0") + " MB liberados");
            Step("Caché de shaders", () => (ShaderCache.Clear() / 1048576.0).ToString("0") + " MB");
            Step("Protección del sistema", () =>
            {
                bool off = RestorePointTool.ProtectionOff();
                if (off) Assert(RestorePointTool.EnableProtection() && !RestorePointTool.ProtectionOff(), "no se pudo activar");
                return off ? "estaba apagada: activada" : "ya estaba activada";
            });
            Step("Punto de restauración", () =>
            {
                var r = RestorePointTool.CreateAsync("Velox smoke test").GetAwaiter().GetResult();
                Assert(r.Result != RestorePointResult.Failed, r.Message);
                return r.Result.ToString();
            });

            // ---------------------------------------------------------------- Inicio de Windows
            Step("Inicio de Windows: activar/desactivar", () =>
            {
                const string run = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
                const string name = "VeloxSmokeTest";
                Reg.Set(run, name, "\"C:\\Windows\\System32\\cmd.exe\" /c exit", RegistryValueKind.String);
                try
                {
                    var e = StartupManager.GetAll().FirstOrDefault(x => x.ValueName == name);
                    Assert(e != null && e.Enabled, "no aparece o no está activado");
                    StartupManager.SetEnabled(e, false);
                    Assert(!StartupManager.GetAll().First(x => x.ValueName == name).Enabled, "no se desactivó");
                    StartupManager.SetEnabled(e, true);
                    Assert(StartupManager.GetAll().First(x => x.ValueName == name).Enabled, "no se reactivó");
                    return StartupManager.GetAll().Count + " programas de inicio";
                }
                finally
                {
                    Reg.Remove(run, name);
                    Reg.Remove(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", name);
                }
            });

            // ---------------------------------------------------------------- Procesos y juegos
            Step("Procesos: lista con CPU", () =>
            {
                var g = ProcessManager.GetGroupsAsync().GetAwaiter().GetResult();
                Assert(g.Count > 10, "pocos procesos");
                return g.Count + " grupos, más RAM: " + g[0].Name;
            });
            Step("Procesos: prioridad y cerrar", () =>
            {
                string exe = Path.Combine(dir, "velox_smoke_proc.exe");
                File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), exe, true);
                var p = Process.Start(new ProcessStartInfo(exe, "-n 60 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false });
                try
                {
                    Thread.Sleep(500);
                    int n = ProcessManager.SetPriority("velox_smoke_proc", ProcessPriorityClass.High);
                    p.Refresh();
                    Assert(n == 1 && p.PriorityClass == ProcessPriorityClass.High, "prioridad no cambiada");
                    int k = ProcessManager.Kill("velox_smoke_proc");
                    Assert(k == 1 && p.WaitForExit(5000), "no se cerró");
                    bool protectedRefused = false;
                    try { ProcessManager.Kill("csrss"); } catch (InvalidOperationException) { protectedRefused = true; }
                    Assert(protectedRefused, "dejó cerrar un proceso del sistema");
                    return "alta prioridad, cerrado; csrss protegido";
                }
                finally { try { if (!p.HasExited) p.Kill(); } catch { } }
            });
            Step("Optimizar juego (permanente)", () =>
            {
                string exe = Path.Combine(dir, "velox_smoke_game.exe");
                File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), exe, true);
                string name = GameOptimizer.Optimize(exe);
                string ifeo = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\" + name + @"\PerfOptions";
                Assert(Reg.GetInt(ifeo, "CpuPriorityClass") == 3 && GameOptimizer.List().Contains(name), "no se aplicó");
                GameOptimizer.Remove(name);
                Assert(Reg.GetInt(ifeo, "CpuPriorityClass") == null && !GameOptimizer.List().Contains(name), "no se deshizo");
                return "aplicado y deshecho";
            });

            // ---------------------------------------------------------------- Actualizador, drivers, apps
            Step("Actualizador (GitHub)", () =>
            {
                var u = Updater.Check();
                return "última " + u.Tag + (u.IsNewer ? " (más nueva)" : " (al día)");
            });
            Step("Drivers: inventario", () => DriverInventory.GetInstalled().Count + " dispositivos");
            Step("Drivers: búsqueda", () =>
            {
                var list = DriverUpdater.SearchAsync(null).GetAwaiter().GetResult();
                return list.Count + " actualización(es), " + DriverUpdater.LastSearchWarnings.Count + " aviso(s)";
            });
            Step("Drivers: protecciones", () => string.Join(" | ", DriverSafety.Blockers().DefaultIfEmpty("ninguna")));
            Step("Apps: lo instalado (winget)", () =>
            {
                Assert(Winget.IsAvailable, "winget no disponible");
                var m = Winget.GetInstalled();
                Assert(m.Count > 0, "no reconoce nada");
                return m.Count + " paquetes reconocidos";
            });
            Step("Apps: buscar en winget", () =>
            {
                var h = Winget.Search("vlc");
                Assert(h.Any(x => x.Id == "VideoLAN.VLC"), "no encuentra VLC");
                return h.Count + " resultados";
            });
            Step("Desinstalar: programas", () =>
            {
                var d = Uninstaller.GetDesktop();
                Assert(d.Count > 0, "lista vacía");
                return d.Count + " programas, " + d.Count(x => x.IsProtected) + " protegidos";
            });
            Step("Desinstalar: Microsoft Store", () =>
            {
                var s = Uninstaller.GetStore();
                Assert(s.Count > 0, "lista vacía");
                return s.Count + " apps, " + s.Count(x => x.IsProtected) + " protegidas";
            });

            // ---------------------------------------------------------------- Disco
            Step("Disco: analizar C:\\Users", () =>
            {
                var root = DiskScanner.Scan(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), null, CancellationToken.None);
                Assert(root.Size > 0 && root.Children.Count > 0, "vacío");
                return Format.Bytes(root.Size) + ", " + root.FileCount.ToString("N0") + " archivos";
            });
            Step("Disco: carpetas protegidas", () =>
            {
                var root = DiskScanner.Scan(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), null, CancellationToken.None);
                var appdata = root.Children.FirstOrDefault(c => c.Name.Equals("AppData", StringComparison.OrdinalIgnoreCase));
                Assert(appdata != null && DiskActions.ProtectedReason(appdata) != null, "AppData no protegida");
                return "AppData protegida";
            });

            // ---------------------------------------------------------------- Explorador y ajustes
            Step("Reiniciar el Explorador", () =>
            {
                bool ok = ExplorerTool.Restart();
                Assert(ok, "el Explorador no volvió en 10 s");
                return "volvió solo";
            });
            Step("Preferencias: guardar y leer", () =>
            {
                // Las preferencias son las reales del usuario (%AppData%\Velox): se cambia un valor, se comprueba y se deja como estaba
                Settings.Load();
                int original = Settings.Current.AutoMemThreshold;
                int probe = original == 23 ? 24 : 23;
                try
                {
                    Settings.Current.AutoMemThreshold = probe;
                    Settings.Save();
                    Settings.Load();
                    Assert(Settings.Current.AutoMemThreshold == probe, "no se guardó");
                }
                finally
                {
                    Settings.Current.AutoMemThreshold = original;
                    Settings.Save();
                }
                Settings.Load();
                Assert(Settings.Current.AutoMemThreshold == original, "no se restauró");
                return "guardado, leído y restaurado";
            });

            W("Smoke: " + (_total - _fails) + "/" + _total + " OK");
            if (outFile != null) File.WriteAllText(outFile, Out.ToString(), Encoding.UTF8);
            try { Directory.Delete(dir, true); } catch { }
            return _fails == 0 ? 0 : 1;
        }
    }
}
