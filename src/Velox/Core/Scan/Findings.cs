using System;
using System.Collections.Generic;
using System.Globalization;
using Velox.Localization;

namespace Velox.Core.Scan
{
    public enum FindingLevel { Ok, Info, Warning }

    public sealed class Finding
    {
        public FindingLevel Level { get; set; }
        /// <summary>Texto ya traducido.</summary>
        public string Text { get; set; }
    }

    /// <summary>Hallazgos del escaneo: cosas que Velox no cambia solo pero conviene saber.</summary>
    public static class Findings
    {
        // Los números y fechas se formatean con la cultura del idioma de la app, no con la de Windows
        static CultureInfo Culture
        {
            get
            {
                try { return CultureInfo.GetCultureInfo(Loc.Language); }
                catch { return CultureInfo.InvariantCulture; }
            }
        }

        static string N(double v, string fmt = "0.#") => v.ToString(fmt, Culture);

        public static List<Finding> Build(SystemInfo s)
        {
            var f = new List<Finding>();
            Action<FindingLevel, string> add = (level, text) => f.Add(new Finding { Level = level, Text = text });
            var ci = Culture;
            string sysDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";

            if (s.OtherAccount)
                add(FindingLevel.Warning, Loc.F("find.otherAccount", Environment.UserName, s.ShellUser));

            if (s.Displays != null)
            {
                foreach (var d in s.Displays)
                {
                    if (d.MaxHz > d.CurrentHz)
                    {
                        string dev = (d.Device ?? "").Replace(@"\\.\", "");
                        add(FindingLevel.Warning, Loc.F("find.displayHz", dev, d.CurrentHz, d.MaxHz));
                    }
                }
            }

            if (s.OnBattery) add(FindingLevel.Warning, Loc.T("find.battery"));
            if (s.RamUsedPct >= 85) add(FindingLevel.Warning, Loc.F("find.ramHigh", s.RamUsedPct));
            if (s.RamTotalGB > 0 && s.RamTotalGB < 7.5) add(FindingLevel.Warning, Loc.F("find.ramLow", N(s.RamTotalGB)));
            if (!s.IsLaptop && s.RamModules == 1) add(FindingLevel.Info, Loc.T("find.singleChannel"));

            if (!s.IsLaptop && s.RamSpeed > 0 &&
                ((s.RamType == "DDR4" && s.RamSpeed <= 2666) || (s.RamType == "DDR5" && s.RamSpeed <= 4800)))
                add(FindingLevel.Info, Loc.F("find.xmp", s.RamType, s.RamSpeed));

            if (s.Hvci) add(FindingLevel.Info, Loc.T("find.hvci"));
            if (s.PerfCountersOff) add(FindingLevel.Info, Loc.T("find.perfCounters"));
            if (s.DiskType == "HDD") add(FindingLevel.Warning, Loc.T("find.hdd"));
            if (s.DiskFreePct < 15) add(FindingLevel.Warning, Loc.F("find.diskLow", sysDrive, N(s.DiskFreeGB), s.DiskFreePct));

            // Salud de los discos: cualquier estado distinto de Healthy
            if (s.Disks != null)
            {
                foreach (var d in s.Disks)
                {
                    if (string.Equals(d.Health, "Healthy", StringComparison.OrdinalIgnoreCase)) continue;
                    string key = "find.health." + (d.Health ?? "unknown").ToLowerInvariant();
                    string health = Loc.Has(key) ? Loc.T(key) : Loc.T("find.health.unknown");
                    add(FindingLevel.Warning, Loc.F("find.diskHealth", d.Name, health));
                }
            }

            // Hibernación con poco espacio libre en el disco del sistema
            if (s.HibernationEnabled && s.DiskFreePct < 20)
                add(FindingLevel.Info, Loc.F("find.hibernateLow", sysDrive, s.DiskFreePct));

            if (s.UptimeDays >= 7) add(FindingLevel.Info, Loc.F("find.uptime", N(s.UptimeDays)));

            if (s.Gpus != null)
            {
                foreach (var g in s.Gpus)
                {
                    if (g.DriverDate.HasValue && (DateTime.Now - g.DriverDate.Value).TotalDays > 180)
                        add(FindingLevel.Info, Loc.F("find.driverOld", g.Name, g.DriverDate.Value.ToString("d", ci)));
                }
            }

            if (s.StartupEnabled > 8) add(FindingLevel.Info, Loc.F("find.startupMany", s.StartupEnabled));

            if (f.Count == 0) add(FindingLevel.Ok, Loc.T("find.none"));
            return f;
        }
    }
}
