using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Velox.Core.Drivers;
using Velox.Localization;

namespace Velox.Core.Update
{
    public sealed class UpdateInfo
    {
        public string Tag { get; set; }
        public Version Latest { get; set; }
        public bool IsNewer { get; set; }
        public string Notes { get; set; }
        public string PageUrl { get; set; }
        public string ExeUrl { get; set; }
        public string ShaUrl { get; set; }
        /// <summary>SHA-256 que GitHub publica en la API para el .exe (si lo da). Debe coincidir con el del archivo .sha256.</summary>
        public string ApiDigest { get; set; }
    }

    /// <summary>
    /// Actualización de Velox desde las releases oficiales de GitHub. Solo se acepta el repositorio de Velox, se comprueba
    /// el SHA-256 y que el archivo sea realmente Velox con una versión mayor. El .exe en uso se renombra (Windows lo permite)
    /// y la versión nueva ocupa su sitio.
    /// </summary>
    public static class Updater
    {
        const string Repo = "iTsJotaxx/velox";
        const string ApiUrl = "https://api.github.com/repos/" + Repo + "/releases/latest";
        const string DownloadPrefix = "https://github.com/" + Repo + "/releases/download/";
        const long MaxExeBytes = 60L * 1024 * 1024;
        const long MinExeBytes = 100L * 1024;
        const string ExeAsset = "Velox.exe";
        const string ShaAsset = "Velox.exe.sha256";

        static HttpWebRequest NewRequest(string url, string accept)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "Velox/" + AppInfo.Version;
            r.Accept = accept;
            r.Timeout = 20000;
            r.ReadWriteTimeout = 30000;
            return r;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : null;
        }

        static Version Normalize(Version v) => new Version(v.Major, v.Minor, Math.Max(v.Build, 0));

        /// <summary>Consulta la última release publicada. Lanza InvalidOperationException con un mensaje ya traducido.</summary>
        public static UpdateInfo Check()
        {
            string json;
            try
            {
                using (var resp = (HttpWebResponse)NewRequest(ApiUrl, "application/vnd.github+json").GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    json = sr.ReadToEnd();
            }
            catch (WebException ex)
            {
                var hr = ex.Response as HttpWebResponse;
                if (hr != null && hr.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException(Loc.T("ui.upd.err.noRelease"));
                throw new InvalidOperationException(Loc.F("ui.upd.err.network", ex.Message));
            }

            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            string tag = Str(root, "tag_name");
            Version latest;
            if (string.IsNullOrEmpty(tag) || !Version.TryParse(tag.TrimStart('v', 'V'), out latest))
                throw new InvalidOperationException(Loc.T("ui.upd.err.badRelease"));

            var info = new UpdateInfo
            {
                Tag = tag,
                Latest = Normalize(latest),
                Notes = Str(root, "body") ?? "",
                PageUrl = Str(root, "html_url"),
            };
            info.IsNewer = info.Latest > Normalize(Version.Parse(AppInfo.Version));

            var assets = root != null && root.ContainsKey("assets") ? root["assets"] as IEnumerable : null;
            if (assets != null)
            {
                foreach (var a in assets)
                {
                    var d = a as Dictionary<string, object>;
                    string name = Str(d, "name"), url = Str(d, "browser_download_url");
                    if (name == ExeAsset)
                    {
                        info.ExeUrl = url;
                        string dig = Str(d, "digest");
                        if (dig != null && dig.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) info.ApiDigest = dig.Substring(7).ToLowerInvariant();
                    }
                    else if (name == ShaAsset) info.ShaUrl = url;
                }
            }
            if (info.IsNewer && (info.ExeUrl == null || info.ShaUrl == null)) throw new InvalidOperationException(Loc.T("ui.upd.err.badRelease"));
            Log.Info("Update check: latest " + info.Tag + ", installed " + AppInfo.Version + (info.IsNewer ? " (newer available)." : " (up to date)."));
            return info;
        }

        static void RequireOfficialUrl(string url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(Loc.T("ui.upd.err.badUrl"));
        }

        static void RequireOfficialHost(Uri u)
        {
            string host = u.Host.ToLowerInvariant();
            bool ok = u.Scheme == Uri.UriSchemeHttps && (host == "github.com" || host.EndsWith(".github.com") || host.EndsWith(".githubusercontent.com"));
            if (!ok) throw new InvalidOperationException(Loc.T("ui.upd.err.badUrl"));
        }

        static string ReadExpectedSha(string url)
        {
            using (var resp = (HttpWebResponse)NewRequest(url, "application/octet-stream").GetResponse())
            {
                RequireOfficialHost(resp.ResponseUri);
                if (resp.ContentLength > 4096) throw new InvalidOperationException(Loc.T("ui.upd.err.hash"));
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.ASCII))
                {
                    var m = Regex.Match(sr.ReadToEnd().Trim(), @"^[0-9a-fA-F]{64}");
                    if (!m.Success) throw new InvalidOperationException(Loc.T("ui.upd.err.hash"));
                    return m.Value.ToLowerInvariant();
                }
            }
        }

