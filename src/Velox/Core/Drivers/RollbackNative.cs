using System;
using System.Runtime.InteropServices;
using Velox.Localization;

namespace Velox.Core.Drivers
{
    /// <summary>Vuelta al driver anterior con DiRollbackDriver (newdev.dll) y SetupAPI.</summary>
    internal static class RollbackNative
    {
        const uint ROLLBACK_FLAG_NO_UI = 0x1;
        static readonly IntPtr InvalidHandle = new IntPtr(-1);

        // Errores que significan «no hay un driver anterior guardado»
        const int ERROR_FILE_NOT_FOUND = 2;
        const int ERROR_PATH_NOT_FOUND = 3;
        const int ERROR_NO_MORE_ITEMS = 259;
        const int ERROR_NOT_FOUND = 1168;
        const int ERROR_NO_DRIVER_SELECTED = unchecked((int)0xE0000203);
        const int ERROR_NO_SUCH_DEVINST = unchecked((int)0xE000020B);

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern IntPtr SetupDiCreateDeviceInfoList(IntPtr classGuid, IntPtr hwndParent);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiOpenDeviceInfo(IntPtr deviceInfoSet, string deviceInstanceId, IntPtr hwndParent, uint openFlags, ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("newdev.dll", SetLastError = true)]
        static extern bool DiRollbackDriver(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, IntPtr hwndParent, uint flags, out bool needReboot);

        // Los HRESULT de SetupAPI (0xE000xxxx) se ven mejor en hexadecimal
        static string ErrText(int err) => err < 0 ? "0x" + err.ToString("X8") : err.ToString();

        /// <summary>Devuelve el mensaje de éxito traducido; lanza InvalidOperationException con mensaje traducido si no se puede.</summary>
        internal static string Rollback(DeviceDriver device)
        {
            if (device == null || string.IsNullOrWhiteSpace(device.DeviceId))
                throw new InvalidOperationException(Loc.T("drv.msg.rollbackNoDevice"));
            string name = string.IsNullOrWhiteSpace(device.DeviceName) ? device.DeviceId : device.DeviceName;
            Log.Info("Driver rollback requested for '" + name + "' (" + device.DeviceId + ").");

            IntPtr set = SetupDiCreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero);
            if (set == InvalidHandle || set == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                Log.Error("Rollback: SetupDiCreateDeviceInfoList failed (" + err + ").");
                throw new InvalidOperationException(Loc.F("drv.msg.rollbackFailed", name, ErrText(err)));
            }
            try
            {
                var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };
                if (!SetupDiOpenDeviceInfo(set, device.DeviceId, IntPtr.Zero, 0, ref data))
                {
                    int err = Marshal.GetLastWin32Error();
                    Log.Error("Rollback: SetupDiOpenDeviceInfo failed (" + err + ").");
                    throw new InvalidOperationException(Loc.F("drv.msg.rollbackFailed", name, ErrText(err)));
                }

                bool needReboot;
                if (!DiRollbackDriver(set, ref data, IntPtr.Zero, ROLLBACK_FLAG_NO_UI, out needReboot))
                {
                    int err = Marshal.GetLastWin32Error();
                    Log.Warn("Rollback: DiRollbackDriver failed (" + err + ") for '" + name + "'.");
                    if (err == ERROR_NO_MORE_ITEMS || err == ERROR_FILE_NOT_FOUND || err == ERROR_PATH_NOT_FOUND ||
                        err == ERROR_NOT_FOUND || err == ERROR_NO_DRIVER_SELECTED || err == ERROR_NO_SUCH_DEVINST)
                        throw new InvalidOperationException(Loc.F("drv.msg.rollbackNone", name));
                    throw new InvalidOperationException(Loc.F("drv.msg.rollbackFailed", name, ErrText(err)));
                }

                Log.Info("Rollback done for '" + name + "'" + (needReboot ? " (reboot required)." : "."));
                return Loc.F(needReboot ? "drv.msg.rollbackDoneReboot" : "drv.msg.rollbackDone", name);
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
        }
    }
}
