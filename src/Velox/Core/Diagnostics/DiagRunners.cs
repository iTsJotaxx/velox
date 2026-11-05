using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Velox.Core.Sys;
using Velox.Localization;

namespace Velox.Core.Diagnostics
{
    /// <summary>Diagnósticos que se ejecutan dentro de Velox (se llaman desde un hilo de fondo).</summary>
    internal static class DiagRunners
    {
        public static DiagResult RunInApp(string id)
        {
            switch (id)
            {
                case "diskhealth": return DiskHealth();
                case "battery": return Battery();
                case "energy": return Energy();
                case "ping": return Ping();
                case "netinfo": return NetInfo();
                case "flushdns": return FlushDns();
                case "netreset": return NetReset();
                default: throw new NotSupportedException(id);
            }
        }

        static DiagResult Fail(string message, string details = null) =>
            new DiagResult { Success = false, Message = message, Details = details };

        // ---------- Discos ----------

        static DiagResult DiskHealth()
        {
            var lines = new List<string>();
            int total = 0, bad = 0;
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();
            using (var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT FriendlyName, MediaType, BusType, Size, HealthStatus, OperationalStatus FROM MSFT_PhysicalDisk")))
            using (var results = searcher.Get())
            {
                foreach (ManagementBaseObject d in results)
                {
                    total++;
                    string name = Convert.ToString(d["FriendlyName"]);
                    int media = ToInt(d["MediaType"], 0);
                    int bus = ToInt(d["BusType"], 0);
                    double size = d["Size"] != null ? Convert.ToDouble(d["Size"], CultureInfo.InvariantCulture) : 0;
                    int health = ToInt(d["HealthStatus"], 5);

                    string type = bus == 17 ? "NVMe" : media == 4 ? "SSD" : media == 3 ? "HDD" : Loc.T("diag.diskhealth.type.unknown");
                    string healthText = health == 0 ? Loc.T("diag.diskhealth.health.ok")
                        : health == 1 ? Loc.T("diag.diskhealth.health.warning")
                        : health == 2 ? Loc.T("diag.diskhealth.health.unhealthy")
                        : Loc.T("diag.diskhealth.health.unknown");
                    if (health != 0) bad++;

                    var ops = d["OperationalStatus"] as ushort[];
                    string opText = ops == null || ops.Length == 0
                        ? Loc.T("diag.diskhealth.op.unknown")
                        : string.Join(", ", ops.Select(OpName));

                    lines.Add(Loc.F("diag.diskhealth.result.line", name, type, FormatSize(size), healthText, opText));
                }
            }

            if (total == 0) return Fail(Loc.T("diag.diskhealth.result.none"));
            return new DiagResult
            {
                Success = bad == 0,
                Message = bad == 0 ? Loc.F("diag.diskhealth.result.ok", total) : Loc.F("diag.diskhealth.result.bad", bad, total),
                Details = string.Join(Environment.NewLine, lines),
            };
        }

        static int ToInt(object o, int def)
        {
            try { return o == null ? def : Convert.ToInt32(o, CultureInfo.InvariantCulture); }
            catch { return def; }
        }

        static string OpName(ushort code)
        {
            switch (code)
            {
                case 2: case 3: case 4: case 5: case 6: case 7: case 12: case 13:
                    return Loc.T("diag.diskhealth.op." + code);
                default:
                    return Loc.F("diag.diskhealth.op.other", code);
            }
        }

        static string FormatSize(double bytes)
        {
            double gb = bytes / 1e9;
            return gb >= 1000
                ? (gb / 1000).ToString("0.##", CultureInfo.CurrentCulture) + " TB"
                : gb.ToString("0", CultureInfo.CurrentCulture) + " GB";
        }

        // ---------- Informes de powercfg ----------

        static string EnsureReportsDir()
        {
            string dir = AppPaths.ReportsDir;
            if (string.IsNullOrEmpty(dir))
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Velox", "Reports");
            Directory.CreateDirectory(dir);
            return dir;
        }

        static DiagResult Battery()
        {
            string file = Path.Combine(EnsureReportsDir(), "battery-report.html");
            try { if (File.Exists(file)) File.Delete(file); } catch { }
            var r = ProcessRunner.Run("powercfg.exe", "/batteryreport /output \"" + file + "\"", 60000);
            if (r.TimedOut) return Fail(Loc.T("diag.msg.timeout"));
            if (!File.Exists(file)) return Fail(Loc.T("diag.battery.result.fail"), r.Output);
            ProcessRunner.Shell(file);
            return new DiagResult { Success = true, Message = Loc.T("diag.battery.result.ok"), Details = file };
        }

        static DiagResult Energy()
        {
            string file = Path.Combine(EnsureReportsDir(), "energy-report.html");
            try { if (File.Exists(file)) File.Delete(file); } catch { }
            var r = ProcessRunner.Run("powercfg.exe", "/energy /output \"" + file + "\" /duration 60", 120000);
            if (r.TimedOut) return Fail(Loc.T("diag.msg.timeout"));
            // powercfg /energy devuelve un código distinto de 0 si el informe contiene errores: no es un fallo de ejecución
            if (!File.Exists(file)) return Fail(Loc.T("diag.energy.result.fail"), r.Output);
            ProcessRunner.Shell(file);
            return new DiagResult
            {
                Success = true,
                Message = Loc.T(r.ExitCode == 0 ? "diag.energy.result.ok" : "diag.energy.result.findings"),
                Details = file + (string.IsNullOrWhiteSpace(r.Output) ? "" : Environment.NewLine + Environment.NewLine + r.Output),
            };
        }

        // ---------- Red ----------

        sealed class PingStats
        {
            public string Host;
            public int Sent, Lost;
            public long Min, Max;
            public double Avg, Jitter;
            public int Received => Sent - Lost;
            public double LossPct => Sent == 0 ? 0 : 100.0 * Lost / Sent;
            public long Sum;
        }

        static PingStats PingHost(string host, int count)
        {
            var st = new PingStats { Host = host, Sent = count };
            var times = new List<long>();
            using (var p = new Ping())
            {
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        var reply = p.Send(host, 1000);
                        if (reply != null && reply.Status == IPStatus.Success) times.Add(reply.RoundtripTime);
                    }
                    catch (Exception ex) { Log.Warn("Ping " + host + ": " + ex.Message); }
                    if (i < count - 1) Thread.Sleep(150);
                }
            }
            st.Lost = count - times.Count;
            if (times.Count > 0)
            {
                st.Min = times.Min();
                st.Max = times.Max();
                st.Sum = times.Sum();
                st.Avg = times.Average();
                // Jitter: media de las diferencias absolutas entre pings consecutivos
                if (times.Count > 1)
                {
                    double acc = 0;
                    for (int i = 1; i < times.Count; i++) acc += Math.Abs(times[i] - times[i - 1]);
                    st.Jitter = acc / (times.Count - 1);
                }
            }
            return st;
        }

