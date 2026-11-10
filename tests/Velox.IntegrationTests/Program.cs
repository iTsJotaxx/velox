using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using Velox.Core;
using Velox.Core.Backup;
using Velox.Core.Optimizations;
using Velox.Core.Scan;
using Velox.Core.Sys;
using Velox.Core.Tweaks;
using Velox.Native;

namespace Velox.IntegrationTests
{
    /// <summary>
    /// Para cada optimización disponible: foto del sistema → activar → desactivar (o al revés si ya estaba
    /// activa) → foto → compara. Usa una copia de seguridad temporal, nunca la real de %ProgramData%\Velox.
    /// Incluye los ajustes de «Personalizar Windows» (TweakCatalog). --tweaks prueba solo esos.
    /// Uso: Velox.IntegrationTests.exe [salida.txt] [--skip=Id1,Id2] [--tweaks]
    /// </summary>
    static class Program
    {
        static readonly StringBuilder Out = new StringBuilder();

        static void W(string s) { Console.WriteLine(s); Out.AppendLine(s); }

        [STAThread]
        static int Main(string[] args)
        {
            string outFile = args.FirstOrDefault(a => !a.StartsWith("--"));
            // Modo drivers: instala de verdad con el motor de la interfaz (ver DriversTest.cs)
            if (args.Contains("--drivers"))
                return DriversTest.Run(outFile, !args.Contains("--no-wu"), !args.Contains("--no-nvidia"),
                    args.FirstOrDefault(a => a.StartsWith("--only="))?.Substring(7),
                    args.FirstOrDefault(a => a.StartsWith("--rollback="))?.Substring(11));
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RefreshRate" };
            foreach (var a in args.Where(a => a.StartsWith("--skip=")))
                foreach (var id in a.Substring(7).Split(',')) skip.Add(id.Trim());

            int fails = 0;
            try
            {
                if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                {
                    W("ERROR: run as administrator.");
                    return 2;
                }

                string dir = Path.Combine(Path.GetTempPath(), "Velox-IntegrationTests");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                AppPaths.Initialize(dir);
                Log.Initialize();
                BackupStore.Load();

                var sys = SystemScanner.Scan(null);
                W("Optimizations: " + Catalog.All.Count + " | Windows tweaks: " + TweakCatalog.All.Count + " | laptop=" + sys.IsLaptop + " disk=" + sys.DiskType + " plan=" + sys.PowerScheme);

                var all = args.Contains("--tweaks") ? TweakCatalog.All.Select(t => t.Opt).ToList()
                                                    : Catalog.All.Concat(TweakCatalog.All.Select(t => t.Opt)).ToList();
                foreach (var o in all)
                {
                    if (skip.Contains(o.Id)) { W("[--] " + o.Id + ": skipped"); continue; }
                    if (!OptimizationEngine.IsAvailable(o, sys)) { W("[--] " + o.Id + ": not available on this PC"); continue; }

                    var before = Snapshot();
                    bool was = OptimizationEngine.IsApplied(o);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    string msg; bool ok;
                    try
                    {
                        if (!was)
                        {
                            OptimizationEngine.Apply(o, sys); bool mid = OptimizationEngine.IsApplied(o);
                            OptimizationEngine.Revert(o, sys); bool end = OptimizationEngine.IsApplied(o);
                            ok = mid && !end; msg = "on->" + mid + " off->" + end;
                        }
                        else
                        {
                            OptimizationEngine.Revert(o, sys); bool mid = OptimizationEngine.IsApplied(o);
                            OptimizationEngine.Apply(o, sys); bool end = OptimizationEngine.IsApplied(o);
                            ok = !mid && end; msg = "(was on) off->" + mid + " on->" + end;
                        }
                    }
                    catch (Exception ex) { ok = false; msg = "EXCEPTION: " + ex.Message; }
                    long ms = sw.ElapsedMilliseconds;

                    var after = Snapshot();
                    var diffs = before.Where(kv => !after.TryGetValue(kv.Key, out var v) || v != kv.Value)
                                      .Where(kv => !kv.Key.StartsWith("status "))
                                      .Select(kv => "    DIFF " + kv.Key + ": '" + kv.Value + "' -> '" + (after.TryGetValue(kv.Key, out var v2) ? v2 : "<missing>") + "'").ToList();
                    ok = ok && diffs.Count == 0;
                    if (!ok) fails++;
                    W((ok ? "[OK] " : "[XX] ") + o.Id.PadRight(18) + msg + " (" + ms + " ms)");
                    foreach (var d in diffs) W(d);
                }

                W("Failures: " + fails);
                W("Backups left in the test store: " + string.Join(", ", BackupStore.Ids()));
            }
            catch (Exception ex)
            {
                W("FATAL: " + ex);
                fails = 99;
            }
            if (outFile != null) File.WriteAllText(outFile, Out.ToString(), Encoding.UTF8);
            return fails == 0 ? 0 : 1;
        }

