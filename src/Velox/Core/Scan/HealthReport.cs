using System;
using System.Collections.Generic;
using System.Management;
using Velox.Localization;

namespace Velox.Core.Scan
{
    /// <summary>Datos para el informe de salud (se juntan en segundo plano; así la lógica se prueba sin tocar Windows).</summary>
    public sealed class HealthInputs
    {
        public double DiskFreeGB { get; set; }
        public double DiskSizeGB { get; set; }
        public string DiskType { get; set; }
        /// <summary>Discos que Windows marca como no sanos (-1 si no se pudo leer).</summary>
        public int DisksUnhealthy { get; set; } = -1;
        public int DisksTotal { get; set; }
        public double RamTotalGB { get; set; }
        /// <summary>% de RAM disponible ahora.</summary>
        public double RamAvailablePct { get; set; } = -1;
        public int RecommendedPending { get; set; }
        public int Reverted { get; set; }
        public bool RebootPending { get; set; }
        public double UptimeDays { get; set; }
        public int StartupApps { get; set; } = -1;
        /// <summary>Protección en tiempo real de Defender: null si no se pudo leer (otro antivirus, por ejemplo).</summary>
        public bool? DefenderRealtime { get; set; }
    }

    public sealed class HealthItem
    {
        /// <summary>disk, disks, ram, opts, windows, startup, security.</summary>
        public string Area { get; set; }
        public bool Attention { get; set; }
        public string Text { get; set; }
        /// <summary>Página a la que lleva el botón (o null).</summary>
        public string Page { get; set; }
        public string Name => Loc.T("health." + Area);
        public string ActionText => Page == null ? null : Loc.T("health.go." + Area);
    }

    /// <summary>
    /// Informe de salud: reordena lo que Velox ya sabe en una lista por áreas (bien / pide atención) con un acceso a dónde
    /// arreglarlo. Sin nota global: cada área dice lo que pasa.
    /// </summary>
    public static class HealthReport
    {
        public const double LowDiskPct = 15, LowRamPct = 15, LongUptimeDays = 7;
        public const int ManyStartupApps = 10;

        public static List<HealthItem> Build(HealthInputs i)
        {
            var list = new List<HealthItem>();
            if (i.DiskSizeGB > 0)
            {
                double pct = i.DiskFreeGB * 100 / i.DiskSizeGB;
                list.Add(new HealthItem
                {
                    Area = "disk", Attention = pct < LowDiskPct, Page = "disk",
                    Text = Loc.F(pct < LowDiskPct ? "health.disk.low" : "health.disk.ok", i.DiskFreeGB.ToString("0"), pct.ToString("0")),
                });
            }
            if (i.DisksUnhealthy >= 0 && i.DisksTotal > 0)
                list.Add(new HealthItem
                {
                    Area = "disks", Attention = i.DisksUnhealthy > 0, Page = i.DisksUnhealthy > 0 ? "diagnostics" : null,
                    Text = i.DisksUnhealthy > 0 ? Loc.F("health.disks.bad", i.DisksUnhealthy, i.DisksTotal) : i.DisksTotal == 1 ? Loc.T("health.disks.ok1") : Loc.F("health.disks.ok", i.DisksTotal),
                });
            if (i.RamAvailablePct >= 0)
                list.Add(new HealthItem
                {
                    Area = "ram", Attention = i.RamAvailablePct < LowRamPct, Page = "memory",
                    Text = Loc.F(i.RamAvailablePct < LowRamPct ? "health.ram.low" : "health.ram.ok", i.RamAvailablePct.ToString("0"), i.RamTotalGB.ToString("0.#")),
                });
            list.Add(new HealthItem
            {
                Area = "opts", Attention = i.RecommendedPending > 0 || i.Reverted > 0, Page = i.RecommendedPending > 0 || i.Reverted > 0 ? "summary" : null,
                Text = i.Reverted > 0 ? Loc.F("health.opts.reverted", i.Reverted)
                     : i.RecommendedPending > 0 ? Loc.F("health.opts.todo", i.RecommendedPending) : Loc.T("health.opts.ok"),
            });
            list.Add(new HealthItem
            {
                Area = "windows", Attention = i.RebootPending || i.UptimeDays >= LongUptimeDays,
                Text = i.RebootPending ? Loc.T("health.windows.reboot")
                     : i.UptimeDays >= LongUptimeDays ? Loc.F("health.windows.uptime", i.UptimeDays.ToString("0")) : Loc.T("health.windows.ok"),
            });
            if (i.StartupApps >= 0)
                list.Add(new HealthItem
                {
                    Area = "startup", Attention = i.StartupApps >= ManyStartupApps, Page = "startup",
                    Text = Loc.F(i.StartupApps >= ManyStartupApps ? "health.startup.many" : "health.startup.ok", i.StartupApps),
                });
            if (i.DefenderRealtime.HasValue)
                list.Add(new HealthItem
                {
                    Area = "security", Attention = !i.DefenderRealtime.Value,
                    Text = i.DefenderRealtime.Value ? Loc.T("health.security.ok") : Loc.T("health.security.off"),
                });
            return list;
        }

        /// <summary>Discos físicos y cuántos no están sanos según Windows (HealthStatus distinto de 0). (-1, 0) si no se puede leer.</summary>
        public static Tuple<int, int> ReadDiskHealth()
        {
            try
            {
                int total = 0, bad = 0;
                var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
                scope.Connect();
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT HealthStatus FROM MSFT_PhysicalDisk")))
                using (var res = s.Get())
                    foreach (ManagementBaseObject d in res)
                        using (d)
                        {
                            total++;
                            if (Convert.ToInt32(d["HealthStatus"] ?? 0) != 0) bad++;
                        }
                return Tuple.Create(bad, total);
            }
            catch (Exception ex) { Log.Warn("Health (disks): " + ex.Message); return Tuple.Create(-1, 0); }
        }

        /// <summary>Protección en tiempo real de Microsoft Defender (null si no se puede saber, por ejemplo con otro antivirus).</summary>
        public static bool? ReadDefenderRealtime()
        {
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\Defender", "SELECT RealTimeProtectionEnabled, AMServiceEnabled FROM MSFT_MpComputerStatus"))
                using (var res = s.Get())
                    foreach (ManagementBaseObject o in res)
                        using (o)
                        {
                            if (o["AMServiceEnabled"] is bool svc && !svc) return null; // Defender apagado porque hay otro antivirus
                            if (o["RealTimeProtectionEnabled"] is bool rt) return rt;
                        }
            }
            catch (Exception ex) { Log.Warn("Health (Defender): " + ex.Message); }
            return null;
        }
    }
}
