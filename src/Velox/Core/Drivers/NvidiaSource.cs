using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Velox.Localization;

namespace Velox.Core.Drivers
{
    /// <summary>Datos internos de una actualización NVIDIA (van en DriverUpdate.Payload).</summary>
    internal sealed class NvidiaPayload
    {
        public Uri Url { get; set; }
        public string Version { get; set; }
        public string Title { get; set; }
    }

    /// <summary>Búsqueda de drivers GeForce con la API oficial de la web de NVIDIA.</summary>
    internal static class NvidiaSource
    {
        const string LookupUrl = "https://www.nvidia.com/Download/API/lookupValueSearch.aspx";
        const string DriverServiceUrl = "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php";
        const string GeForceParentId = "1";
        const string AllowedHostSuffix = ".download.nvidia.com";

        internal sealed class GpuInfo
        {
            public string Name, PnpId, DriverVersion;
            public DateTime? DriverDate;
        }

        sealed class Product { public string Psid, SeriesName, Pfid, Name; }

        /// <summary>GPU NVIDIA reales (se ignoran los adaptadores virtuales ROOT\).</summary>
        internal static List<GpuInfo> GetGpus()
        {
            var list = new List<GpuInfo>();
            using (var s = new ManagementObjectSearcher("SELECT Name, PNPDeviceID, DriverVersion, DriverDate FROM Win32_VideoController"))
            using (var res = s.Get())
                foreach (ManagementBaseObject o in res)
                    using (o)
                    {
                        string pnp = o["PNPDeviceID"] as string ?? "";
                        if (!pnp.StartsWith(@"PCI\VEN_10DE", StringComparison.OrdinalIgnoreCase)) continue;
                        list.Add(new GpuInfo
                        {
                            Name = (o["Name"] as string ?? "").Trim(),
                            PnpId = pnp,
                            DriverVersion = o["DriverVersion"] as string,
                            DriverDate = PnpReader.ParseCimDate(o["DriverDate"] as string),
                        });
                    }
            return list;
        }

        /// <summary>Busca la última versión para cada GPU NVIDIA. Los avisos (sin coincidencia exacta...) van a warnings; los fallos se lanzan.</summary>
        internal static List<DriverUpdate> Search(List<string> warnings)
        {
            var res = new List<DriverUpdate>();
            var gpus = GetGpus();
            if (gpus.Count == 0) { Log.Info("NVIDIA: no NVIDIA GPU detected."); return res; }
            if (!Environment.Is64BitOperatingSystem) { Log.Info("NVIDIA: 32-bit Windows is not supported."); return res; }

            int osId = OsId();
            List<Product> products = null; // se carga solo si hace falta, y entero: una serie que falle invalida la búsqueda
            var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var gpu in gpus)
            {
                Log.Info("NVIDIA: GPU '" + gpu.Name + "' driver " + gpu.DriverVersion + ".");

                int? installed = DriverRules.NvidiaInstalledToNumber(gpu.DriverVersion);
                if (installed == null)
                {
                    Log.Warn("NVIDIA: cannot read installed version '" + gpu.DriverVersion + "'.");
                    warnings.Add(Loc.F("drv.warn.nvidiaVersionUnreadable", gpu.Name));
                    continue;
                }

                if (products == null) products = LoadGeForceProducts();
                string wanted = Normalize(gpu.Name);
                var matches = products.Where(p => Normalize(p.Name) == wanted && wanted.Length > 0).ToList();
                if (matches.Count != 1)
                {
                    Log.Warn("NVIDIA: " + matches.Count + " exact catalog matches for '" + gpu.Name + "'; nothing offered.");
                    warnings.Add(Loc.F("drv.warn.nvidiaNoExactMatch", gpu.Name));
                    continue;
                }
                var prod = matches[0];
                Log.Info("NVIDIA: catalog match '" + prod.Name + "' (series " + prod.SeriesName + ", psid " + prod.Psid + ", pfid " + prod.Pfid + ").");

                var upd = LookupLatest(prod, osId, gpu, installed.Value);
                if (upd == null) continue;
                var payload = (NvidiaPayload)upd.Payload;
                if (!urls.Add(payload.Url.AbsoluteUri)) continue; // dos GPU con el mismo paquete: una sola oferta
                res.Add(upd);
            }
            return res;
        }