        static string DownloadFile(UpdateInfo info, string part, IProgress<string> progress, CancellationToken ct)
        {
            using (var resp = (HttpWebResponse)NewRequest(info.ExeUrl, "application/octet-stream").GetResponse())
            {
                RequireOfficialHost(resp.ResponseUri); // también tras las redirecciones de GitHub
                long total = resp.ContentLength;
                if (total > MaxExeBytes) throw new InvalidOperationException(Loc.T("ui.upd.err.size"));
                using (var s = resp.GetResponseStream())
                using (var fs = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var sha = SHA256.Create())
                {
                    var buf = new byte[81920];
                    long done = 0; int last = -1, n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        fs.Write(buf, 0, n);
                        sha.TransformBlock(buf, 0, n, null, 0);
                        done += n;
                        if (done > MaxExeBytes) throw new InvalidOperationException(Loc.T("ui.upd.err.size"));
                        if (total > 0)
                        {
                            int pct = (int)(done * 100 / total);
                            if (pct != last) { last = pct; progress?.Report(Loc.F("ui.upd.downloading", info.Tag, pct)); }
                        }
                    }
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    if (total > 0 && done != total) throw new InvalidOperationException(Loc.T("ui.upd.err.size"));
                    return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }

        /// <summary>Descarga y verifica la versión nueva. Devuelve la ruta del .exe ya comprobado. Lanza con mensaje traducido.</summary>
        public static string DownloadAndVerify(UpdateInfo info, IProgress<string> progress, CancellationToken ct)
        {
            if (info == null || !info.IsNewer) throw new InvalidOperationException(Loc.T("ui.upd.err.notNewer"));
            RequireOfficialUrl(info.ExeUrl);
            RequireOfficialUrl(info.ShaUrl);

            string expected;
            try { expected = ReadExpectedSha(info.ShaUrl); }
            catch (WebException ex) { throw new InvalidOperationException(Loc.F("ui.upd.err.network", ex.Message)); }
            if (!string.IsNullOrEmpty(info.ApiDigest) && !string.Equals(info.ApiDigest, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(Loc.T("ui.upd.err.hash"));

            string dir = Path.Combine(AppPaths.DataDir, "Downloads");
            Directory.CreateDirectory(dir);
            string part = Path.Combine(dir, "Velox-" + info.Latest + ".exe.part");
            string final = Path.Combine(dir, "Velox-" + info.Latest + ".exe");
            try
            {
                string actual;
                try { actual = DownloadFile(info, part, progress, ct); }
                catch (WebException ex) { throw new InvalidOperationException(Loc.F("ui.upd.err.network", ex.Message)); }

                progress?.Report(Loc.T("ui.upd.verifying"));
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(Loc.T("ui.upd.err.hash"));

                var fi = new FileInfo(part);
                if (fi.Length < MinExeBytes || fi.Length > MaxExeBytes) throw new InvalidOperationException(Loc.T("ui.upd.err.size"));
                var head = new byte[2];
                using (var fs = File.OpenRead(part)) fs.Read(head, 0, 2);
                if (head[0] != (byte)'M' || head[1] != (byte)'Z') throw new InvalidOperationException(Loc.T("ui.upd.err.identity"));

                // Debe ser Velox de verdad y exactamente la versión anunciada (y más nueva que la instalada)
                AssemblyName an;
                try { an = AssemblyName.GetAssemblyName(part); }
                catch (Exception) { throw new InvalidOperationException(Loc.T("ui.upd.err.identity")); }
                if (an.Name != "Velox" || Normalize(an.Version) != info.Latest || Normalize(an.Version) <= Normalize(Version.Parse(AppInfo.Version)))
                    throw new InvalidOperationException(Loc.T("ui.upd.err.identity"));

                if (File.Exists(final)) File.Delete(final);
                File.Move(part, final);
                Log.Info("Update " + info.Tag + " downloaded and verified (SHA-256 " + actual + ").");
                return final;
            }
            catch
            {
                try { if (File.Exists(part)) File.Delete(part); } catch { }
                throw;
            }
        }

        static string CurrentExePath()
        {
            return (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).Location;
        }

        /// <summary>
        /// Coloca la versión nueva en lugar de la actual y la arranca. El .exe en uso se renombra a «.old» (Windows lo permite);
        /// la instancia nueva espera a que esta se cierre. Quien llama debe cerrar Velox justo después.
        /// </summary>
        public static void ApplyAndRestart(string stagedExe)
        {
            string cur = CurrentExePath();
            string neu = cur + ".new";
            string old = cur + ".old";
            try
            {
                File.Copy(stagedExe, neu, true);
                if (File.Exists(old))
                {
                    try { File.Delete(old); } catch { old = cur + ".old" + DateTime.Now.Ticks; }
                }
                File.Move(cur, old);
                try { File.Move(neu, cur); }
                catch
                {
                    try { File.Move(old, cur); } catch { } // deja todo como estaba
                    throw;
                }
            }
            catch (Exception ex)
            {
                try { if (File.Exists(neu)) File.Delete(neu); } catch { }
                throw new InvalidOperationException(Loc.F("ui.upd.err.replace", ex.Message));
            }

            Log.Info("Update applied; restarting Velox.");
            try { File.Delete(stagedExe); } catch { }
            try
            {
                // Velox ya va con permisos de administrador y la versión nueva los hereda sin pedir nada
                Process.Start(new ProcessStartInfo(cur, "--after-update") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(cur) });
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 740)
            {
                try { Process.Start(new ProcessStartInfo(cur, "--after-update") { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(cur) }); }
                catch (Exception ex2) { throw new InvalidOperationException(Loc.F("ui.upd.err.restart", ex2.Message)); }
            }
            catch (Exception ex)
            {
                // El reemplazo ya está hecho: lo único que falta es abrir Velox a mano
                throw new InvalidOperationException(Loc.F("ui.upd.err.restart", ex.Message));
            }
        }

        /// <summary>Borra los restos de una actualización anterior (.old, .new). No falla nunca.</summary>
        public static void CleanupOld()
        {
            try
            {
                string cur = CurrentExePath();
                string dir = Path.GetDirectoryName(cur), name = Path.GetFileName(cur);
                foreach (var pattern in new[] { name + ".old*", name + ".new" })
                    foreach (var f in Directory.GetFiles(dir, pattern))
                        try { File.Delete(f); } catch { }
            }
            catch { }
        }
    }
}