        static DiagResult Ping()
        {
            var stats = new[] { PingHost("1.1.1.1", 10), PingHost("8.8.8.8", 10) };

            var lines = new List<string>();
            foreach (var s in stats)
            {
                lines.Add(s.Received == 0
                    ? Loc.F("diag.ping.result.line.lost", s.Host)
                    : Loc.F("diag.ping.result.line", s.Host, Math.Round(s.Avg), s.Min, s.Max, Math.Round(s.Jitter), Math.Round(s.LossPct)));
            }

            int sent = stats.Sum(s => s.Sent), lost = stats.Sum(s => s.Lost);
            double lossPct = 100.0 * lost / sent;
            var details = string.Join(Environment.NewLine, lines);
            if (lost == sent) return Fail(Loc.T("diag.ping.result.none"), details);

            int received = sent - lost;
            double avg = stats.Sum(s => (double)s.Sum) / received;
            var withData = stats.Where(s => s.Received > 1).ToList();
            double jitter = withData.Count > 0 ? withData.Average(s => s.Jitter) : 0;

            string msg;
            if (lost > 0) msg = Loc.F("diag.ping.result.loss", Math.Round(avg), Math.Round(jitter), Math.Round(lossPct));
            else if (avg > 100) msg = Loc.F("diag.ping.result.slow", Math.Round(avg), Math.Round(jitter));
            else msg = Loc.F("diag.ping.result.ok", Math.Round(avg), Math.Round(jitter));

            return new DiagResult { Success = lost == 0 && avg <= 100, Message = msg, Details = details };
        }

