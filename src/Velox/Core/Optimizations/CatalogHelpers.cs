using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Velox.Core.Backup;
using Velox.Core.Sys;
using Velox.Native;

namespace Velox.Core.Optimizations
{
    /// <summary>Funciones auxiliares del catálogo (plan Ultimate, DirectX, accesibilidad, red, entrada).</summary>
    static class CatalogHelpers
    {
        // ------------------------------------------------------------ Registro

        public static RegItem R(string path, string name, object value, RegistryValueKind kind, object @default)
            => new RegItem(path, name, value, kind, @default);

        public static RegItem Dw(string path, string name, int value, int? @default)
            => new RegItem(path, name, value, RegistryValueKind.DWord, @default);

        /// <summary>Lee un número del registro (DWORD o cadena); si falta o no es válido, devuelve el valor por defecto.</summary>
        public static int RegInt(string path, string name, int fallback)
        {
            var st = Reg.Get(path, name);
            if (!st.Existed || st.Value == null) return fallback;
            try { return Convert.ToInt32(st.Value); } catch { return fallback; }
        }

        // ------------------------------------------------------------ Plan «Máximo rendimiento»

        const string UltimateNameRx = "Ultimate Performance|M.ximo rendimiento|Rendimiento m.ximo";

        /// <summary>GUID del plan que creó Velox (guardado en la copia «PowerPlan»).</summary>
        public static string CreatedUltimateGuid()
        {
            foreach (var r in BackupStore.Get("PowerPlan"))
                if (r.Kind == "PowerScheme" && !string.IsNullOrEmpty(r.Extra)) return r.Extra.ToLowerInvariant();
            return null;
        }

        /// <summary>GUID de un plan Máximo rendimiento ya existente (el original, el creado por Velox o por nombre), o null.</summary>
        public static string FindUltimateScheme()
        {
            var list = PowerCfg.ListSchemes();
            foreach (var s in list)
                if (s.Guid == PowerCfg.UltimatePerformance) return s.Guid;
            string known = CreatedUltimateGuid();
            if (known != null)
                foreach (var s in list)
                    if (s.Guid == known) return s.Guid;
            foreach (var s in list)
                if (Regex.IsMatch(s.Name ?? "", UltimateNameRx, RegexOptions.IgnoreCase)) return s.Guid;
            return null;
        }

        public static bool UltimateActive()
        {
            string active = PowerCfg.ActiveScheme();
            if (active == null) return false;
            if (active == PowerCfg.UltimatePerformance) return true;
            string known = CreatedUltimateGuid();
            if (known != null && active == known) return true;
            return Regex.IsMatch(PowerCfg.ActiveSchemeName() ?? "", UltimateNameRx, RegexOptions.IgnoreCase);
        }

        public static void ApplyPowerPlan(string id)
        {
            string prev = PowerCfg.ActiveScheme();
            string guid = FindUltimateScheme();
            string created = null;
            if (guid == null)
            {
                try { guid = PowerCfg.Duplicate(PowerCfg.UltimatePerformance); created = guid; }
                catch (Exception ex)
                {
                    Log.Warn("PowerPlan duplicate: " + ex.Message);
                    throw new InvalidOperationException(Localization.Loc.T("err.powerPlanCreate"));
                }
            }
            Tracked.Record(id, new BackupRecord { Kind = "PowerScheme", Key = "PowerScheme", Path = prev, Extra = created });
            try { PowerCfg.SetActive(guid); }
            catch (Exception ex)
            {
                Log.Warn("PowerPlan setactive: " + ex.Message);
                throw new InvalidOperationException(Localization.Loc.T("err.powerPlanActivate"));
            }
        }

        // ------------------------------------------------------------ Ajustes de energía

        /// <summary>Lee un ajuste del plan activo; null si no se puede leer.</summary>
        public static PowerValue Active(string sub, string setting) => PowerCfg.QueryActive(sub, setting);

        /// <summary>Pone valores por defecto en el plan activo (sin copia: se usa al revertir).</summary>
        public static void SetActiveValue(string sub, string setting, int? ac, int? dc)
        {
            string scheme = PowerCfg.ActiveScheme();
            if (scheme == null) throw new InvalidOperationException("Active power plan could not be read.");
            PowerCfg.SetValue(scheme, sub, setting, ac, dc);
        }