        static DriverUpdate LookupLatest(Product prod, int osId, GpuInfo gpu, int installedNumber)
        {
            string url = DriverServiceUrl + "?func=DriverManualLookup&psid=" + prod.Psid + "&pfid=" + prod.Pfid + "&osID=" + osId +
                "&languageCode=1033&beta=0&isWHQL=1&dltype=-1&dch=1&upCRD=0&qnf=0&sort1=0&numberOfResults=1";
            string json = DriverHttp.GetString(url);

            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            var ids = root != null && root.ContainsKey("IDS") ? root["IDS"] as IEnumerable : null;
            object first = null;
            if (ids != null) foreach (var x in ids) { first = x; break; }
            if (first == null)
            {
                Log.Info("NVIDIA: the service returned no driver for this GPU.");
                return null;
            }
            var info = Sub(first as Dictionary<string, object>, "downloadInfo");
            if (info == null) throw new InvalidDataException(Loc.T("drv.err.badResponse"));

            string version = Text(info, "Version");
            int? newNumber = DriverRules.NvidiaWebToNumber(version);
            if (newNumber == null) throw new InvalidDataException(Loc.T("drv.err.badResponse"));
            if (Text(info, "IsWHQL") != "1")
            {
                Log.Warn("NVIDIA: driver " + version + " is not WHQL; skipped.");
                return null;
            }

            // Solo se ofrece una versión estrictamente mayor
            if (newNumber.Value <= installedNumber)
            {
                Log.Info("NVIDIA: installed " + DriverRules.NvidiaNumberToText(installedNumber) + " is up to date (latest " + version + ").");
                return null;
            }

            Uri uri = ValidateDownloadUri(Text(info, "DownloadURL"), version); // lanza si no es de NVIDIA
            string name = Text(info, "Name");
            try { name = Uri.UnescapeDataString((name ?? "").Replace('+', ' ')); } catch { }
            if (string.IsNullOrWhiteSpace(name)) name = "NVIDIA Driver";
            string title = name.Trim() + " " + version;

            string details = Text(info, "DetailsURL");
            if (!IsSafeNvidiaPage(details)) details = null;

            Log.Info("NVIDIA: offering " + version + " (installed " + DriverRules.NvidiaNumberToText(installedNumber) + ").");
            return new DriverUpdate
            {
                Key = "nvidia:" + version,
                Source = DriverSource.Nvidia,
                Title = title,
                DeviceName = gpu.Name,
                DeviceId = gpu.PnpId,
                Category = "gpu",
                CurrentVersion = DriverRules.NvidiaNumberToText(installedNumber),
                CurrentDate = gpu.DriverDate,
                NewVersion = version,
                NewDate = ParseReleaseDate(Text(info, "ReleaseDateTime")),
                SizeMB = ParseSizeMB(Text(info, "DownloadURLFileSize")),
                ReleaseNotesUrl = details,
                Payload = new NvidiaPayload { Url = uri, Version = version, Title = title },
            };
        }