        static bool IsVirtualAdapter(NetworkInterface n)
        {
            string s = (n.Name + " " + n.Description).ToLowerInvariant();
            return s.Contains("virtual") || s.Contains("vethernet") || s.Contains("vmware") || s.Contains("vbox")
                || s.Contains("hyper-v") || s.Contains("bluetooth") || s.Contains("tap-") || s.Contains("wsl");
        }

        static DiagResult NetInfo()
        {
            var sb = new StringBuilder();
            int count = 0, notes = 0;
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus != OperationalStatus.Up) continue;
                if (n.NetworkInterfaceType == NetworkInterfaceType.Loopback || n.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                var props = n.GetIPProperties();
                var ips = props.UnicastAddresses.Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(u => u.Address.ToString()).ToList();
                var gws = props.GatewayAddresses.Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                    && !g.Address.Equals(IPAddress.Any)).Select(g => g.Address.ToString()).ToList();
                var dns = props.DnsAddresses.Where(d => d.AddressFamily == AddressFamily.InterNetwork)
                    .Select(d => d.ToString()).ToList();

                bool ethernet = n.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                    || n.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet;
                string type = ethernet ? Loc.T("diag.netinfo.type.ethernet")
                    : n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? Loc.T("diag.netinfo.type.wifi")
                    : n.NetworkInterfaceType.ToString();

                count++;
                string none = Loc.T("diag.netinfo.none");
                if (sb.Length > 0) sb.AppendLine();
                sb.AppendLine(Loc.F("diag.netinfo.adapter", n.Name, type, FormatSpeed(n.Speed)));
                sb.AppendLine("  " + Loc.F("diag.netinfo.ipv4", ips.Count > 0 ? string.Join(", ", ips) : none));
                sb.AppendLine("  " + Loc.F("diag.netinfo.gateway", gws.Count > 0 ? string.Join(", ", gws) : none));
                sb.AppendLine("  " + Loc.F("diag.netinfo.dns", dns.Count > 0 ? string.Join(", ", dns) : none));

                if (ethernet && n.Speed > 0 && n.Speed < 1000000000L && !IsVirtualAdapter(n))
                {
                    notes++;
                    sb.AppendLine("  " + Loc.F("diag.netinfo.note", FormatSpeed(n.Speed)));
                }
            }

            if (count == 0) return Fail(Loc.T("diag.netinfo.result.none"));
            return new DiagResult
            {
                Success = true,
                Message = notes > 0 ? Loc.F("diag.netinfo.result.notes", count) : Loc.F("diag.netinfo.result.ok", count),
                Details = sb.ToString().TrimEnd(),
            };
        }

        static string FormatSpeed(long bps)
        {
            if (bps <= 0) return Loc.T("diag.netinfo.speed.unknown");
            return bps >= 1000000000L
                ? (bps / 1e9).ToString("0.##", CultureInfo.CurrentCulture) + " Gbps"
                : (bps / 1e6).ToString("0", CultureInfo.CurrentCulture) + " Mbps";
        }

        static DiagResult FlushDns()
        {
            var r = ProcessRunner.Run("ipconfig.exe", "/flushdns", 30000);
            if (r.TimedOut) return Fail(Loc.T("diag.msg.timeout"));
            return r.ExitCode == 0
                ? new DiagResult { Success = true, Message = Loc.T("diag.flushdns.result.ok"), Details = r.Output }
                : Fail(Loc.F("diag.flushdns.result.fail", r.ExitCode), r.Output);
        }

        static DiagResult NetReset()
        {
            var r1 = ProcessRunner.Run("netsh.exe", "winsock reset", 60000);
            var r2 = ProcessRunner.Run("netsh.exe", "int ip reset", 60000);
            string details = "netsh winsock reset" + Environment.NewLine + r1.Output
                + Environment.NewLine + Environment.NewLine
                + "netsh int ip reset" + Environment.NewLine + r2.Output;
            if (r1.TimedOut || r2.TimedOut) return Fail(Loc.T("diag.msg.timeout"), details);
            if (r1.ExitCode != 0 || r2.ExitCode != 0) return Fail(Loc.T("diag.netreset.result.fail"), details);
            return new DiagResult { Success = true, Message = Loc.T("diag.netreset.result.ok"), Details = details };
        }
    }
}
