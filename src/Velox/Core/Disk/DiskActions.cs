using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using Velox.Localization;

namespace Velox.Core.Disk
{
    /// <summary>Acciones sobre los elementos del explorador de disco: abrir, mostrar en el Explorador y enviar a la papelera.</summary>
    public static class DiskActions
    {
        public static void Open(DiskNode n)
        {
            string p = n.FullPath;
            if (n.IsDir || n.IsAggregate) Process.Start("explorer.exe", "\"" + p + "\"");
            else Process.Start(new ProcessStartInfo(p) { UseShellExecute = true });
        }

        public static void ShowInExplorer(DiskNode n)
        {
            if (n.IsAggregate || n.Parent == null) Process.Start("explorer.exe", "\"" + n.FullPath + "\"");
            else Process.Start("explorer.exe", "/select,\"" + n.FullPath + "\"");
        }

        /// <summary>Motivo (ya traducido) por el que no se permite borrar algo, o null si se puede.</summary>
        public static string ProtectedReason(DiskNode n)
        {
            if (n == null || n.IsAggregate) return Loc.T("disk.del.notAllowed");
            if (n.Parent == null) return Loc.T("disk.del.root");
            // Carpetas clave de Windows y del usuario, las que las contienen y las de programas instalados
            return Protection.PathReason(n.FullPath, InstalledFolders());
        }

        static List<string> _installed;
        static DateTime _installedAt;

        /// <summary>Carpetas de los programas instalados (se relee como mucho cada 2 minutos).</summary>
        static List<string> InstalledFolders()
        {
            if (_installed == null || (DateTime.Now - _installedAt).TotalMinutes > 2)
            {
                try
                {
                    _installed = Apps.Uninstaller.GetDesktop().Select(p => p.InstallLocation).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                }
                catch { _installed = new List<string>(); }
                _installedAt = DateTime.Now;
            }
            return _installed;
        }

        // ---------------------------------------------------------------- Papelera (SHFileOperation, sin ventanas)

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

        const uint FO_DELETE = 3;
        const ushort FOF_SILENT = 0x0004, FOF_NOCONFIRMATION = 0x0010, FOF_ALLOWUNDO = 0x0040, FOF_NOERRORUI = 0x0400, FOF_WANTNUKEWARNING = 0x4000;

        /// <summary>Envía el elemento a la papelera. Lanza excepción con el motivo si no se pudo.</summary>
        public static void Recycle(DiskNode n)
        {
            string reason = ProtectedReason(n);
            if (reason != null) throw new InvalidOperationException(reason);
            string p = n.FullPath;
            if (!File.Exists(p) && !Directory.Exists(p)) throw new InvalidOperationException(Loc.T("disk.del.missing"));
            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = p + "\0\0",
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI),
            };
            int rc = SHFileOperation(ref op);
            if (rc != 0 || op.fAnyOperationsAborted)
                throw new InvalidOperationException(Loc.F("disk.del.failed", "0x" + rc.ToString("X")));
            if (File.Exists(p) || Directory.Exists(p))
                throw new InvalidOperationException(Loc.T("disk.del.partial"));
            Log.Info("Sent to Recycle Bin: " + p + " (" + Format.Bytes(n.Size) + ").");
        }
    }
}
