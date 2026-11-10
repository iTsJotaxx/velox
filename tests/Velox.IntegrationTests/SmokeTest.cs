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
using Velox.Core.Sys;
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

        // ================================================================ Funciones de la 1.5

        static void RunV15(string dir, SystemInfo sys)
        {
            // ---------------------------------------------------------------- Exportar e importar en el mismo equipo: nada que cambiar
            Step("Perfil: exportar e importar aquí", () =>
            {
                var installed = Winget.IsAvailable ? Winget.GetInstalled() : new Dictionary<string, string>();
                var p = Velox.Core.Profiles.ProfileStore.Capture(sys, "smoke", installed);
                string f = Path.Combine(dir, "smoke.velox.json");
                Velox.Core.Profiles.ProfileStore.Save(p, f);
                var q = Velox.Core.Profiles.ProfileStore.Load(f);
                var plan = Velox.Core.Profiles.ProfileStore.Plan(q, sys, installed);
                Assert(plan.Changes.Count == 0, "importar lo exportado cambiaría: " + string.Join(", ", plan.Changes.Select(c => c.Opt.Id + "=" + c.Enable)));
                Assert(plan.AppsToInstall.Count == 0, "propondría instalar: " + string.Join(", ", plan.AppsToInstall.Select(a => a.Id)));
                return q.Optimizations.Count + " optimizaciones, " + q.Tweaks.Count + " ajustes, " + (q.Apps?.Count ?? 0) + " apps; 0 cambios al reimportar";
            });

            // ---------------------------------------------------------------- Ajuste revertido por «Windows» y vuelto a aplicar
            Step("Ajustes revertidos: detectar y reaplicar", () =>
            {
                var o = new[] { "MenuDelay", "Tips", "SearchWeb", "ActivityHistory", "DeliveryOpt" }
                    .Select(Velox.Core.Optimizations.OptimizationEngine.Find)
                    .FirstOrDefault(x => x != null && Velox.Core.Optimizations.OptimizationEngine.IsAvailable(x, sys) && !Velox.Core.Optimizations.OptimizationEngine.IsApplied(x));
                Assert(o != null, "no hay ningún ajuste inocuo sin aplicar para probar");
                try
                {
                    Velox.Core.Optimizations.OptimizationEngine.Apply(o, sys, "ui");
                    Assert(!Velox.Core.Optimizations.OptimizationEngine.Reverted(sys).Contains(o), "recién aplicado ya sale como revertido");
                    // «Windows Update» lo devuelve a su valor: se escriben los originales sin pasar por Velox
                    foreach (var rec in Velox.Core.Backup.BackupStore.Get(o.Id)) Velox.Core.Backup.Restorer.Restore(rec);
                    Assert(Velox.Core.Optimizations.OptimizationEngine.Reverted(sys).Contains(o), "no detecta que se ha revertido");
                    Velox.Core.Optimizations.OptimizationEngine.Apply(o, sys, "reapply");
                    Assert(Velox.Core.Optimizations.OptimizationEngine.IsApplied(o) && !Velox.Core.Optimizations.OptimizationEngine.Reverted(sys).Contains(o), "no se reaplicó");
                    var h = Velox.Core.Backup.History.All().Where(e => e.Id == o.Id).ToList();
                    Assert(h.Count >= 2 && h[0].Source == "reapply", "historial incompleto");
                }
                finally { Velox.Core.Optimizations.OptimizationEngine.Revert(o, sys, "ui"); }
                Assert(!Velox.Core.Optimizations.OptimizationEngine.IsApplied(o) && !Velox.Core.Backup.BackupStore.Has(o.Id), "no quedó como estaba");
                Assert(Velox.Core.Backup.History.LastAction(o.Id) == "off", "el historial no registra el deshacer");
                return o.Id + ": aplicado, revertido por fuera, detectado, reaplicado y deshecho";
            });

            // ---------------------------------------------------------------- DNS: cambiar, medir y volver (pase lo que pase)
            Step("DNS: cambiar, medir y volver", () =>
            {
                var before = DnsTool.ActiveAdapters();
                Assert(before.Count > 0, "sin adaptadores");
                var original = before.ToDictionary(a => a.Id, a => a.StaticServers);
                var cf = DnsTool.Providers.First(p => p.Id == "cloudflare");
                double? ms;
                try
                {
                    DnsTool.Set(cf, "ui");
                    var now = DnsTool.ActiveAdapters();
                    Assert(now.All(a => DnsTool.SplitServers(a.StaticServers).FirstOrDefault() == cf.Primary), "el registro no tiene 1.1.1.1");
                    Assert(DnsTool.CurrentProvider(now)?.Id == "cloudflare", "Windows no usa 1.1.1.1");
                    ms = DnsTool.Measure(cf.Primary);
                    Assert(System.Net.Dns.GetHostAddresses("microsoft.com").Length > 0, "no resuelve nombres con el DNS nuevo");
                }
                finally
                {
                    if (DnsTool.HasBackup) DnsTool.RestoreOriginal("ui");
                }
                var after = DnsTool.ActiveAdapters();
                foreach (var a in after)
                    Assert(original.TryGetValue(a.Id, out var o) && DnsTool.SplitServers(o).SequenceEqual(DnsTool.SplitServers(a.StaticServers)),
                           "el adaptador " + a.Name + " no volvió a su DNS (" + a.StaticServers + ")");
                Assert(System.Net.Dns.GetHostAddresses("github.com").Length > 0, "no resuelve nombres tras volver");
                return "Cloudflare " + (ms.HasValue ? ms.Value.ToString("0") + " ms" : "sin respuesta") + "; vuelto a " + DnsTool.Describe(original.Values.First());
            });
            Step("DNS: medir proveedores", () => string.Join(", ", DnsTool.Providers.Select(p => p.Name + " " + (DnsTool.Measure(p.Primary, 1)?.ToString("0") ?? "-") + " ms")));

            // ---------------------------------------------------------------- Limpieza profunda
            Step("Limpieza profunda: medir", () =>
            {
                var m = DeepCleaner.Categories().Select(DeepCleaner.Measure).ToList();
                bool reboot = Velox.Core.Drivers.DriverSafety.RebootPendingReason() != null;
                Assert(m.First(x => x.Category.Id == "wu").Blocked != null == reboot, "el bloqueo de Windows Update no sigue al reinicio pendiente");
                return string.Join(" · ", m.Select(x => x.Category.Id + " " + Format.Bytes(x.Bytes) + (x.Blocked != null ? " (bloqueado)" : "")));
            });
            Step("Limpieza profunda: temporales e informes", () =>
            {
                long freed = 0;
                foreach (var c in DeepCleaner.Categories().Where(c => c.Id == "temp" || c.Id == "reports")) freed += DeepCleaner.Clean(c);
                foreach (var s in new[] { "wuauserv", "bits", "DoSvc" }) Assert(Services.StatusText(s) != "StopPending", s + " se quedó parándose");
                return Format.Bytes(freed) + " liberados";
            });

            // ---------------------------------------------------------------- Informe de salud
            Step("Informe de salud", () =>
            {
                var disks = Velox.Core.Scan.HealthReport.ReadDiskHealth();
                var mem = MemoryTool.Snapshot();
                var items = Velox.Core.Scan.HealthReport.Build(new Velox.Core.Scan.HealthInputs
                {
                    DiskFreeGB = sys.DiskFreeGB, DiskSizeGB = sys.DiskSizeGB, DisksUnhealthy = disks.Item1, DisksTotal = disks.Item2,
                    RamAvailablePct = mem.AvailablePct, RamTotalGB = sys.RamTotalGB, UptimeDays = sys.UptimeDays, StartupApps = sys.StartupEnabled,
                    RebootPending = Velox.Core.Drivers.DriverSafety.RebootPendingReason() != null, DefenderRealtime = Velox.Core.Scan.HealthReport.ReadDefenderRealtime(),
                });
                Assert(items.Count >= 5, "faltan áreas");
                return string.Join(" · ", items.Select(i => i.Area + (i.Attention ? "!" : "")));
            });

            // ---------------------------------------------------------------- Actualizaciones de apps (con una de verdad)
            Step("Apps: lista de actualizaciones", () =>
            {
                var u = Winget.GetUpgrades();
                return u.Count + " (" + string.Join(", ", u.Select(x => x.Id + (x.Excluded ? " excluida" : x.SelectedByDefault ? "" : " desmarcada"))) + ")";
            });
            Step("Apps: actualizar 7-Zip de una versión vieja", () =>
            {
                string wg = Winget.Path;
                Assert(wg != null, "winget no disponible");
                bool hadIt = Winget.GetInstalled().ContainsKey("7zip.7zip");
                Assert(!hadIt, "7-Zip ya estaba instalado: no se toca");
                var inst = ProcessRunner.Run(wg, "install --id 7zip.7zip --exact --version 23.01 --silent --accept-package-agreements --accept-source-agreements --disable-interactivity --source winget", 600000);
                try
                {
                    Assert(inst.ExitCode == 0, "no se pudo instalar la versión vieja (0x" + inst.ExitCode.ToString("X8") + ")");
                    var up = Winget.GetUpgrades().FirstOrDefault(x => x.Id == "7zip.7zip");
                    Assert(up != null && up.SelectedByDefault, "no aparece como actualizable");
                    var res = Winget.Install(new AppEntry { Id = "7zip.7zip", Name = "7-Zip" }, true, null, CancellationToken.None);
                    Assert(res.Outcome == AppInstallOutcome.Updated, "resultado " + res.Outcome + " " + res.Error);
                    string ver = Winget.GetInstalled().TryGetValue("7zip.7zip", out var v) ? v : null;
                    Assert(ver != null && ver != "23.01", "sigue en " + ver);
                    return "23.01 → " + ver;
                }
                finally
                {
                    // Se deja como estaba: sin 7-Zip
                    ProcessRunner.Run(wg, "uninstall --id 7zip.7zip --exact --silent --accept-source-agreements --disable-interactivity", 300000);
                }
            });

            // ---------------------------------------------------------------- Modo sin ventana (el .exe de verdad)
            Step("Modo sin ventana: Velox abierto → código 3", () =>
            {
                using (var m = new Mutex(true, @"Global\Velox_SingleInstance", out bool created))
                {
                    Assert(created, "Velox está abierto: ciérralo para esta prueba");
                    int code = RunVelox("--apply-recommended");
                    Assert(code == 3, "código " + code);
                }
                return "código 3";
            });
            Step("Modo sin ventana: --import cambia y vuelve", () =>
            {
                var o = new[] { "MenuDelay", "Tips", "SearchWeb" }.Select(Velox.Core.Optimizations.OptimizationEngine.Find)
                    .First(x => x != null && Velox.Core.Optimizations.OptimizationEngine.IsAvailable(x, sys));
                bool was = Velox.Core.Optimizations.OptimizationEngine.IsApplied(o);
                string f1 = Path.Combine(dir, "flip.velox.json"), f2 = Path.Combine(dir, "back.velox.json");
                Velox.Core.Profiles.ProfileStore.Save(new Velox.Core.Profiles.VeloxProfile { Name = "flip", Optimizations = { { o.Id, !was } } }, f1);
                Velox.Core.Profiles.ProfileStore.Save(new Velox.Core.Profiles.VeloxProfile { Name = "back", Optimizations = { { o.Id, was } } }, f2);
                int c1 = RunVelox("--import=\"" + f1 + "\" --no-restore-point");
                bool mid = Velox.Core.Optimizations.OptimizationEngine.IsApplied(o);
                int c2 = RunVelox("--import=\"" + f2 + "\" --no-restore-point");
                bool end = Velox.Core.Optimizations.OptimizationEngine.IsApplied(o);
                Assert(c1 == 0 && mid == !was, "primera importación: código " + c1 + ", aplicado " + mid);
                Assert(c2 == 0 && end == was, "vuelta: código " + c2 + ", aplicado " + end);
                int bad = RunVelox("--import=\"" + Path.Combine(dir, "no-existe.velox.json") + "\"");
                Assert(bad == 2, "archivo inexistente: código " + bad);
                return o.Id + " " + was + " → " + mid + " → " + end + "; archivo inexistente → 2";
            });
        }

        /// <summary>Ejecuta el Velox.exe de esta carpeta sin ventana y devuelve su código de salida.</summary>
        static int RunVelox(string args)
        {
            string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Velox.exe");
            var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true });
            if (!p.WaitForExit(300000)) { try { p.Kill(); } catch { } return -1; }
            return p.ExitCode;
        }

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

            RunV15(dir, sys);

            W("Smoke: " + (_total - _fails) + "/" + _total + " OK");
            if (outFile != null) File.WriteAllText(outFile, Out.ToString(), Encoding.UTF8);
            try { Directory.Delete(dir, true); } catch { }
            return _fails == 0 ? 0 : 1;
        }
    }
}