        // ------------------------------------------------------------ DirectX (DirectXUserGlobalSettings)

        public const string DxPath = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences";
        const string DxName = "DirectXUserGlobalSettings";

        static string DxRaw() => Reg.Get(DxPath, DxName).Value as string ?? "";

        public static string GetDxSetting(string key)
        {
            foreach (var p in DxRaw().Split(';'))
            {
                var kv = p.Split(new[] { '=' }, 2);
                if (kv.Length == 2 && kv[0].Trim() == key) return kv[1].Trim();
            }
            return null;
        }

        /// <summary>Cadena «Clave=valor;» con la clave puesta (se conservan las demás).</summary>
        public static string MergeDxSetting(string key, string value)
        {
            var parts = new List<string>();
            bool found = false;
            foreach (var p in DxRaw().Split(';'))
            {
                if (p.Trim().Length == 0) continue;
                var kv = p.Split(new[] { '=' }, 2);
                if (kv[0].Trim() == key) { parts.Add(key + "=" + value); found = true; }
                else parts.Add(p.Trim());
            }
            if (!found) parts.Add(key + "=" + value);
            return string.Join(";", parts) + ";";
        }

        // ------------------------------------------------------------ Red

        const string TcpInterfaces = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\";

        /// <summary>Claves TCP/IP de los adaptadores conectados (sin bucle local).</summary>
        public static List<string> ActiveInterfaceKeys()
        {
            var result = new List<string>();
            foreach (var a in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (a.OperationalStatus != OperationalStatus.Up || a.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                string p = TcpInterfaces + a.Id;
                if (Reg.KeyExists(p)) result.Add(p);
            }
            return result;
        }

        // ------------------------------------------------------------ Ratón y teclado

        const string MousePath = @"HKCU\Control Panel\Mouse";
        const string KeyboardPath = @"HKCU\Control Panel\Keyboard";

        public static void SyncMouse()
        {
            // Si falta un valor se usa el de Windows (6, 10, 1); con 0 se quitaría la aceleración al revertir
            Spi.SetMouse(RegInt(MousePath, "MouseThreshold1", 6), RegInt(MousePath, "MouseThreshold2", 10), RegInt(MousePath, "MouseSpeed", 1));
        }

        public static void SyncKeyboard()
        {
            Spi.SetKeyboard((uint)RegInt(KeyboardPath, "KeyboardDelay", 1), (uint)RegInt(KeyboardPath, "KeyboardSpeed", 31));
        }

        public const string AccessPath = @"HKCU\Control Panel\Accessibility";
        public const string StickyPath = AccessPath + @"\StickyKeys";
        public const string FilterPath = AccessPath + @"\Keyboard Response";
        public const string TogglePath = AccessPath + @"\ToggleKeys";

        /// <summary>Flags de accesibilidad actuales (si falta alguno, el valor por defecto de Windows).</summary>
        public static void GetAccessFlags(out int sticky, out int filter, out int toggle)
        {
            sticky = RegInt(StickyPath, "Flags", 510);
            filter = RegInt(FilterPath, "Flags", 126);
            toggle = RegInt(TogglePath, "Flags", 62);
        }

        public static void SyncAccessKeys()
        {
            GetAccessFlags(out int s, out int f, out int t);
            Spi.SetAccessibilityFlags((uint)s, (uint)f, (uint)t);
        }

        // ------------------------------------------------------------ Varios

        public static void SyncMinAnimate()
        {
            string v = Reg.Get(@"HKCU\Control Panel\Desktop\WindowMetrics", "MinAnimate").Value as string;
            Spi.SetMinAnimate(v != "0");
        }

        public static void SyncMenuDelay()
        {
            Spi.SetMenuShowDelay((uint)RegInt(@"HKCU\Control Panel\Desktop", "MenuShowDelay", 400));
        }

        /// <summary>Tareas programadas de telemetría (ruta completa = carpeta + nombre).</summary>
        public static readonly string[] TelemetryTasks =
        {
            @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
            @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser Exp",
            @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
            @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
            @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
            @"\Microsoft\Windows\Autochk\Proxy",
            @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector",
        };

        /// <summary>Servicios poco usados que desactiva «UnusedServices».</summary>
        public static readonly string[] UnusedServiceNames = { "MapsBroker", "Fax", "RetailDemo", "WMPNetworkSvc" };
    }
}
