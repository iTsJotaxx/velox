using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Velox.Localization;

namespace Velox.Core.Drivers
{
    /// <summary>
    /// Condiciones en las que NO se debe tocar un driver (instalar o volver al anterior):
    /// 1) Windows tiene un reinicio pendiente: encadenar cambios de drivers sin reiniciar es la forma clásica de romper uno.
    /// 2) Portátil funcionando con batería: un corte en mitad de un driver (sobre todo el de la gráfica) puede dejarlo inservible.
    /// </summary>
    public static class DriverSafety
    {
        /// <summary>Un cambio de driver hecho por Velox en esta sesión pidió reiniciar.</summary>
        public static bool SessionRebootPending { get; set; }

        /// <summary>Motivos (ya traducidos) que impiden tocar drivers ahora. Lista vacía = se puede.</summary>
        public static List<string> Blockers()
        {
            var list = new List<string>();
            string reboot = RebootPendingReason();
            if (reboot != null) list.Add(reboot);
            string power = PowerReason();
            if (power != null) list.Add(power);
            return list;
        }

        /// <summary>Motivo del reinicio pendiente, o null si no hay.</summary>
        internal static string RebootPendingReason()
        {
            if (SessionRebootPending) return Loc.T("drv.block.rebootSession");
            // Claves que crean el propio Windows Update y el servicio de componentes (CBS) cuando falta reiniciar.
            // PendingFileRenameOperations no se usa: muchos programas la dejan puesta sin que haga falta reiniciar.
            if (Reg.KeyExists(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") ||
                Reg.KeyExists(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending"))
                return Loc.T("drv.block.rebootWindows");
            return null;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;        // 0 = batería, 1 = enchufado, 255 = desconocido
            public byte BatteryFlag;         // 128 = sin batería, 255 = desconocido
            public byte BatteryLifePercent;  // 0-100, 255 = desconocido
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        /// <summary>Motivo si el equipo va con batería, o null si está enchufado o no tiene batería.</summary>
        internal static string PowerReason()
        {
            try
            {
                if (!GetSystemPowerStatus(out var s)) return null;
                bool hasBattery = s.BatteryFlag != 128 && s.BatteryFlag != 255;
                if (hasBattery && s.ACLineStatus == 0)
                    return s.BatteryLifePercent <= 100
                        ? Loc.F("drv.block.batteryPct", s.BatteryLifePercent)
                        : Loc.T("drv.block.battery");
            }
            catch (Exception ex) { Log.Warn("Power status: " + ex.Message); }
            return null;
        }
    }
}