        /// <summary>Solo https, puerto 443, sin credenciales, host *.download.nvidia.com y un .exe de esa versión. Lanza si no cumple.</summary>
        internal static Uri ValidateDownloadUri(string url, string version)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) throw new InvalidDataException(Loc.T("drv.err.badResponse"));
            ValidateHost(u);
            if (!u.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(Loc.T("drv.err.badResponse"));
            if (!string.IsNullOrEmpty(version) && u.AbsolutePath.IndexOf("/" + version + "/", StringComparison.Ordinal) < 0)
                throw new InvalidDataException(Loc.T("drv.err.badResponse"));
            return u;
        }

        /// <summary>Comprobación de host aplicada a la URL inicial y a cada redirección.</summary>
        internal static void ValidateHost(Uri u)
        {
            string host = u.Host.ToLowerInvariant();
            bool ok = u.Scheme == Uri.UriSchemeHttps
                && u.IsDefaultPort
                && string.IsNullOrEmpty(u.UserInfo)
                && host.Length > AllowedHostSuffix.Length
                && host.EndsWith(AllowedHostSuffix, StringComparison.Ordinal)
                && Regex.IsMatch(host, @"^[a-z0-9.-]+$");
            if (!ok)
            {
                Log.Error("NVIDIA: rejected download URL host '" + u.Host + "' (" + u.Scheme + ").");
                throw new InvalidDataException(Loc.F("drv.err.badHost", u.Host));
            }
        }

        static bool IsSafeNvidiaPage(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u) || u.Scheme != Uri.UriSchemeHttps) return false;
            string h = u.Host.ToLowerInvariant();
            return h == "nvidia.com" || h.EndsWith(".nvidia.com", StringComparison.Ordinal);
        }

        /// <summary>135 = Windows 11 (build 22000 o mayor); 57 = Windows 10 64-bit.</summary>
        static int OsId()
        {
            int build = 0;
            try { int.TryParse(Convert.ToString(Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber", null)), out build); } catch { }
            return build >= 22000 ? 135 : 57;
        }

        // Todos los productos de todas las series GeForce (sobremesa y portátiles)
        static List<Product> LoadGeForceProducts()
        {
            var series = new List<KeyValuePair<string, string>>(); // psid, nombre
            var doc = XDocument.Parse(DriverHttp.GetString(LookupUrl + "?TypeID=2"));
            foreach (var lv in doc.Descendants("LookupValue"))
                // En la respuesta real ParentID es un atributo; se acepta también como elemento hijo
                if (((string)lv.Attribute("ParentID") ?? (string)lv.Element("ParentID") ?? "").Trim() == GeForceParentId)
                {
                    string psid = ((string)lv.Element("Value") ?? "").Trim();
                    if (psid.Length > 0 && psid.All(char.IsDigit)) series.Add(new KeyValuePair<string, string>(psid, ((string)lv.Element("Name") ?? "").Trim()));
                }
            if (series.Count == 0) throw new InvalidDataException(Loc.T("drv.err.badResponse"));
            Log.Info("NVIDIA: " + series.Count + " GeForce series in the catalog.");

            var all = new System.Collections.Concurrent.ConcurrentBag<Product>();
            try
            {
                Parallel.ForEach(series, new ParallelOptions { MaxDegreeOfParallelism = 4 }, s =>
                {
                    var d = XDocument.Parse(DriverHttp.GetString(LookupUrl + "?TypeID=3&ParentID=" + s.Key));
                    foreach (var lv in d.Descendants("LookupValue"))
                    {
                        string pfid = ((string)lv.Element("Value") ?? "").Trim();
                        string name = ((string)lv.Element("Name") ?? "").Trim();
                        if (pfid.Length > 0 && name.Length > 0) all.Add(new Product { Psid = s.Key, SeriesName = s.Value, Pfid = pfid, Name = name });
                    }
                });
            }
            catch (AggregateException ae) { throw ae.InnerExceptions[0]; }
            return all.ToList();
        }

        internal static string Normalize(string s) => Regex.Replace((s ?? "").Trim(), @"\s+", " ").ToUpperInvariant();

        static Dictionary<string, object> Sub(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        static string Text(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
        }

        internal static DateTime? ParseReleaseDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            DateTime d;
            string[] fmts = { "ddd MMM dd, yyyy", "ddd MMM d, yyyy", "yyyy-MM-dd" };
            if (DateTime.TryParseExact(s.Trim(), fmts, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d;
            return null;
        }

        /// <summary>"1.04 GB" a MB.</summary>
        internal static double ParseSizeMB(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            var m = Regex.Match(s, @"([\d.,]+)\s*(KB|MB|GB)", RegexOptions.IgnoreCase);
            if (!m.Success) return 0;
            double v;
            if (!double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return 0;
            switch (m.Groups[2].Value.ToUpperInvariant())
            {
                case "GB": return Math.Round(v * 1024, 1);
                case "KB": return Math.Round(v / 1024, 1);
                default: return Math.Round(v, 1);
            }
        }
    }

    /// <summary>GET sencillo con tiempo límite para las API de NVIDIA.</summary>
    internal static class DriverHttp
    {
        internal const string UserAgent = "Velox";

        internal static string GetString(string url, int timeoutMs = 20000)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = UserAgent;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                if (resp.ResponseUri.Scheme != Uri.UriSchemeHttps) throw new WebException("Insecure redirect");
                using (var rs = resp.GetResponseStream())
                using (var sr = new StreamReader(rs, Encoding.UTF8))
                {
                    var buf = new char[8192];
                    var sb = new StringBuilder();
                    int n;
                    while ((n = sr.Read(buf, 0, buf.Length)) > 0)
                    {
                        sb.Append(buf, 0, n);
                        if (sb.Length > 8 * 1024 * 1024) throw new InvalidDataException("Response too large");
                    }
                    return sb.ToString();
                }
            }
        }
    }
}
