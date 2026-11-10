using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Velox.Localization;

namespace Velox.Core
{
    /// <summary>
    /// Lo que Velox nunca borra ni desinstala. Lo usan el explorador de disco (enviar a la papelera), la limpieza de
    /// restos del desinstalador y el propio desinstalador. Ante la duda, se protege.
    /// </summary>
    public static class Protection
    {
        // ================================================================ Carpetas y archivos

        static List<string> _keyFolders;
        static readonly object Gate = new object();

        /// <summary>Carpetas clave: no se pueden borrar ni ellas ni ninguna carpeta que las contenga.</summary>
        public static List<string> KeyFolders
        {
            get
            {
                lock (Gate)
                {
                    if (_keyFolders == null) _keyFolders = BuildKeyFolders();
                    return _keyFolders;
                }
            }
        }

        static List<string> BuildKeyFolders()
        {
            var l = new List<string>();
            void Add(string p) { if (!string.IsNullOrWhiteSpace(p)) { try { l.Add(Norm(p)); } catch { } } }
            void Sf(Environment.SpecialFolder f) => Add(Environment.GetFolderPath(f));

            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string sys = Path.GetPathRoot(win) ?? "C:\\";
            Add(win);
            foreach (var f in new[]
            {
                Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.CommonProgramFiles, Environment.SpecialFolder.CommonProgramFilesX86,
                Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.UserProfile,
                Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolder.Desktop, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.MyPictures,
                Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.MyVideos, Environment.SpecialFolder.Favorites,
                Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.CommonStartMenu, Environment.SpecialFolder.Startup,
                Environment.SpecialFolder.CommonDesktopDirectory, Environment.SpecialFolder.CommonDocuments, Environment.SpecialFolder.Fonts,
            }) Sf(f);
            Add(Environment.GetEnvironmentVariable("ProgramW6432"));
            Add(Environment.GetEnvironmentVariable("OneDrive"));
            foreach (var id in new[] { DownloadsId, SavedGamesId, LinksId, ContactsId, SearchesId, ObjectsId }) Add(KnownFolder(id));

            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            Add(Path.Combine(profile, "AppData"));
            Add(Path.Combine(profile, "AppData", "LocalLow"));
            Add(Path.Combine(local, "Microsoft"));
            Add(Path.Combine(local, "Packages"));
            Add(Path.Combine(local, "Programs"));
            Add(Path.Combine(local, "Temp"));
            Add(Path.Combine(roaming, "Microsoft"));
            Add(Path.Combine(pd, "Microsoft"));
            Add(Path.Combine(pd, "Packages"));
            Add(Path.Combine(pd, "Package Cache"));
            Add(Path.Combine(pf, "WindowsApps"));
            Add(Path.Combine(pf, "Windows Defender"));
            Add(Path.Combine(pf, "Windows NT"));
            Add(Path.Combine(pf, "Microsoft Update Health Tools"));
            Add(Path.Combine(pf86, "Microsoft"));
            Add(Path.Combine(sys, "Users"));
            Add(Path.Combine(sys, "Users", "Public"));
            Add(Path.Combine(sys, "Users", "Default"));
            foreach (var n in new[] { "Recovery", "Boot", "EFI", "$WinREAgent", "Config.Msi", "$Windows.~BT", "$Windows.~WS", "Documents and Settings", "ProgramData" })
                Add(Path.Combine(sys, n));
            try { if (AppPaths.DataDir != null) Add(AppPaths.DataDir); } catch { } // copias de seguridad de Velox
            Add(Path.Combine(pd, "Velox"));
            return l.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Nombres protegidos en cualquier unidad
        static readonly HashSet<string> ProtectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System Volume Information", "$Recycle.Bin", "$WinREAgent", "$Windows.~BT", "$Windows.~WS", "Config.Msi", "$SysReset", "$GetCurrent",
            "pagefile.sys", "hiberfil.sys", "swapfile.sys", "bootmgr", "BOOTNXT", "BOOTSECT.BAK",
        };