        static string RegStr(string path, string name)
        {
            var st = Reg.Get(path, name);
            if (!st.Existed) return "<none>";
            return st.Kind + ":" + (st.Value is string[] a ? string.Join("|", a) : st.Value is byte[] b ? Convert.ToBase64String(b) : Convert.ToString(st.Value));
        }

        /// <summary>Todo lo que alguna optimización puede tocar.</summary>
        static SortedDictionary<string, string> Snapshot()
        {
            var d = new SortedDictionary<string, string>();
            foreach (var o in Catalog.All.Concat(TweakCatalog.All.Select(t => t.Opt)))
                foreach (var it in o.Reg) d["reg " + it.Path + "|" + it.Name] = RegStr(it.Path, it.Name);
            foreach (var k in TweakCatalog.TrackedKeys)
            {
                d["key " + k] = Reg.KeyExists(k).ToString();
                d["reg " + k + "|(default)"] = RegStr(k, "");
            }

            var extra = new List<string[]>
            {
                new[] { @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings" },
                new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry" },
                new[] { @"HKCU\Control Panel\Accessibility\StickyKeys", "Flags" },
                new[] { @"HKCU\Control Panel\Accessibility\Keyboard Response", "Flags" },
                new[] { @"HKCU\Control Panel\Accessibility\ToggleKeys", "Flags" },
                new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests" },
                new[] { @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa" },
                new[] { @"HKCU\Control Panel\Desktop\WindowMetrics", "MinAnimate" },
                new[] { @"HKCU\Control Panel\Mouse", "MouseSpeed" },
                new[] { @"HKCU\Control Panel\Keyboard", "KeyboardDelay" },
                new[] { @"HKCU\Control Panel\Desktop", "MenuShowDelay" },
            };
            foreach (var k in CatalogHelpers.ActiveInterfaceKeys())
            {
                extra.Add(new[] { k, "TcpAckFrequency" });
                extra.Add(new[] { k, "TCPNoDelay" });
            }
            foreach (var e in extra) d["reg " + e[0] + "|" + e[1]] = RegStr(e[0], e[1]);

            string scheme = PowerCfg.ActiveScheme();
            d["power scheme"] = scheme;
            d["power schemes count"] = PowerCfg.ListSchemes().Count.ToString();
            foreach (var s in new[] { new[] { PowerCfg.SubProcessor, PowerCfg.CpMinCores }, new[] { PowerCfg.SubUsb, PowerCfg.UsbSelectiveSuspend }, new[] { PowerCfg.SubPciExpress, PowerCfg.PciAspm } })
            {
                var v = PowerCfg.Query(scheme, s[0], s[1]);
                d["power " + s[1]] = v == null ? "<none>" : v.AC + "/" + v.DC;
            }
            d["power hibernation"] = PowerCfg.HibernationEnabled().ToString();
            d["power overlay"] = PowerOverlay.Get() ?? "<none>";

            foreach (var name in new[] { "DiagTrack", "dmwappushservice", "SysMain", "WSearch" }.Concat(CatalogHelpers.UnusedServiceNames))
            {
                var i = Services.GetStartInfo(name);
                d["svc " + name] = i == null ? "<none>" : i.Start + "|" + RegStr(@"HKLM\SYSTEM\CurrentControlSet\Services\" + name, "DelayedAutostart");
                d["status " + name] = Services.StatusText(name);
            }
            foreach (var t in CatalogHelpers.TelemetryTasks)
                d["task " + t] = Convert.ToString(ScheduledTasks.IsEnabled(t)) ?? "<none>";

            d["spi clientAnim"] = Spi.GetClientAreaAnimation().ToString();
            foreach (var disp in Display.GetDisplays()) d["display " + disp.Device] = disp.CurrentHz.ToString();
            return d;
        }
    }
}
