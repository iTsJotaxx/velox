using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using Velox.Core.Tools;
using Velox.Localization;

namespace Velox.Core.Drivers
{
    /// <summary>Orquestación de la búsqueda e instalación. Las firmas públicas están en DriverUpdater.</summary>
    internal static class DriverEngine
    {
        static readonly string[] CategoryOrder = { "gpu", "network", "audio", "bluetooth", "storage", "chipset", "input", "usb", "other" };
        static int _installing;

        sealed class Part
        {
            public List<DriverUpdate> Updates = new List<DriverUpdate>();
            public List<string> Warnings = new List<string>();
        }

        // ---------- Búsqueda ----------

        internal static async Task<List<DriverUpdate>> SearchAsync(IProgress<string> progress)
        {
            // Las dos fuentes van por separado y en paralelo: si una falla, la otra sigue
            var wu = WindowsUpdateSource.RunMta(() => SearchWindowsUpdate(progress));
            var nv = Task.Run(() => SearchNvidia(progress));
            var parts = await Task.WhenAll(wu, nv).ConfigureAwait(false);

            var all = new List<DriverUpdate>();
            foreach (var p in parts)
            {
                all.AddRange(p.Updates);
                DriverUpdater.LastSearchWarnings.AddRange(p.Warnings);
            }
            RemoveDuplicateGpuOffers(all);
            Log.Info("Driver search finished: " + all.Count + " update(s), " + DriverUpdater.LastSearchWarnings.Count + " warning(s).");
            return all
                .OrderBy(u => Array.IndexOf(CategoryOrder, u.Category))
                .ThenBy(u => u.DeviceName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Windows Update y NVIDIA pueden ofrecer driver para la misma gráfica. Instalar los dos seguidos podría dejar
        /// el más viejo encima del nuevo: se queda solo el de versión mayor (a igualdad, el de NVIDIA).
        /// </summary>
        internal static void RemoveDuplicateGpuOffers(List<DriverUpdate> all)
        {
            foreach (var nv in all.Where(u => u.Source == DriverSource.Nvidia).ToList())
            {
                int? nvNum = DriverRules.NvidiaWebToNumber(nv.NewVersion);
                foreach (var wu in all.Where(u => u.Source == DriverSource.WindowsUpdate &&
                                                  string.Equals(u.DeviceId, nv.DeviceId, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    // La versión de Windows Update viene en formato Windows (32.0.15.6094 = 560.94)
                    int? wuNum = DriverRules.NvidiaInstalledToNumber(wu.NewVersion);
                    if (wuNum.HasValue && nvNum.HasValue && wuNum.Value > nvNum.Value)
                    {
                        all.Remove(nv);
                        Log.Info("Driver search: '" + nv.Title + "' dropped, Windows Update offers a newer one (" + wu.NewVersion + ").");
                        break;
                    }
                    all.Remove(wu);
                    Log.Info("Driver search: Windows Update '" + wu.Title + "' dropped, NVIDIA offers " + nv.NewVersion + " for the same GPU.");
                }
            }
        }

        static Part SearchWindowsUpdate(IProgress<string> progress)
        {
            var part = new Part();
            try
            {
                progress?.Report(Loc.T("drv.progress.searchWu"));
                var installed = PnpReader.ReadAll();
                part.Updates.AddRange(WindowsUpdateSource.Search(installed));
            }
            catch (Exception ex)
            {
                Log.Error("Windows Update search", ex);
                part.Updates.Clear();
                part.Warnings.Add(Loc.F("drv.warn.wuFailed", ex.Message));
            }
            return part;
        }

        static Part SearchNvidia(IProgress<string> progress)
        {
            var part = new Part();
            try
            {
                progress?.Report(Loc.T("drv.progress.searchNvidia"));
                part.Updates.AddRange(NvidiaSource.Search(part.Warnings));
            }
            catch (Exception ex)
            {
                Log.Error("NVIDIA search", ex);
                part.Updates.Clear();
                part.Warnings.Add(Loc.F("drv.warn.nvidiaFailed", ex.Message));
            }
            return part;
        }

        // ---------- Instalación ----------

        internal static async Task<DriverInstallResult> InstallAsync(IList<DriverUpdate> updates, IProgress<string> progress, CancellationToken ct)
        {
            var result = new DriverInstallResult();
            if (Interlocked.CompareExchange(ref _installing, 1, 0) != 0)
            {
                Log.Warn("Driver install ignored: another installation is running.");
                result.Messages.Add(Loc.T("drv.msg.busy"));
                return result;
            }
            try
            {
                // Solo lo marcado, y solo de las fuentes conocidas
                var selected = (updates ?? new List<DriverUpdate>()).Where(u => u != null && u.Selected).ToList();
                if (selected.Count == 0) return result;

                // Última barrera (la interfaz ya lo comprueba antes): reinicio pendiente o portátil con batería
                var blockers = DriverSafety.Blockers();
                if (blockers.Count > 0)
                {
                    Log.Warn("Driver install blocked: " + string.Join(" | ", blockers));
                    result.Blocked = true;
                    result.Messages.AddRange(blockers);
                    return result;
                }
                Log.Info("Driver install started: " + selected.Count + " update(s).");

                ct.ThrowIfCancellationRequested();
                progress?.Report(Loc.T("drv.progress.restorePoint"));
                try
                {
                    var rp = await RestorePointTool.CreateAsync("Velox - drivers").ConfigureAwait(false);
                    if (rp.Result == RestorePointResult.Failed)
                    {
                        Log.Warn("Driver install continues without restore point.");
                        result.Messages.Add(Loc.T("drv.msg.restorePointFailed"));
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Restore point before drivers", ex);
                    result.Messages.Add(Loc.T("drv.msg.restorePointFailed"));
                }

                // 1) Windows Update, en bloque
                ct.ThrowIfCancellationRequested();
                var wu = selected.Where(u => u.Source == DriverSource.WindowsUpdate).ToList();
                if (wu.Count > 0)
                    await WindowsUpdateSource.RunMta<object>(() => { WindowsUpdateSource.Install(wu, result, progress, ct); return null; }).ConfigureAwait(false);

                // 2) NVIDIA, uno a uno
                foreach (var nv in selected.Where(u => u.Source == DriverSource.Nvidia))
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Run(() => NvidiaInstaller.Install(nv, result, progress, ct)).ConfigureAwait(false);
                }
                Log.Info("Driver install finished: " + result.Installed + " installed, " + result.Failed + " failed.");
            }
            catch (OperationCanceledException)
            {
                Log.Warn("Driver install cancelled.");
                result.Messages.Add(Loc.T("drv.msg.cancelled"));
            }
            catch (Exception ex)
            {
                Log.Error("Driver install", ex);
                result.Failed++;
                result.Messages.Add(Loc.F("drv.msg.failed", "-", ex.Message));
            }
            finally
            {
                if (result.RebootRequired) DriverSafety.SessionRebootPending = true;
                Interlocked.Exchange(ref _installing, 0);
            }
            return result;
        }

        // ---------- Otras gráficas ----------

        internal static List<GpuVendorTool> OtherGpuVendors()
        {
            var list = new List<GpuVendorTool>();
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, PNPDeviceID, DriverVersion FROM Win32_VideoController"))
                using (var res = s.Get())
                    foreach (ManagementBaseObject o in res)
                        using (o)
                        {
                            string name = (o["Name"] as string ?? "").Trim();
                            string pnp = o["PNPDeviceID"] as string ?? "";
                            // Adaptadores virtuales: ni mirarlos
                            if (pnp.StartsWith(@"ROOT\", StringComparison.OrdinalIgnoreCase)) continue;
                            if (name.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                            if (name.IndexOf("Microsoft Basic", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                            if (!pnp.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase)) continue;

                            string vendor, url;
                            if (pnp.IndexOf("VEN_1002", StringComparison.OrdinalIgnoreCase) >= 0)
                            { vendor = "AMD"; url = "https://www.amd.com/en/support/download/drivers.html"; }
                            else if (pnp.IndexOf("VEN_8086", StringComparison.OrdinalIgnoreCase) >= 0)
                            { vendor = "Intel"; url = "https://www.intel.com/content/www/us/en/support/detect.html"; }
                            else continue;

                            list.Add(new GpuVendorTool { Vendor = vendor, GpuName = name, DriverVersion = o["DriverVersion"] as string, ToolUrl = url });
                        }
            }
            catch (Exception ex) { Log.Warn("GPU vendor detection: " + ex.Message); }
            return list;
        }
    }
}
