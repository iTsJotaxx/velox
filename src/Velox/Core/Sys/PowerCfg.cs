using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Velox.Core.Sys
{
    public sealed class PowerValue
    {
        public int AC { get; set; }
        public int DC { get; set; }
        /// <summary>Rango permitido (solo en ajustes numéricos; null en ajustes de lista).</summary>
        public int? Min { get; set; }
        public int? Max { get; set; }
    }

    public sealed class PowerScheme
    {
        public string Guid { get; set; }
        public string Name { get; set; }
        public bool Active { get; set; }
    }

    /// <summary>Planes y ajustes de energía con powercfg.exe (la salida se analiza sin depender del idioma).</summary>
    public static class PowerCfg
    {
        public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
        public const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        public const string UltimatePerformance = "e9a42b02-d5df-448d-aa00-03f14749eb61";

        public const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
        public const string CpMinCores = "0cc5b647-c1df-4637-891a-dec35c318583";
        public const string SubUsb = "2a737441-1930-4402-8d77-b2bebba308a3";
        public const string UsbSelectiveSuspend = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";
        public const string SubPciExpress = "501a4d13-42af-4429-9fd1-a8218c268e20";
        public const string PciAspm = "ee12f906-d277-404b-b6da-e5fa1a576df5";

        public const string OverlayBestPerformance = "ded574b5-45a0-4f42-8737-46345c09c238";
        public const string OverlayBalanced = "00000000-0000-0000-0000-000000000000";

        public static readonly Regex GuidRx = new Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);
        static readonly Regex HexRx = new Regex("0x([0-9a-fA-F]{8})", RegexOptions.Compiled);

        static ProcResult Run(string args) => ProcessRunner.Run("powercfg.exe", args, 30000);

        static void RunChecked(string args)
        {
            var r = Run(args);
            if (r.ExitCode != 0) throw new InvalidOperationException("powercfg: " + r.Output);
        }

        public static string ActiveScheme()
        {
            var m = GuidRx.Match(Run("/getactivescheme").Output);
            return m.Success ? m.Value.ToLowerInvariant() : null;
        }

        /// <summary>El nombre va entre paréntesis tras el GUID y puede llevar paréntesis dentro.</summary>
        public static string ActiveSchemeName()
        {
            var o = Run("/getactivescheme").Output;
            var m = Regex.Match(o, GuidRx + @"\s+\((.*)\)\s*$", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        public static List<PowerScheme> ListSchemes()
        {
            var list = new List<PowerScheme>();
            foreach (var line in Run("/list").Output.Split('\n'))
            {
                var g = GuidRx.Match(line);
                if (!g.Success) continue;
                var n = Regex.Match(line, GuidRx + @"\s+\((.*)\)\s*(\*)?\s*$");
                list.Add(new PowerScheme
                {
                    Guid = g.Value.ToLowerInvariant(),
                    Name = n.Success ? n.Groups[1].Value.Trim() : "",
                    Active = line.TrimEnd().EndsWith("*"),
                });
            }
            return list;
        }

        /// <summary>Lee los índices AC/DC actuales (los dos últimos 0x… de la salida). /qh incluye ajustes ocultos.</summary>
        public static PowerValue Query(string scheme, string sub, string setting)
        {
            if (string.IsNullOrEmpty(scheme)) return null;
            foreach (var verb in new[] { "/qh", "/query" })
            {
                var hex = HexRx.Matches(Run(verb + " " + scheme + " " + sub + " " + setting).Output);
                if (hex.Count < 2) continue;
                var v = new PowerValue
                {
                    AC = Convert.ToInt32(hex[hex.Count - 2].Groups[1].Value, 16),
                    DC = Convert.ToInt32(hex[hex.Count - 1].Groups[1].Value, 16),
                };
                // Ajustes de rango: mínimo, máximo, incremento, AC, DC
                if (hex.Count >= 5)
                {
                    v.Min = Convert.ToInt32(hex[hex.Count - 5].Groups[1].Value, 16);
                    v.Max = Convert.ToInt32(hex[hex.Count - 4].Groups[1].Value, 16);
                }
                return v;
            }
            return null;
        }

        public static PowerValue QueryActive(string sub, string setting) => Query(ActiveScheme(), sub, setting);

        public static void SetValue(string scheme, string sub, string setting, int? ac, int? dc)
        {
            if (ac.HasValue) RunChecked("/setacvalueindex " + scheme + " " + sub + " " + setting + " " + ac.Value);
            if (dc.HasValue) RunChecked("/setdcvalueindex " + scheme + " " + sub + " " + setting + " " + dc.Value);
            if (string.Equals(scheme, ActiveScheme(), StringComparison.OrdinalIgnoreCase)) RunChecked("/setactive " + scheme);
        }

        public static void SetActive(string guid) => RunChecked("/setactive " + guid);

        public static string Duplicate(string guid)
        {
            var r = Run("/duplicatescheme " + guid);
            var m = GuidRx.Matches(r.Output);
            // La salida contiene el GUID nuevo (distinto del original)
            foreach (Match x in m)
                if (!string.Equals(x.Value, guid, StringComparison.OrdinalIgnoreCase)) return x.Value.ToLowerInvariant();
            throw new InvalidOperationException("powercfg /duplicatescheme: " + r.Output);
        }

        public static void TryDelete(string guid)
        {
            try { Run("/delete " + guid); } catch { }
        }

        /// <summary>true si la hibernación está activada (lee el registro).</summary>
        public static bool HibernationEnabled()
        {
            int? v = Reg.GetInt(@"HKLM\SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled");
            return v != 0;
        }

        public static void SetHibernation(bool on) => RunChecked("/hibernate " + (on ? "on" : "off"));

        [System.Runtime.InteropServices.DllImport("powrprof.dll")]
        static extern bool GetPwrCapabilities(IntPtr capabilities);

        /// <summary>
        /// ¿El equipo admite hibernar (estado S4)? En máquinas virtuales y algunos equipos el registro dice que está
        /// activada pero el firmware no la admite: entonces se podría apagar y nunca volver a encender.
        /// </summary>
        public static bool SupportsHibernation()
        {
            IntPtr buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(512); // SYSTEM_POWER_CAPABILITIES (~76 bytes)
            try
            {
                if (!GetPwrCapabilities(buf)) return true; // si no se sabe, no se bloquea
                return System.Runtime.InteropServices.Marshal.ReadByte(buf, 6) != 0; // BOOLEAN SystemS4
            }
            catch { return true; }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buf); }
        }
    }
}
