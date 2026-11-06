using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using Velox.Localization;

namespace Velox.Core.Drivers
{
    /// <summary>Descarga segura, verificación de firma e instalación silenciosa del driver de NVIDIA.</summary>
    internal static class NvidiaInstaller
    {
        const string SignerOrganization = "NVIDIA Corporation";
        const int MaxRedirects = 5;
        static readonly TimeSpan MaxInstallTime = TimeSpan.FromMinutes(60);

        static string DownloadsDir => Path.Combine(AppPaths.DataDir, "Downloads");

        /// <summary>Descarga, verifica e instala. Anota el resultado en result; solo deja pasar OperationCanceledException.</summary>
        internal static void Install(DriverUpdate update, DriverInstallResult result, IProgress<string> progress, CancellationToken ct)
        {
            var payload = update.Payload as NvidiaPayload;
            string path = null;
            bool keepFile = false;
            bool verified = false;
            try
            {
                if (payload == null) throw new InvalidOperationException(Loc.T("drv.reason.noPayload"));
                // Se revalida aquí aunque ya se hizo al buscar: es la última barrera antes de bajar nada
                var url = NvidiaSource.ValidateDownloadUri(payload.Url.AbsoluteUri, payload.Version);

                path = Download(url, payload, progress, ct);
                ct.ThrowIfCancellationRequested();

                // El archivo queda abierto (lectura compartida, sin escritura) desde que se verifica hasta que arranca: nadie puede cambiarlo en medio
                using (var guard = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    progress?.Report(Loc.T("drv.msg.verifying"));
                    string problem = VerifySignature(path);
                    if (problem != null)
                    {
                        guard.Dispose();
                        TryDelete(path);
                        path = null;
                        Fail(result, update, problem);
                        return;
                    }

                    verified = true;
                    ct.ThrowIfCancellationRequested();
                    progress?.Report(Loc.T("drv.msg.installingNvidia"));
                    Log.Info("NVIDIA: running installer silently: " + path);
                    int code = RunInstaller(path);
                    Log.Info("NVIDIA: installer exit code " + code + ".");

                    // El instalador de NVIDIA a veces devuelve un código de error por un paso final suyo (permisos al
                    // limpiar, componentes opcionales...) aunque el driver ya esté instalado. Lo que manda es el resultado real.
                    bool ok = code == 0 || code == 1;
                    if (!ok && InstalledVersionReached(update))
                    {
                        Log.Warn("NVIDIA: installer returned " + code + " but the driver " + update.NewVersion + " is installed; counting it as installed.");
                        ok = true;
                        result.RebootRequired = true; // por si algún componente necesita reiniciar
                    }

                    if (ok)
                    {
                        if (code == 1) result.RebootRequired = true;
                        result.Installed++;
                        result.Messages.Add(Loc.F("drv.msg.installed", update.Title, update.NewVersion));
                        keepFile = false;
                        // Cachés de shaders del driver anterior: pueden dar cierres de GPU en juegos (visto con Fortnite)
                        long freed = ShaderCache.Clear();
                        result.Messages.Add(Loc.F("drv.msg.shaderCache", (freed / (1024.0 * 1024.0 * 1024.0)).ToString("0.0")));
                    }
                    else
                    {
                        keepFile = true;
                        Fail(result, update, Loc.F("drv.msg.nvidiaExit", code, path));
                    }
                }
                if (!keepFile && path != null) { TryDelete(path); path = null; }
            }
            catch (OperationCanceledException)
            {
                if (path != null && !keepFile) TryDelete(path);
                throw;
            }
            catch (Exception ex)
            {
                Log.Error("NVIDIA install", ex);
                // Sin firma comprobada, el archivo se borra siempre. Ya verificado, se deja y se dice dónde para abrirlo a mano.
                if (path != null && !verified) { TryDelete(path); path = null; }
                string reason = path != null && File.Exists(path) ? ex.Message + " " + Loc.F("drv.msg.fileKept", path) : ex.Message;
                Fail(result, update, reason);
            }
        }

        // null si todo bien; si no, el motivo ya traducido
        static string VerifySignature(string path)
        {
            int rc = Authenticode.VerifyTrust(path);
            if (rc != 0)
            {
                Log.Error("NVIDIA: signature check failed (WinVerifyTrust 0x" + rc.ToString("X8") + "). The file will be deleted.");
                return Loc.T("drv.msg.signatureInvalid");
            }
            string subject = Authenticode.SignerSubject(path);
            if (!Authenticode.SubjectOrganizationIs(subject, SignerOrganization))
            {
                Log.Error("NVIDIA: unexpected signer '" + subject + "'. The file will be deleted.");
                return Loc.T("drv.msg.signerWrong");
            }
            Log.Info("NVIDIA: signature valid, signer: " + subject);
            return null;
        }

        /// <summary>¿El driver de la gráfica ya tiene (como mínimo) la versión que se quería instalar?</summary>
        static bool InstalledVersionReached(DriverUpdate update)
        {
            try
            {
                int? target = DriverRules.NvidiaWebToNumber(update.NewVersion);
                if (target == null || string.IsNullOrEmpty(update.DeviceId)) return false;
                string id = update.DeviceId.Replace("\\", "\\\\").Replace("'", "''");
                using (var s = new System.Management.ManagementObjectSearcher("SELECT DriverVersion FROM Win32_VideoController WHERE PNPDeviceID = '" + id + "'"))
                    foreach (System.Management.ManagementBaseObject o in s.Get())
                    {
                        int? now = DriverRules.NvidiaInstalledToNumber(Convert.ToString(o["DriverVersion"]));
                        if (now.HasValue && now.Value >= target.Value) return true;
                    }
            }
            catch (Exception ex) { Log.Warn("NVIDIA: could not verify the installed version: " + ex.Message); }
            return false;
        }

