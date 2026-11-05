using System.Runtime.InteropServices;

namespace Velox.Native
{
    /// <summary>Memoria física total y disponible (GlobalMemoryStatusEx).</summary>
    public static class MemoryStatus
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength; public uint dwMemoryLoad;
            public ulong ullTotalPhys; public ulong ullAvailPhys;
            public ulong ullTotalPageFile; public ulong ullAvailPageFile;
            public ulong ullTotalVirtual; public ulong ullAvailVirtual; public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        public static bool Read(out double totalBytes, out double availableBytes)
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            totalBytes = 0; availableBytes = 0;
            if (!GlobalMemoryStatusEx(ref m)) return false;
            totalBytes = m.ullTotalPhys;
            availableBytes = m.ullAvailPhys;
            return true;
        }
    }
}
