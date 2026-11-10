using System;
using System.Runtime.InteropServices;

namespace Velox.Native
{
    /// <summary>Enumeración rápida de carpetas (FindFirstFileExW con FindExInfoBasic y LARGE_FETCH).</summary>
    public static class FileFind
    {
        public const uint AttrDirectory = 0x10;
        public const uint AttrReparsePoint = 0x400;

        // FILETIME son dos DWORD con alineación de 4 bytes: con "long" el resto de campos quedaría desplazado
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public uint ftCreationLow, ftCreationHigh;
            public uint ftAccessLow, ftAccessHigh;
            public uint ftWriteLow, ftWriteHigh;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr FindFirstFileExW(string lpFileName, int fInfoLevelId, out WIN32_FIND_DATA lpFindFileData, int fSearchOp, IntPtr lpSearchFilter, int dwAdditionalFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll")]
        static extern bool FindClose(IntPtr hFindFile);

        public static long LastWrite(ref WIN32_FIND_DATA d) => ((long)d.ftWriteHigh << 32) | d.ftWriteLow;

        static readonly IntPtr Invalid = new IntPtr(-1);
        const int FindExInfoBasic = 1;
        const int FindExSearchNameMatch = 0;
        const int FIND_FIRST_EX_LARGE_FETCH = 2;

        /// <summary>Ruta con prefijo \\?\ (admite rutas largas).</summary>
        public static string LongPath(string path)
        {
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
            if (path.StartsWith(@"\\", StringComparison.Ordinal)) return @"\\?\UNC\" + path.Substring(2);
            return @"\\?\" + path;
        }

        public delegate void EntryHandler(ref WIN32_FIND_DATA data);

        /// <summary>Recorre las entradas de una carpeta (sin "." ni ".."). false si no se pudo abrir.</summary>
        public static bool Enumerate(string directory, EntryHandler onEntry)
        {
            string pattern = LongPath(directory.TrimEnd('\\')) + @"\*";
            WIN32_FIND_DATA d;
            IntPtr h = FindFirstFileExW(pattern, FindExInfoBasic, out d, FindExSearchNameMatch, IntPtr.Zero, FIND_FIRST_EX_LARGE_FETCH);
            if (h == Invalid) return false;
            try
            {
                do
                {
                    string n = d.cFileName;
                    if (string.IsNullOrEmpty(n) || n == "." || n == "..") continue;
                    onEntry(ref d);
                }
                while (FindNextFileW(h, out d));
            }
            finally { FindClose(h); }
            return true;
        }
    }
}