        static int RunInstaller(string path)
        {
            var psi = new ProcessStartInfo(path, "-s -noreboot -noeula")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(path),
            };
            using (var p = Process.Start(psi))
            {
                // Cancelar a mitad de un driver gráfico es peor que dejarlo acabar: aquí no se interrumpe
                if (!p.WaitForExit((int)MaxInstallTime.TotalMilliseconds))
                    throw new TimeoutException(Loc.T("drv.msg.nvidiaTimeout"));
                return p.ExitCode;
            }
        }

        /// <summary>Descarga el instalador a Downloads. Devuelve su ruta. Lanza si algo falla; no deja archivos a medias.</summary>
        static string Download(Uri url, NvidiaPayload payload, IProgress<string> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(DownloadsDir);
            string fileName = SafeFileName(url, payload.Version);
            string finalPath = Path.Combine(DownloadsDir, fileName);
            string partPath = finalPath + ".part";
            TryDelete(partPath);
            TryDelete(finalPath); // nunca se reutiliza un archivo anterior

            Uri current = url;
            for (int hop = 0; hop <= MaxRedirects; hop++)
            {
                ct.ThrowIfCancellationRequested();
                NvidiaSource.ValidateHost(current); // también en cada redirección
                var req = (HttpWebRequest)WebRequest.Create(current);
                req.UserAgent = DriverHttp.UserAgent;
                req.AllowAutoRedirect = false;
                req.Timeout = 30000;
                req.ReadWriteTimeout = 60000;
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    int status = (int)resp.StatusCode;
                    if (status >= 300 && status < 400)
                    {
                        string loc = resp.Headers["Location"];
                        Uri next;
                        if (string.IsNullOrEmpty(loc) || !Uri.TryCreate(current, loc, out next)) throw new WebException(Loc.T("drv.err.badResponse"));
                        current = next;
                        continue;
                    }
                    if (resp.StatusCode != HttpStatusCode.OK) throw new WebException("HTTP " + status);

                    long total = resp.ContentLength; // -1 si no lo dice
                    CheckDiskSpace(total);
                    try
                    {
                        CopyWithProgress(resp, partPath, total, payload.Title, progress, ct);
                        if (File.Exists(finalPath)) File.Delete(finalPath);
                        File.Move(partPath, finalPath);
                    }
                    catch
                    {
                        TryDelete(partPath);
                        throw;
                    }
                    Log.Info("NVIDIA: downloaded " + finalPath);
                    return finalPath;
                }
            }
            throw new WebException(Loc.T("drv.err.badResponse")); // demasiadas redirecciones
        }

        static void CopyWithProgress(HttpWebResponse resp, string partPath, long total, string title, IProgress<string> progress, CancellationToken ct)
        {
            const double MB = 1024.0 * 1024.0;
            long done = 0;
            var buf = new byte[256 * 1024];
            var sw = Stopwatch.StartNew();
            long lastReport = -1000;
            using (var rs = resp.GetResponseStream())
            using (var fs = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                int n;
                while ((n = rs.Read(buf, 0, buf.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    fs.Write(buf, 0, n);
                    done += n;
                    if (sw.ElapsedMilliseconds - lastReport >= 250)
                    {
                        lastReport = sw.ElapsedMilliseconds;
                        ReportProgress(progress, title, done, total, MB);
                    }
                }
                fs.Flush(true);
            }
            if (total >= 0 && done != total) throw new IOException(Loc.F("drv.err.incomplete", (done / MB).ToString("0"), (total / MB).ToString("0")));
            if (done == 0) throw new IOException(Loc.T("drv.err.badResponse"));
            ReportProgress(progress, title, done, total, MB);
        }

        static void ReportProgress(IProgress<string> progress, string title, long done, long total, double mb)
        {
            if (progress == null) return;
            int pct = total > 0 ? (int)(done * 100 / total) : 0;
            string size = total > 0 ? (done / mb).ToString("0") + " / " + (total / mb).ToString("0") + " MB" : (done / mb).ToString("0") + " MB";
            progress.Report(Loc.F("drv.msg.downloading", title, pct, size));
        }

        // El instalador se descomprime en TEMP: hace falta bastante más que el propio archivo
        static void CheckDiskSpace(long total)
        {
            try
            {
                if (total <= 0) return;
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(DownloadsDir)));
                long needed = total * 2;
                if (drive.AvailableFreeSpace < needed)
                    throw new IOException(Loc.F("drv.msg.noSpace", (needed / (1024.0 * 1024 * 1024)).ToString("0.0")));
            }
            catch (IOException) { throw; }
            catch { }
        }

        static string SafeFileName(Uri url, string version)
        {
            string name = Path.GetFileName(url.AbsolutePath);
            if (string.IsNullOrEmpty(name) || !Regex.IsMatch(name, @"^[A-Za-z0-9._-]{1,120}\.exe$", RegexOptions.IgnoreCase))
                name = "nvidia-driver-" + Regex.Replace(version ?? "x", @"[^0-9.]", "") + ".exe";
            return name;
        }

        static void Fail(DriverInstallResult r, DriverUpdate u, string reason)
        {
            Log.Error("NVIDIA: '" + u.Title + "' failed: " + reason);
            r.Failed++;
            r.Messages.Add(Loc.F("drv.msg.failed", u.Title, reason));
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { Log.Warn("Could not delete " + path + ": " + ex.Message); }
        }
    }
}