        /// <summary>
        /// Motivo (ya traducido) por el que no se puede borrar esa ruta, o null si se puede.
        /// installedFolders: carpetas de programas instalados (borrarlas a mano los rompe: hay que desinstalarlos).
        /// </summary>
        public static string PathReason(string path, IEnumerable<string> installedFolders = null)
        {
            if (string.IsNullOrWhiteSpace(path)) return Loc.T("protect.path.invalid");
            string p;
            try { p = Norm(path); } catch { return Loc.T("protect.path.invalid"); }

            // Raíz de una unidad
            if (p.Length <= 3 || string.Equals(p, Norm(Path.GetPathRoot(p) ?? p), StringComparison.OrdinalIgnoreCase)) return Loc.T("protect.path.drive");

            string name = Path.GetFileName(p);
            if (ProtectedNames.Contains(name)) return Loc.T("protect.path.system");

            string win = Norm(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            if (IsUnder(p, win)) return Loc.T("protect.path.system");

            string usersRoot = Path.Combine(Path.GetPathRoot(win) ?? "C:\\", "Users"); // «C:\» con la barra: Path.Combine("C:", ..) daría «C:Users»
            // Perfiles de usuario (C:\Users\Nombre), de cualquier usuario
            if (string.Equals(Norm(Path.GetDirectoryName(p) ?? ""), usersRoot, StringComparison.OrdinalIgnoreCase)) return Loc.T("protect.path.profile");

            foreach (var k in KeyFolders)
            {
                if (string.Equals(p, k, StringComparison.OrdinalIgnoreCase)) return Loc.T("protect.path.key");
                if (k.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase)) return Loc.T("protect.path.contains");
            }

            // Dentro de carpetas que gestiona Windows (apps de la Store, Microsoft, datos de Velox)
            foreach (var inside in new[]
            {
                Path.Combine(Norm(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)), "WindowsApps"),
                Path.Combine(Norm(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)), "Microsoft"),
                Path.Combine(Norm(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)), "Packages"),
            })
                if (IsUnder(p, inside)) return Loc.T("protect.path.system");
            // Datos de Velox (copias para deshacer): la carpeta configurada y la estándar
            if (IsUnder(p, Path.Combine(Norm(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)), "Velox"))) return Loc.T("protect.path.velox");
            try { if (AppPaths.DataDir != null && IsUnder(p, Norm(AppPaths.DataDir))) return Loc.T("protect.path.velox"); } catch { }

            if (installedFolders != null)
                foreach (var f in installedFolders)
                {
                    string n;
                    try { n = Norm(f); } catch { continue; }
                    if (n.Length <= 3) continue;
                    if (string.Equals(p, n, StringComparison.OrdinalIgnoreCase) || n.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase))
                        return Loc.T("protect.path.installed");
                }
            return null;
        }

        static string Norm(string p) => Path.GetFullPath(Environment.ExpandEnvironmentVariables(p.Trim().Trim('"'))).TrimEnd('\\');

        static bool IsUnder(string path, string folder) =>
            string.Equals(path, folder, StringComparison.OrdinalIgnoreCase) || path.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase);

        // ---------------------------------------------------------------- Carpetas conocidas (Descargas, Juegos guardados...)

        static readonly Guid DownloadsId = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        static readonly Guid SavedGamesId = new Guid("4C5C32FF-BB9D-43B0-B5B4-2D72E54EAAA4");
        static readonly Guid LinksId = new Guid("BFB9D5E0-C6A9-404C-B2B2-AE6DB6AF4968");
        static readonly Guid ContactsId = new Guid("56784854-C6CB-462B-8169-88E350ACB882");
        static readonly Guid SearchesId = new Guid("7D1D3A04-DEBB-4115-95CF-2F29DA2920DA");
        static readonly Guid ObjectsId = new Guid("31C0DD25-9439-4F12-BF41-7FF4EDA38722");

        [DllImport("shell32.dll")]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        static string KnownFolder(Guid id)
        {
            try
            {
                if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out IntPtr p) != 0) return null;
                try { return Marshal.PtrToStringUni(p); } finally { Marshal.FreeCoTaskMem(p); }
            }
            catch { return null; }
        }

        // ================================================================ Programas

        // Componentes que necesitan otros programas o Windows: quitarlos rompe cosas sin avisar
        static readonly Regex RuntimeName = new Regex(
            @"Visual C\+\+.*(Redistributable|Runtime)|Microsoft Visual C\+\+ \d{4}|VC_?Redist|" +
            @"\.NET( Core)?( \d+(\.\d+)*)? (Runtime|Desktop Runtime|Windows Desktop Runtime|Host|Hosting)|ASP\.NET Core.*(Runtime|Shared Framework)|\.NET Framework|" +
            @"DirectX|WebView2|Windows App Runtime|WindowsAppRuntime|Microsoft Update Health Tools|Microsoft Edge Update|" +
            @"Windows Desktop Extension|UWPDesktop|Microsoft GameInput|Windows PC Health Check",
            RegexOptions.IgnoreCase);

        // Drivers y software del hardware: se gestionan desde «Controladores» (actualizar o volver atrás), no borrándolos
        static readonly Regex DriverName = new Regex(
            @"(?<!Display )\bDrivers?\b(?! ?(Booster|Easy|Pack|Genius|Updater|Talent|Max|Support|Fusion|Magic|Uninstaller|Store Explorer))|Chipset|Management Engine|Rapid Storage|Serial IO|Guest Additions|VMware Tools|Hyper-V Integration|" +
            @"NVIDIA (Graphics|HD Audio|PhysX|FrameView|USB)|AMD Software|Radeon Software|Realtek.*Audio|Intel\(R\) .*(Graphics|Wireless|Bluetooth|Ethernet)",
            RegexOptions.IgnoreCase);

        static readonly Regex SystemName = new Regex(@"^Microsoft Edge$|^Microsoft Windows\b|^Windows (Security|Defender)\b", RegexOptions.IgnoreCase);

        // Apps de la Store imprescindibles (familia del paquete)
        static readonly Regex StoreFamily = new Regex(
            @"^Microsoft\.(DesktopAppInstaller|WindowsStore|StorePurchaseApp|SecHealthUI|Windows\.ShellExperienceHost|Windows\.StartMenuExperienceHost|" +
            @"WindowsTerminal|VCLibs|NET\.Native|UI\.Xaml|WindowsAppRuntime|Services\.Store\.Engagement|GamingServices|XboxIdentityProvider)",
            RegexOptions.IgnoreCase);

        /// <summary>Motivo (ya traducido) por el que no se puede desinstalar, o null si se puede.</summary>
        public static string ProgramReason(string name, string publisher, string storeFamily)
        {
            name = name ?? "";
            if (name.IndexOf("Velox", StringComparison.OrdinalIgnoreCase) >= 0 && (publisher ?? "").IndexOf("Velox", StringComparison.OrdinalIgnoreCase) >= 0)
                return Loc.T("protect.prog.velox");
            if (!string.IsNullOrEmpty(storeFamily) && StoreFamily.IsMatch(storeFamily)) return Loc.T("protect.prog.store");
            if (RuntimeName.IsMatch(name)) return Loc.T("protect.prog.runtime");
            if (DriverName.IsMatch(name)) return Loc.T("protect.prog.driver");
            if (SystemName.IsMatch(name)) return Loc.T("protect.prog.system");
            return null;
        }
    }
}
