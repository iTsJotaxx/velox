using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;

namespace Velox.Core.Drivers
{
    /// <summary>Lectura de Win32_PnPSignedDriver y clasificación por categorías.</summary>
    internal static class PnpReader
    {
        static readonly string[] CategoryOrder = { "gpu", "network", "audio", "bluetooth", "storage", "chipset", "input", "usb", "other" };

        // Clases que son software puro: no tiene sentido listarlas ni volver atrás su driver
        static readonly HashSet<string> SoftwareClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SOFTWARECOMPONENT", "SOFTWAREDEVICE", "AUDIOPROCESSINGOBJECT", "EXTENSION",
            "VOLUME", "VOLUMESNAPSHOT", "PRINTQUEUE", "PRINTER", "WSDPRINTDEVICE",
            "COMPUTER", "LEGACYDRIVER", "PROCESSOR",
            "FIRMWARE", // el firmware nunca se toca
        };

        /// <summary>Todos los drivers firmados con nombre y versión, sin filtrar por clase. Sirve para emparejar actualizaciones.</summary>
        internal static List<DeviceDriver> ReadAll()
        {
            var list = new List<DeviceDriver>();
            const string q = "SELECT DeviceName, DeviceClass, Manufacturer, DriverProviderName, DriverVersion, DriverDate, DeviceID, HardWareID, InfName FROM Win32_PnPSignedDriver";
            using (var searcher = new ManagementObjectSearcher(q))
            using (var results = searcher.Get())
            {
                foreach (ManagementBaseObject o in results)
                {
                    using (o)
                    {
                        string name = Str(o, "DeviceName");
                        string ver = Str(o, "DriverVersion");
                        string id = Str(o, "DeviceID");
                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(ver) || string.IsNullOrWhiteSpace(id)) continue;
                        string cls = Str(o, "DeviceClass");
                        string man = Str(o, "Manufacturer");
                        list.Add(new DeviceDriver
                        {
                            DeviceName = name.Trim(),
                            DeviceClass = cls,
                            Manufacturer = man,
                            Provider = Str(o, "DriverProviderName"),
                            DriverVersion = ver.Trim(),
                            DriverDate = ParseCimDate(Str(o, "DriverDate")),
                            DeviceId = id,
                            HardwareId = Str(o, "HardWareID"),
                            InfName = Str(o, "InfName"),
                            Category = CategoryOf(cls, man),
                        });
                    }
                }
            }
            return list;
        }

        /// <summary>Dispositivos con driver, sin duplicados ni los puramente de software, ordenados por categoría y nombre.</summary>
        internal static List<DeviceDriver> GetInstalled()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var res = new List<DeviceDriver>();
            foreach (var d in ReadAll())
            {
                if (IsSoftwareOnly(d)) continue;
                if (!seen.Add(d.DeviceId)) continue;
                res.Add(d);
            }
            return res
                .OrderBy(d => Array.IndexOf(CategoryOrder, d.Category))
                .ThenBy(d => d.DeviceName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        internal static bool IsSoftwareOnly(DeviceDriver d)
        {
            string cls = d.DeviceClass ?? "";
            if (SoftwareClasses.Contains(cls)) return true;
            // Dispositivos de software (SWD\) y componentes de software (SWC\)
            string id = d.DeviceId ?? "";
            if (id.StartsWith(@"SWD\", StringComparison.OrdinalIgnoreCase) || id.StartsWith(@"SWC\", StringComparison.OrdinalIgnoreCase)) return true;
            // SYSTEM solo cuenta si es chipset (Intel/AMD)
            if (cls.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase) && d.Category != "chipset") return true;
            return false;
        }

        internal static string CategoryOf(string deviceClass, string manufacturer)
        {
            switch ((deviceClass ?? "").ToUpperInvariant())
            {
                case "DISPLAY": return "gpu";
                case "NET": return "network";
                case "MEDIA":
                case "AUDIOENDPOINT": return "audio";
                case "BLUETOOTH": return "bluetooth";
                case "SCSIADAPTER":
                case "HDC":
                case "DISKDRIVE": return "storage";
                case "SYSTEM": return IsIntelOrAmd(manufacturer) ? "chipset" : "other";
                case "HIDCLASS":
                case "KEYBOARD":
                case "MOUSE": return "input";
                case "USB": return "usb";
                default: return "other";
            }
        }

        static bool IsIntelOrAmd(string manufacturer)
        {
            if (string.IsNullOrWhiteSpace(manufacturer)) return false;
            return manufacturer.IndexOf("intel", StringComparison.OrdinalIgnoreCase) >= 0
                || manufacturer.IndexOf("advanced micro", StringComparison.OrdinalIgnoreCase) >= 0
                || Regex.IsMatch(manufacturer, @"\bAMD\b", RegexOptions.IgnoreCase);
        }

        static string Str(ManagementBaseObject o, string prop)
        {
            try { return o[prop] as string; } catch { return null; }
        }

        /// <summary>Fecha CIM (yyyyMMddHHmmss.ffffff+zzz). Solo el día: así no cambia por la zona horaria.</summary>
        internal static DateTime? ParseCimDate(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 8) return null;
            DateTime d;
            if (DateTime.TryParseExact(s.Substring(0, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) && d.Year > 1980)
                return d;
            return null;
        }
    }
}
