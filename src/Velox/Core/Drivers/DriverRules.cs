using System;
using System.Text.RegularExpressions;

namespace Velox.Core.Drivers
{
    /// <summary>Reglas de seguridad puras (sin red ni COM): firmware, comparación de versiones y conversión de versiones NVIDIA.</summary>
    internal static class DriverRules
    {
        static readonly Regex FirmwareWord = new Regex(@"(?<![a-z])(bios|uefi)(?![a-z])|firmware", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex VersionInText = new Regex(@"\d+(?:\.\d+){2,3}", RegexOptions.CultureInvariant);

        /// <summary>Firmware, BIOS o UEFI: nunca se ofrece ni se instala. Ante la duda, se descarta.</summary>
        internal static bool IsFirmware(string title, string driverClass, string model, string hardwareId)
        {
            if (!string.IsNullOrEmpty(driverClass) && driverClass.IndexOf("firmware", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!string.IsNullOrEmpty(title) && FirmwareWord.IsMatch(title)) return true;
            if (!string.IsNullOrEmpty(model) && FirmwareWord.IsMatch(model)) return true;
            // Los firmware de Windows Update llevan el hardware id UEFI\RES_{guid}
            if (!string.IsNullOrEmpty(hardwareId) && hardwareId.TrimStart().StartsWith(@"UEFI\", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Última versión con forma a.b.c(.d) que aparezca en el texto (los títulos de Windows Update la llevan al final).</summary>
        internal static Version VersionFromTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return null;
            var ms = VersionInText.Matches(title);
            if (ms.Count == 0) return null;
            return ParseVersion(ms[ms.Count - 1].Value);
        }

        /// <summary>Versión de driver "23.150.0.4" normalizada a 4 componentes; null si no se puede leer.</summary>
        internal static Version ParseVersion(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var parts = s.Trim().Split('.');
            if (parts.Length < 2 || parts.Length > 4) return null;
            var n = new int[4];
            for (int i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out n[i])) return null;
            return new Version(n[0], n[1], n[2], n[3]);
        }

        /// <summary>
        /// ¿Es la actualización de Windows Update estrictamente más nueva que lo instalado?
        /// La fecha debe ser mayor; si además se pueden leer las dos versiones, la nueva debe ser mayor. Ante la duda, false.
        /// </summary>
        internal static bool IsWuUpdateNewer(DateTime? newDate, string newTitle, DateTime? curDate, string curVersion)
        {
            if (!newDate.HasValue || !curDate.HasValue) return false;
            if (newDate.Value.Date <= curDate.Value.Date) return false;
            var nv = VersionFromTitle(newTitle);
            var cv = ParseVersion(curVersion);
            if (nv != null && cv != null && nv <= cv) return false;
            return true;
        }

        /// <summary>"32.0.16.2101" (Windows) a 62101, que equivale a la versión NVIDIA 621.01. null si no encaja.</summary>
        internal static int? NvidiaInstalledToNumber(string windowsVersion)
        {
            if (string.IsNullOrWhiteSpace(windowsVersion)) return null;
            var p = windowsVersion.Trim().Split('.');
            if (p.Length != 4) return null;
            foreach (var b in p) if (b.Length == 0 || b.Length > 5 || !IsDigits(b)) return null;
            if (p[3].Length > 4) return null;
            string s = p[2][p[2].Length - 1] + p[3].PadLeft(4, '0');
            int n;
            return int.TryParse(s, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out n) ? (int?)n : null;
        }

        /// <summary>"621.14" (web de NVIDIA) a 62114. null si no tiene la forma esperada.</summary>
        internal static int? NvidiaWebToNumber(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            var m = Regex.Match(v.Trim(), @"^(\d{3,4})\.(\d{2})$");
            if (!m.Success) return null;
            return int.Parse(m.Groups[1].Value) * 100 + int.Parse(m.Groups[2].Value);
        }

        internal static string NvidiaNumberToText(int n) => (n / 100) + "." + (n % 100).ToString("00");

        static bool IsDigits(string s)
        {
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return true;
        }
    }
}
