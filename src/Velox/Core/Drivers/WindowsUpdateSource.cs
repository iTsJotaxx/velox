using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Velox.Localization;

namespace Velox.Core.Drivers
{
    /// <summary>Drivers de Windows Update mediante la API COM de WUA (siempre desde un hilo MTA).</summary>
    internal static class WindowsUpdateSource
    {
        const string SearchCriteria = "IsInstalled=0 and Type='Driver' and IsHidden=0";

        /// <summary>Ejecuta en un hilo MTA (los de ThreadPool lo son; si no, crea uno).</summary>
        internal static Task<T> RunMta<T>(Func<T> work)
        {
            return Task.Run(() =>
            {
                if (Thread.CurrentThread.GetApartmentState() == ApartmentState.MTA) return work();
                T value = default(T);
                Exception error = null;
                var t = new Thread(() => { try { value = work(); } catch (Exception ex) { error = ex; } });
                t.SetApartmentState(ApartmentState.MTA);
                t.IsBackground = true;
                t.Start();
                t.Join();
                if (error != null) throw error;
                return value;
            });
        }

        static dynamic NewSession()
        {
            dynamic session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session", true));
            session.ClientApplicationID = "Velox";
            return session;
        }

        // Lectura tolerante de propiedades COM: una que falle no tumba la búsqueda entera
        static string Str(Func<object> get) { try { var o = get(); return o == null ? null : Convert.ToString(o); } catch { return null; } }
        static double Num(Func<object> get) { try { return Convert.ToDouble(get()); } catch { return 0; } }
        static DateTime? Date(Func<object> get) { try { var o = get(); return o is DateTime ? (DateTime?)(DateTime)o : null; } catch { return null; } }

        /// <summary>Busca drivers y se queda solo con los que se pueden emparejar con un dispositivo instalado y son estrictamente más nuevos.</summary>
        internal static List<DriverUpdate> Search(List<DeviceDriver> installed)
        {
            Log.Info("Windows Update: searching for driver updates...");
            dynamic session = NewSession();
            dynamic searcher = session.CreateUpdateSearcher();
            searcher.ServerSelection = 2; // ssWindowsUpdate: nunca WSUS
            dynamic found = searcher.Search(SearchCriteria);
            int count = (int)found.Updates.Count;
            Log.Info("Windows Update: " + count + " driver update(s) reported.");

            var best = new Dictionary<string, DriverUpdate>(StringComparer.OrdinalIgnoreCase); // por dispositivo
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < count; i++)
            {
                dynamic u = found.Updates.Item(i);
                string title = Str(() => u.Title);
                try
                {
                    string cls = Str(() => u.DriverClass);
                    string model = Str(() => u.DriverModel);
                    string hwid = Str(() => u.DriverHardwareID);
                    string updateId = Str(() => u.Identity.UpdateID);
                    DateTime? newDate = Date(() => u.DriverVerDate);

                    if (DriverRules.IsFirmware(title, cls, model, hwid))
                    {
                        Log.Info("Windows Update: skipped firmware/BIOS update '" + title + "'.");
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(hwid) || string.IsNullOrWhiteSpace(updateId))
                    {
                        Log.Info("Windows Update: skipped '" + title + "' (no hardware id or update id).");
                        continue;
                    }
                    if (!ids.Add(updateId)) continue;

                    var matches = Match(installed, hwid.Trim());
                    if (matches.Count == 0)
                    {
                        Log.Info("Windows Update: skipped '" + title + "' (no installed device matches " + hwid + ").");
                        continue;
                    }
                    // Si hay varios dispositivos iguales, todos deben ser más antiguos
                    var dev = matches[0];
                    bool allOlder = matches.All(m => DriverRules.IsWuUpdateNewer(newDate, title, m.DriverDate, m.DriverVersion));
                    if (!allOlder)
                    {
                        Log.Info("Windows Update: skipped '" + title + "' (not newer than installed " + dev.DriverVersion + " " + dev.DateText + ").");
                        continue;
                    }

                    var ver = DriverRules.VersionFromTitle(title);
                    string notes = null;
                    try { if ((int)u.MoreInfoUrls.Count > 0) notes = Convert.ToString(u.MoreInfoUrls.Item(0)); } catch { }
                    if (notes != null && !notes.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !notes.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) notes = null;

                    var upd = new DriverUpdate
                    {
                        Key = "wu:" + updateId,
                        Source = DriverSource.WindowsUpdate,
                        Title = title,
                        DeviceName = dev.DeviceName,
                        DeviceId = dev.DeviceId,
                        Category = dev.Category,
                        CurrentVersion = dev.DriverVersion,
                        CurrentDate = dev.DriverDate,
                        NewVersion = ver != null ? ver.ToString() : "-",
                        NewDate = newDate,
                        SizeMB = Math.Round(Num(() => u.MaxDownloadSize) / (1024.0 * 1024.0), 1),
                        ReleaseNotesUrl = notes,
                        Payload = (object)u,
                    };

                    // Un solo update por dispositivo: el más nuevo (nunca instalar uno viejo encima de otro)
                    DriverUpdate prev;
                    if (best.TryGetValue(dev.DeviceId, out prev) && prev.NewDate.HasValue && newDate.HasValue && prev.NewDate.Value >= newDate.Value)
                    {
                        Log.Info("Windows Update: skipped '" + title + "' (a newer update for the same device is already listed).");
                        continue;
                    }
                    best[dev.DeviceId] = upd;
                    Log.Info("Windows Update: offering '" + title + "' for '" + dev.DeviceName + "' (" + dev.DriverVersion + " -> " + upd.NewVersion + ").");
                }
                catch (Exception ex)
                {
                    Log.Warn("Windows Update: skipped '" + title + "': " + ex.Message);
                }
            }
            return best.Values.ToList();
        }

