using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Velox.Core.Backup;
using Velox.Core.Sys;
using Velox.Localization;

namespace Velox.Core.Tools
{
    public sealed class DnsProvider
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Primary { get; set; }
        public string Secondary { get; set; }
    }

    /// <summary>Adaptador de red en uso (con puerta de enlace IPv4).</summary>
    public sealed class DnsAdapter
    {
        /// <summary>Identificador del adaptador ({GUID}): es la clave del registro y no cambia aunque se renombre.</summary>
        public string Id { get; set; }
        public string Name { get; set; }
        public int Index { get; set; }
        /// <summary>DNS fijados a mano (registro NameServer). Vacío = los da el router (DHCP).</summary>
        public string StaticServers { get; set; }
        /// <summary>DNS IPv4 en uso ahora mismo.</summary>
        public List<string> Current { get; set; } = new List<string>();
    }

    /// <summary>
    /// DNS en un clic. Solo IPv4: las direcciones IPv6 del adaptador no se tocan. Antes de cambiar se guarda el DNS
    /// original de cada adaptador (vacío = el del router) y «Volver al original» lo deja exactamente igual. Nunca forma
    /// parte del modo Recomendado.
    /// </summary>
    public static class DnsTool
    {
        public const string HistoryId = "dns";

        public static readonly List<DnsProvider> Providers = new List<DnsProvider>
        {
            new DnsProvider { Id = "cloudflare", Name = "Cloudflare", Primary = "1.1.1.1", Secondary = "1.0.0.1" },
            new DnsProvider { Id = "google", Name = "Google", Primary = "8.8.8.8", Secondary = "8.8.4.4" },
            new DnsProvider { Id = "quad9", Name = "Quad9", Primary = "9.9.9.9", Secondary = "149.112.112.112" },
        };

        const string IfKey = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\";

        // ------------------------------------------------------------ Estado

        public static List<DnsAdapter> ActiveAdapters()
        {
            var list = new List<DnsAdapter>();
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus != OperationalStatus.Up) continue;
                if (n.NetworkInterfaceType == NetworkInterfaceType.Loopback || n.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                IPInterfaceProperties props;
                try { props = n.GetIPProperties(); } catch { continue; }
                bool gateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                if (!gateway) continue;
                int index;
                try { index = props.GetIPv4Properties()?.Index ?? -1; } catch { index = -1; }
                if (index < 0) continue;
                list.Add(new DnsAdapter
                {
                    Id = n.Id,
                    Name = n.Name,
                    Index = index,
                    StaticServers = (Reg.GetString(IfKey + n.Id, "NameServer") ?? "").Trim(),
                    Current = props.DnsAddresses.Where(d => d.AddressFamily == AddressFamily.InterNetwork).Select(d => d.ToString()).ToList(),
                });
            }
            return list;
        }

        /// <summary>Proveedor en uso (si todos los adaptadores usan el mismo de la lista), o null (el del router u otro).</summary>
        public static DnsProvider CurrentProvider(List<DnsAdapter> adapters = null)
        {
            adapters = adapters ?? ActiveAdapters();
            if (adapters.Count == 0) return null;
            foreach (var p in Providers)
                if (adapters.All(a => a.Current.Count > 0 && a.Current[0] == p.Primary)) return p;
            return null;
        }

        /// <summary>Texto para el historial: nombre del proveedor, «Automático (router)» o las direcciones.</summary>
        public static string Describe(string servers)
        {
            var list = SplitServers(servers);
            if (list.Count == 0) return Loc.T("dns.auto");
            var p = Providers.FirstOrDefault(x => x.Primary == list[0]);
            return p != null ? p.Name : string.Join(", ", list);
        }

        internal static List<string> SplitServers(string s) =>
            (s ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim())
                     .Where(x => IPAddress.TryParse(x, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork).ToList();

        public static bool HasBackup => BackupStore.Has(HistoryId);

        // ------------------------------------------------------------ Cambiar y volver

        /// <summary>Pone el proveedor en todos los adaptadores en uso, guardando antes el DNS original de cada uno.</summary>
        public static void Set(DnsProvider p, string source = "ui")
        {
            var adapters = ActiveAdapters();
            if (adapters.Count == 0) throw new InvalidOperationException(Loc.T("dns.err.noAdapter"));
            string before = Describe(adapters[0].StaticServers);
            try
            {
                foreach (var a in adapters)
                {
                    BackupStore.Add(HistoryId, new BackupRecord { Kind = "Dns", Key = "Dns|" + a.Id, Path = a.Id, Name = a.Name, Data = a.StaticServers });
                    SetStatic(a.Index, new[] { p.Primary, p.Secondary });
                }
                Flush();
                // Comprobación: el registro tiene que decir lo que se ha pedido
                foreach (var a in ActiveAdapters())
                    if (SplitServers(a.StaticServers).FirstOrDefault() != p.Primary)
                        throw new InvalidOperationException(Loc.F("dns.err.notApplied", a.Name));
            }
            catch (Exception ex)
            {
                History.Add(HistoryId, true, source, false, ex.Message, before, p.Name);
                // A medias no se queda: se vuelve a lo de antes
                try { BackupStore.Restore(HistoryId); Flush(); } catch { }
                throw;
            }
            History.Add(HistoryId, true, source, true, null, before, p.Name);
            Log.Info("DNS set to " + p.Name + " (" + p.Primary + ", " + p.Secondary + ") on " + adapters.Count + " adapter(s).");
        }

        /// <summary>Devuelve cada adaptador a su DNS original. false si no había nada que restaurar.</summary>
        public static bool RestoreOriginal(string source = "ui")
        {
            var recs = BackupStore.Get(HistoryId);
            if (recs.Count == 0) return false;
            string before = Describe(ActiveAdapters().FirstOrDefault()?.StaticServers);
            string after = Describe(recs[0].Data);
            BackupStore.Restore(HistoryId);
            Flush();
            bool ok = !BackupStore.Has(HistoryId);
            History.Add(HistoryId, false, source, ok, ok ? null : Loc.T("dns.err.restore"), before, after);
            Log.Info("DNS restored to the original (" + after + ")" + (ok ? "." : " with errors."));
            if (!ok) throw new InvalidOperationException(Loc.T("dns.err.restore"));
            return true;
        }

        /// <summary>Restaura un adaptador (lo usa Restorer para las copias de tipo «Dns»).</summary>
        internal static void RestoreAdapter(string adapterId, string servers)
        {
            var n = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(x => string.Equals(x.Id, adapterId, StringComparison.OrdinalIgnoreCase));
            if (n == null) { Log.Warn("DNS restore: adapter " + adapterId + " no longer exists; skipped."); return; }
            int index = n.GetIPProperties().GetIPv4Properties()?.Index ?? -1;
            if (index < 0) throw new InvalidOperationException("Adapter " + n.Name + " has no IPv4.");
            var list = SplitServers(servers);
            if (list.Count == 0) Netsh("interface ipv4 set dnsservers name=" + index + " source=dhcp");
            else SetStatic(index, list.ToArray());
        }

        static void SetStatic(int index, string[] servers)
        {
            Netsh("interface ipv4 set dnsservers name=" + index + " static " + servers[0] + " primary validate=no");
            for (int i = 1; i < servers.Length; i++)
                Netsh("interface ipv4 add dnsservers name=" + index + " " + servers[i] + " index=" + (i + 1) + " validate=no");
        }

        static void Netsh(string args)
        {
            var r = ProcessRunner.Run("netsh.exe", args, 30000);
            if (r.TimedOut || r.ExitCode != 0)
                throw new InvalidOperationException("netsh " + args + ": " + (r.TimedOut ? "timeout" : (r.Output ?? "").Trim()));
        }

        static void Flush() { try { ProcessRunner.Run("ipconfig.exe", "/flushdns", 30000); } catch { } }

        // ------------------------------------------------------------ Medir

        static readonly string[] BenchDomains = { "google.com", "youtube.com", "microsoft.com", "cloudflare.com", "wikipedia.org", "steampowered.com" };

        /// <summary>Mediana en ms de varias consultas reales (UDP, puerto 53) a ese servidor. null si no responde.</summary>
        public static double? Measure(string server, int rounds = 2, int timeoutMs = 1500)
        {
            if (!IPAddress.TryParse(server, out var ip)) return null;
            var times = new List<double>();
            var rnd = new Random();
            for (int r = 0; r < rounds; r++)
                foreach (var d in BenchDomains)
                {
                    var ms = QueryMs(ip, d, (ushort)rnd.Next(1, 65535), timeoutMs);
                    if (ms.HasValue) times.Add(ms.Value);
                }
            if (times.Count == 0) return null;
            times.Sort();
            int m = times.Count / 2;
            return times.Count % 2 == 1 ? times[m] : (times[m - 1] + times[m]) / 2;
        }

        static double? QueryMs(IPAddress server, string domain, ushort id, int timeoutMs)
        {
            try
            {
                using (var udp = new UdpClient(server.AddressFamily))
                {
                    udp.Client.ReceiveTimeout = timeoutMs;
                    var q = BuildQuery(id, domain);
                    var ep = new IPEndPoint(server, 53);
                    var sw = Stopwatch.StartNew();
                    udp.Send(q, q.Length, ep);
                    var remote = new IPEndPoint(IPAddress.Any, 0);
                    while (sw.ElapsedMilliseconds < timeoutMs)
                    {
                        var resp = udp.Receive(ref remote);
                        if (ParseResponse(resp, id, out int rcode, out _) && rcode == 0) return sw.Elapsed.TotalMilliseconds;
                    }
                }
            }
            catch (SocketException) { }
            catch (Exception ex) { Log.Warn("DNS query " + server + ": " + ex.Message); }
            return null;
        }

        /// <summary>Consulta DNS estándar (RFC 1035): cabecera con recursión pedida y una pregunta de tipo A.</summary>
        internal static byte[] BuildQuery(ushort id, string domain)
        {
            var b = new List<byte> { (byte)(id >> 8), (byte)id, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 };
            foreach (var label in domain.Trim('.').Split('.'))
            {
                var bytes = System.Text.Encoding.ASCII.GetBytes(label);
                if (bytes.Length == 0 || bytes.Length > 63) throw new ArgumentException("Bad domain label: " + label);
                b.Add((byte)bytes.Length);
                b.AddRange(bytes);
            }
            b.AddRange(new byte[] { 0, 0, 1, 0, 1 }); // fin del nombre, tipo A, clase IN
            return b.ToArray();
        }

        /// <summary>Comprueba que es la respuesta a esa consulta (mismo id, bit de respuesta) y lee el código y las respuestas.</summary>
        internal static bool ParseResponse(byte[] r, ushort id, out int rcode, out int answers)
        {
            rcode = -1; answers = 0;
            if (r == null || r.Length < 12) return false;
            if (((r[0] << 8) | r[1]) != id) return false;
            if ((r[2] & 0x80) == 0) return false; // no es una respuesta
            rcode = r[3] & 0x0F;
            answers = (r[6] << 8) | r[7];
            return true;
        }
    }
}