        /// <summary>Dispositivos instalados que corresponden al hardware id: igual, o prefijo del DeviceID (dos primeras partes de la ruta) acabando en límite '&amp;'.</summary>
        internal static List<DeviceDriver> Match(List<DeviceDriver> installed, string hwid)
        {
            var exact = installed.Where(d => !string.IsNullOrEmpty(d.HardwareId) && string.Equals(d.HardwareId.Trim(), hwid, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count > 0) return exact;
            if (hwid.IndexOf('\\') < 0) return new List<DeviceDriver>(); // un id sin bus (p. ej. "PCI") es demasiado genérico
            // Solo fabricante («PCI\VEN_8086») encajaría con todos sus dispositivos: hace falta al menos fabricante y modelo
            if (hwid.IndexOf('&') < 0) return new List<DeviceDriver>();
            return installed.Where(d =>
            {
                var parts = (d.DeviceId ?? "").Split('\\');
                if (parts.Length < 2) return false;
                string head = parts[0] + "\\" + parts[1];
                if (!head.StartsWith(hwid, StringComparison.OrdinalIgnoreCase)) return false;
                return head.Length == hwid.Length || head[hwid.Length] == '&';
            }).ToList();
        }

        /// <summary>Descarga e instala en bloque. Anota en result lo que ocurra con cada driver; solo deja pasar OperationCanceledException.</summary>
        internal static void Install(IList<DriverUpdate> items, DriverInstallResult result, IProgress<string> progress, CancellationToken ct)
        {
            var usable = new List<DriverUpdate>();
            foreach (var u in items)
            {
                if (u.Payload == null) { Fail(result, u, Loc.T("drv.reason.noPayload")); continue; }
                dynamic iu = u.Payload;
                // Revisión de seguridad de nuevo justo antes de instalar
                if (DriverRules.IsFirmware(Str(() => iu.Title), Str(() => iu.DriverClass), Str(() => iu.DriverModel), Str(() => iu.DriverHardwareID)))
                {
                    Log.Warn("Windows Update: refused to install firmware-like update '" + u.Title + "'.");
                    Fail(result, u, Loc.T("drv.reason.firmware"));
                    continue;
                }
                bool already = false;
                try { already = (bool)iu.IsInstalled; } catch { }
                if (already)
                {
                    Log.Info("Windows Update: '" + u.Title + "' is already installed; skipped.");
                    result.Messages.Add(Loc.F("drv.msg.alreadyInstalled", u.Title));
                    continue;
                }
                usable.Add(u);
            }
            if (usable.Count == 0) return;

            var pending = new HashSet<DriverUpdate>(usable);
            try
            {
                ct.ThrowIfCancellationRequested();
                dynamic session = NewSession();
                dynamic coll = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl", true));
                foreach (var u in usable)
                {
                    dynamic iu = u.Payload;
                    try { if (!(bool)iu.EulaAccepted) iu.AcceptEula(); } catch (Exception ex) { Log.Warn("Windows Update: EULA for '" + u.Title + "': " + ex.Message); }
                    coll.Add(iu);
                }

                progress?.Report(Loc.F("drv.progress.wuDownload", usable.Count));
                Log.Info("Windows Update: downloading " + usable.Count + " driver(s)...");
                dynamic downloader = session.CreateUpdateDownloader();
                downloader.Updates = coll;
                dynamic dres = downloader.Download();

                dynamic icoll = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl", true));
                var toInstall = new List<DriverUpdate>();
                for (int i = 0; i < usable.Count; i++)
                {
                    int rc; int hr = 0;
                    try { dynamic r = dres.GetUpdateResult(i); rc = (int)r.ResultCode; try { hr = (int)r.HResult; } catch { } }
                    catch (Exception ex) { rc = -1; Log.Warn("Windows Update: no download result for '" + usable[i].Title + "': " + ex.Message); }
                    if (rc == 2 || rc == 3) { icoll.Add((dynamic)usable[i].Payload); toInstall.Add(usable[i]); }
                    else { pending.Remove(usable[i]); Fail(result, usable[i], Loc.F("drv.reason.downloadCode", rc, Hex(hr))); }
                }
                if (toInstall.Count == 0) return;

                // Una vez descargado, se instala: abortar un driver a medias es peor que terminarlo
                ct.ThrowIfCancellationRequested();
                progress?.Report(Loc.F("drv.progress.wuInstall", toInstall.Count));
                Log.Info("Windows Update: installing " + toInstall.Count + " driver(s)...");
                dynamic installer = session.CreateUpdateInstaller();
                installer.Updates = icoll;
                try { installer.AllowSourcePrompts = false; } catch { }
                try { installer.ForceQuiet = true; } catch { }
                dynamic ires = installer.Install();

                for (int i = 0; i < toInstall.Count; i++)
                {
                    var u = toInstall[i];
                    int rc; int hr = 0;
                    try { dynamic r = ires.GetUpdateResult(i); rc = (int)r.ResultCode; try { hr = (int)r.HResult; } catch { } }
                    catch (Exception ex) { rc = -1; Log.Warn("Windows Update: no install result for '" + u.Title + "': " + ex.Message); }
                    pending.Remove(u);
                    if (rc == 2 || rc == 3)
                    {
                        if (rc == 3) Log.Warn("Windows Update: '" + u.Title + "' installed with errors.");
                        else Log.Info("Windows Update: installed '" + u.Title + "'.");
                        result.Installed++;
                        result.Messages.Add(Loc.F("drv.msg.installed", u.Title, u.NewVersion));
                    }
                    else Fail(result, u, Loc.F("drv.reason.installCode", rc, Hex(hr)));
                }
                try { if ((bool)ires.RebootRequired) result.RebootRequired = true; } catch { }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Error("Windows Update install", ex);
                foreach (var u in pending) Fail(result, u, ex.Message);
            }
        }

        static void Fail(DriverInstallResult r, DriverUpdate u, string reason)
        {
            Log.Error("Windows Update: '" + u.Title + "' failed: " + reason);
            r.Failed++;
            r.Messages.Add(Loc.F("drv.msg.failed", u.Title, reason));
        }

        static string Hex(int hr) => "0x" + hr.ToString("X8");
    }
}
