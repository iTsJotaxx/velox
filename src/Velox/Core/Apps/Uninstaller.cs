using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
using Velox.Core.Drivers;
using Velox.Localization;

namespace Velox.Core.Apps
{
    public enum ProgramKind { Desktop, Store }

    /// <summary>Programa instalado (entrada de «Aplicaciones instaladas» de Windows).</summary>
    public sealed class InstalledProgram
    {
        public ProgramKind Kind { get; set; }
        /// <summary>Clave de registro completa (escritorio) o PackageFullName (Store).</summary>
        public string Key { get; set; }
        public string Name { get; set; }
        public string Publisher { get; set; }
        public string Version { get; set; }
        public DateTime? InstallDate { get; set; }
        /// <summary>Bytes (0 = desconocido).</summary>
        public long Size { get; set; }
        public string UninstallString { get; set; }
        public string QuietUninstallString { get; set; }
        public string ProductCode { get; set; }
        public string InstallLocation { get; set; }
        public string IconPath { get; set; }
        public int IconIndex { get; set; }
        /// <summary>Componente que usan otros programas (Visual C++, .NET, DirectX...): mejor no quitarlo.</summary>
        public bool IsShared { get; set; }

        public bool IsMsi => !string.IsNullOrEmpty(ProductCode);
        /// <summary>Se puede quitar sin ventanas (silencioso, MSI o Store).</summary>
        public bool IsQuiet => Kind == ProgramKind.Store || IsMsi || !string.IsNullOrWhiteSpace(QuietUninstallString)
                               || Regex.IsMatch(UninstallString ?? "", @"unins\d{3}\.exe", RegexOptions.IgnoreCase)
                               || Uninstaller.IsWingetCommand(UninstallString, out _);
    }

    public enum UninstallOutcome { Removed, NotRemoved, Failed }

    public sealed class UninstallResult
    {
        public UninstallOutcome Outcome { get; set; }
        public string Error { get; set; }
        /// <summary>Carpeta de instalación que sigue existiendo después (restos), o null.</summary>
        public string Leftover { get; set; }
        public long LeftoverSize { get; set; }
    }

    /// <summary>Lista y desinstala programas: los de escritorio (registro Uninstall) y los de Microsoft Store.</summary>
    public static class Uninstaller
    {
        const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        static readonly Regex Guid = new Regex(@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}");
        static readonly Regex SharedName = new Regex(@"Visual C\+\+|\.NET (Desktop |Windows Desktop |ASP\.NET Core )?Runtime|\.NET Framework|DirectX|Windows SDK|Windows App Runtime|WebView2|VC\+\+ ?Redist|Microsoft Update Health|Java\(TM\)|Temurin", RegexOptions.IgnoreCase);

        // ------------------------------------------------------------ Escritorio

        public static List<InstalledProgram> GetDesktop()
        {
            var list = new List<InstalledProgram>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Read(RegistryHive.LocalMachine, RegistryView.Registry64, list, seen);
            Read(RegistryHive.LocalMachine, RegistryView.Registry32, list, seen);
            Read(RegistryHive.CurrentUser, RegistryView.Registry64, list, seen);
            Read(RegistryHive.CurrentUser, RegistryView.Registry32, list, seen);
            Log.Info("Uninstaller: " + list.Count + " desktop program(s).");
            return list;
        }

        static void Read(RegistryHive hive, RegistryView view, List<InstalledProgram> list, HashSet<string> seen)
        {
            try
            {
                using (var b = RegistryKey.OpenBaseKey(hive, view))
                using (var root = b.OpenSubKey(UninstallKey))
                {
                    if (root == null) return;
                    foreach (var sub in root.GetSubKeyNames())
                    {
                        try
                        {
                            using (var k = root.OpenSubKey(sub))
                            {
                                if (k == null) continue;
                                string name = (k.GetValue("DisplayName") as string ?? "").Trim();
                                string un = (k.GetValue("UninstallString") as string ?? "").Trim();
                                if (name.Length == 0 || un.Length == 0) continue;
                                if (Convert.ToInt32(k.GetValue("SystemComponent") ?? 0) == 1) continue;
                                if (k.GetValue("ParentKeyName") != null) continue; // actualizaciones de otro programa
                                string rt = k.GetValue("ReleaseType") as string ?? "";
                                if (rt.IndexOf("Update", StringComparison.OrdinalIgnoreCase) >= 0 || rt.IndexOf("Hotfix", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                                if (Regex.IsMatch(name, @"^(Security Update|Update for|Hotfix for) ", RegexOptions.IgnoreCase)) continue;

                                string version = (k.GetValue("DisplayVersion") as string ?? "").Trim();
                                if (!seen.Add(name + "|" + version)) continue; // la misma en 32 y 64 bits

                                var p = new InstalledProgram
                                {
                                    Kind = ProgramKind.Desktop,
                                    Key = (hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU") + (view == RegistryView.Registry32 ? "32" : "64") + "\\" + UninstallKey + "\\" + sub,
                                    Name = name,
                                    Publisher = (k.GetValue("Publisher") as string ?? "").Trim(),
                                    Version = version,
                                    InstallDate = ParseDate(k.GetValue("InstallDate") as string),
                                    UninstallString = un,
                                    QuietUninstallString = (k.GetValue("QuietUninstallString") as string ?? "").Trim(),
                                    InstallLocation = (k.GetValue("InstallLocation") as string ?? "").Trim().Trim('"'),
                                };
                                object est = k.GetValue("EstimatedSize");
                                if (est is int kb && kb > 0) p.Size = kb * 1024L;
                                // MSI: el producto es el GUID de la clave (y WindowsInstaller = 1)
                                bool msi = Convert.ToInt32(k.GetValue("WindowsInstaller") ?? 0) == 1 || un.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase) >= 0;
                                if (msi && Guid.IsMatch(sub)) p.ProductCode = Guid.Match(sub).Value;
                                ParseIcon(k.GetValue("DisplayIcon") as string, p);
                                p.IsShared = SharedName.IsMatch(name);
                                list.Add(p);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { Log.Warn("Uninstall list " + hive + "/" + view + ": " + ex.Message); }
        }

        static DateTime? ParseDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return DateTime.TryParseExact(s.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null;
        }

        // "C:\x\app.exe,0" o "\"C:\x\app.exe\",-101"
        static void ParseIcon(string s, InstalledProgram p)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            s = Environment.ExpandEnvironmentVariables(s.Trim());
            int idx = 0;
            var m = Regex.Match(s, @"^(.*?),\s*(-?\d+)$");
            if (m.Success) { s = m.Groups[1].Value; int.TryParse(m.Groups[2].Value, out idx); }
            s = s.Trim().Trim('"');
            if (s.Length > 0) { p.IconPath = s; p.IconIndex = idx; }
        }

        /// <summary>¿Sigue registrada (no se ha desinstalado)?</summary>
        public static bool StillInstalled(InstalledProgram p)
        {
            if (p.Kind == ProgramKind.Store) return GetStoreFullNames().Contains(p.Key);
            string k = p.Key;
            var hive = k.StartsWith("HKLM", StringComparison.Ordinal) ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
            var view = k.Substring(4, 2) == "32" ? RegistryView.Registry32 : RegistryView.Registry64;
            string sub = k.Substring(k.IndexOf('\\') + 1);
            try
            {
                using (var b = RegistryKey.OpenBaseKey(hive, view))
                using (var key = b.OpenSubKey(sub))
                    return key != null;
            }
            catch { return true; }
        }

        // ------------------------------------------------------------ Microsoft Store

        const string StoreListScript =
            "$ErrorActionPreference='SilentlyContinue'; $ProgressPreference='SilentlyContinue';" +
            "$m=@{}; foreach($a in Get-StartApps){ if($a.AppID -like '*!*'){ $f=$a.AppID.Split('!')[0]; if(-not $m.ContainsKey($f)){ $m[$f]=$a.Name } } };" +
            "$r=@(Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable -and $_.SignatureKind -ne 'System' -and $m.ContainsKey($_.PackageFamilyName) } | " +
            "ForEach-Object { [pscustomobject]@{ N=$m[$_.PackageFamilyName]; F=$_.PackageFullName; V=[string]$_.Version; P=($_.Publisher -replace '^CN=\"?([^,\"]+)\"?.*','$1'); L=$_.InstallLocation; S=[string]$_.SignatureKind; Fam=$_.PackageFamilyName } });" +
            "[Console]::OutputEncoding=[Text.Encoding]::UTF8; ConvertTo-Json -InputObject $r -Compress";

        public static List<InstalledProgram> GetStore()
        {
            var list = new List<InstalledProgram>();
            try
            {
                string json = RunPowerShell(StoreListScript, TimeSpan.FromSeconds(60), out int code);
                if (string.IsNullOrWhiteSpace(json)) return list;
                if (MiniJson.Parse(json.Trim()) is IEnumerable arr)
                    foreach (var o in arr)
                        if (o is Dictionary<string, object> d)
                        {
                            string Get(string n) => d.TryGetValue(n, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : "";
                            string full = Get("F");
                            if (full.Length == 0 || Get("N").Length == 0) continue;
                            // Publicadores de Windows sin interés para el usuario (sistema) ya los filtra NonRemovable/SignatureKind
                            string pub = Get("P").Trim('"', ' ');
                            if (Regex.IsMatch(pub, @"^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-")) pub = ""; // editor con identificador interno
                            string fam = Get("Fam");
                            list.Add(new InstalledProgram
                            {
                                Kind = ProgramKind.Store,
                                Key = full,
                                Name = Get("N"),
                                Publisher = pub,
                                // Sin «Instalador de aplicaciones» deja de funcionar winget (y la sección Instalar aplicaciones)
                                IsShared = Regex.IsMatch(fam, @"^Microsoft\.(DesktopAppInstaller|WindowsStore|StorePurchaseApp)_", RegexOptions.IgnoreCase),
                                Version = Get("V"),
                                InstallLocation = Get("L"),
                            });
                        }
                Log.Info("Uninstaller: " + list.Count + " Store app(s).");
            }
            catch (Exception ex) { Log.Warn("Store apps: " + ex.Message); }
            return list;
        }

        static HashSet<string> GetStoreFullNames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string txt = RunPowerShell("$ProgressPreference='SilentlyContinue'; [Console]::OutputEncoding=[Text.Encoding]::UTF8; (Get-AppxPackage).PackageFullName", TimeSpan.FromSeconds(60), out _);
            foreach (var l in (txt ?? "").Split('\r', '\n')) if (l.Trim().Length > 0) set.Add(l.Trim());
            return set;
        }

        static string RunPowerShell(string script, TimeSpan timeout, out int code)
        {
            string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + enc)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            using (var p = Process.Start(psi))
            {
                var outTask = p.StandardOutput.ReadToEndAsync();
                var errTask = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit((int)timeout.TotalMilliseconds)) { try { p.Kill(); } catch { } code = -1; return null; }
                code = p.ExitCode;
                string err = errTask.Result;
                if (!string.IsNullOrWhiteSpace(err) && !err.TrimStart().StartsWith("#< CLIXML")) Log.Warn("PowerShell: " + err.Trim().Split('\n')[0]);
                return outTask.Result;
            }
        }

        // ------------------------------------------------------------ Desinstalar

        /// <summary>
        /// Quita el programa. Store: Remove-AppxPackage. MSI: msiexec /x (solo barra de progreso). Con desinstalación
        /// silenciosa: esa. Si no, se abre el desinstalador del propio programa (el usuario puede cancelarlo) y se espera.
        /// </summary>
        public static UninstallResult Uninstall(InstalledProgram p, Action<string> onStatus, CancellationToken ct)
        {
            var res = new UninstallResult();
            Log.Info("Uninstall: " + p.Name + " (" + p.Kind + (p.IsMsi ? ", MSI " + p.ProductCode : "") + ")...");
            try
            {
                if (p.Kind == ProgramKind.Store)
                {
                    onStatus?.Invoke(Loc.T("unin.status.removing"));
                    string name = p.Key.Replace("'", "''");
                    RunPowerShell("$ProgressPreference='SilentlyContinue'; Remove-AppxPackage -Package '" + name + "' -ErrorAction Stop", TimeSpan.FromMinutes(10), out int code);
                    if (code != 0 && StillInstalled(p)) { res.Outcome = UninstallOutcome.Failed; res.Error = Loc.T("unin.err.store"); return res; }
                }
                else if (!p.IsMsi && string.IsNullOrWhiteSpace(p.QuietUninstallString) && IsWingetCommand(p.UninstallString, out string wgArgs))
                {
                    // Programas portables instalados con winget: su desinstalación es «winget uninstall ...»
                    // (si son del usuario, winget exige quitarlos sin permisos de administrador: se hace como usuario)
                    onStatus?.Invoke(Loc.T("unin.status.removing"));
                    int code = Winget.RunUninstallCommand(wgArgs + " --silent --accept-source-agreements --disable-interactivity", TimeSpan.FromMinutes(15));
                    if (code != 0) Log.Warn("Uninstall: winget exit code 0x" + code.ToString("X8") + ".");
                }
                else
                {
                    string exe, args; bool interactive;
                    if (p.IsMsi) { exe = "msiexec.exe"; args = "/x " + p.ProductCode + " /passive /norestart"; interactive = false; }
                    else if (!string.IsNullOrWhiteSpace(p.QuietUninstallString)) { Split(p.QuietUninstallString, out exe, out args); interactive = false; }
                    else
                    {
                        Split(p.UninstallString, out exe, out args);
                        interactive = true;
                        // Inno Setup (unins000.exe) admite quitarse en silencio aunque el programa no lo declare
                        if (Regex.IsMatch(Path.GetFileName(exe) ?? "", @"^unins\d{3}\.exe$", RegexOptions.IgnoreCase))
                        {
                            args = (args + " /VERYSILENT /SUPPRESSMSGBOXES /NORESTART").Trim();
                            interactive = false;
                        }
                    }

                    onStatus?.Invoke(Loc.T(interactive ? "unin.status.interactive" : "unin.status.removing"));
                    var started = DateTime.Now;
                    using (var proc = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, WorkingDirectory = SafeDir(exe) }))
                    {
                        var sw = Stopwatch.StartNew();
                        while (!proc.WaitForExit(500))
                        {
                            if (sw.Elapsed > TimeSpan.FromMinutes(30)) { res.Outcome = UninstallOutcome.Failed; res.Error = Loc.T("unin.err.timeout"); return res; }
                        }
                        if (p.IsMsi && proc.ExitCode != 0 && proc.ExitCode != 3010 && proc.ExitCode != 1605)
                            Log.Warn("Uninstall: msiexec exit code " + proc.ExitCode + ".");
                    }
                    // Muchos desinstaladores (NSIS, Inno...) se copian a TEMP, se relanzan y el primero termina enseguida:
                    // se espera mientras siga desapareciendo o haya un proceso de desinstalación activo
                    WaitForUninstaller(p, started, onStatus);
                }

                if (StillInstalled(p)) { res.Outcome = UninstallOutcome.NotRemoved; Log.Info("Uninstall: " + p.Name + " is still installed (cancelled or failed)."); }
                else
                {
                    res.Outcome = UninstallOutcome.Removed;
                    Log.Info("Uninstall: " + p.Name + " removed.");
                    FindLeftover(p, res);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Uninstall " + p.Name, ex);
                res.Outcome = UninstallOutcome.Failed;
                res.Error = ex.Message;
            }
            return res;
        }

        static readonly Regex WingetCommand = new Regex(@"^\s*""?winget(\.exe)?""?\s+(uninstall\s+.+)$", RegexOptions.IgnoreCase);

        internal static bool IsWingetCommand(string cmd, out string args)
        {
            var m = WingetCommand.Match(cmd ?? "");
            args = m.Success ? m.Groups[2].Value.Trim() : null;
            return m.Success;
        }

        static readonly Regex UninstallerProcess = new Regex(@"^(Au_|Un_A|un_|unins\d*|uninst|uninstall|_iu\w+|setup|Update)", RegexOptions.IgnoreCase);

        static void WaitForUninstaller(InstalledProgram p, DateTime started, Action<string> onStatus)
        {
            var sw = Stopwatch.StartNew();
            int idle = 0;
            while (sw.Elapsed < TimeSpan.FromMinutes(30))
            {
                if (!StillInstalled(p)) return;
                bool active = false;
                foreach (var pr in Process.GetProcesses())
                {
                    using (pr)
                    {
                        try
                        {
                            if (!UninstallerProcess.IsMatch(pr.ProcessName) && pr.ProcessName.IndexOf("uninst", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            if (pr.StartTime >= started.AddSeconds(-2)) { active = true; break; }
                        }
                        catch { }
                    }
                }
                idle = active ? 0 : idle + 1;
                if (idle >= 6) return; // 3 s sin desinstalador en marcha
                Thread.Sleep(500);
            }
        }

        static string SafeDir(string exe)
        {
            try { string d = Path.GetDirectoryName(exe); return Directory.Exists(d) ? d : Environment.GetFolderPath(Environment.SpecialFolder.System); }
            catch { return Environment.GetFolderPath(Environment.SpecialFolder.System); }
        }

        /// <summary>Separa «"C:\a b\x.exe" /S» o «C:\a b\x.exe /S» en programa y argumentos.</summary>
        internal static void Split(string cmd, out string exe, out string args)
        {
            cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                exe = end > 0 ? cmd.Substring(1, end - 1) : cmd.Trim('"');
                args = end > 0 ? cmd.Substring(end + 1).Trim() : "";
                return;
            }
            int i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (i > 0)
            {
                exe = cmd.Substring(0, i + 4);
                args = cmd.Substring(i + 4).Trim();
                return;
            }
            int sp = cmd.IndexOf(' ');
            exe = sp > 0 ? cmd.Substring(0, sp) : cmd;
            args = sp > 0 ? cmd.Substring(sp + 1) : "";
        }

        // ------------------------------------------------------------ Restos

        /// <summary>Si la carpeta de instalación sigue existiendo y es segura de borrar, la anota como resto.</summary>
        static void FindLeftover(InstalledProgram p, UninstallResult res)
        {
            try
            {
                string dir = p.InstallLocation;
                if (string.IsNullOrWhiteSpace(dir) || p.Kind == ProgramKind.Store) return;
                dir = Path.GetFullPath(Environment.ExpandEnvironmentVariables(dir)).TrimEnd('\\');
                if (!IsSafeLeftover(dir)) return;
                // Muchos desinstaladores borran su propia carpeta unos segundos después de terminar: se les da tiempo
                for (int i = 0; i < 16 && Directory.Exists(dir); i++) Thread.Sleep(500);
                if (!Directory.Exists(dir)) return;
                res.Leftover = dir;
                res.LeftoverSize = FolderSize(dir);
                Log.Info("Uninstall: leftover folder " + dir + " (" + Disk.Format.Bytes(res.LeftoverSize) + ").");
            }
            catch { }
        }

        /// <summary>Solo carpetas propias de un programa: dentro de Archivos de programa, ProgramData o AppData y nunca esas raíces.</summary>
        public static bool IsSafeLeftover(string dir)
        {
            dir = dir.TrimEnd('\\');
            var roots = new[]
            {
                Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData,
                Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData,
            }.Select(f => Environment.GetFolderPath(f).TrimEnd('\\')).Where(r => r.Length > 3).ToList();
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"));
            foreach (var r in roots)
            {
                if (string.Equals(dir, r, StringComparison.OrdinalIgnoreCase)) return false;
            }
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
            if (dir.StartsWith(win + "\\", StringComparison.OrdinalIgnoreCase)) return false;
            // «Microsoft», «Common Files»... son compartidas
            string leaf = Path.GetFileName(dir);
            if (Regex.IsMatch(leaf, @"^(Microsoft|Common Files|Windows.*|WindowsApps|Package Cache|Packages|Temp|Programs)$", RegexOptions.IgnoreCase)) return false;
            return roots.Any(r => dir.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase));
        }

        static long FolderSize(string dir)
        {
            long total = 0;
            try
            {
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories))
                    try { total += f.Length; } catch { }
            }
            catch { }
            return total;
        }

        /// <summary>Envía la carpeta de restos a la papelera (solo si sigue siendo segura).</summary>
        public static void RecycleLeftover(string dir)
        {
            if (!IsSafeLeftover(dir)) throw new InvalidOperationException(Loc.T("disk.del.system"));
            if (!Directory.Exists(dir)) return; // ya la borró el propio desinstalador
            RecycleFolder(dir);
        }

        // Papelera mediante el mismo mecanismo que el explorador de disco
        static void RecycleFolder(string dir)
        {
            var op = new SHFILEOPSTRUCT { wFunc = 3, pFrom = dir + "\0\0", fFlags = 0x0040 | 0x0010 | 0x0004 | 0x0400 };
            int rc = SHFileOperation(ref op);
            if (rc != 0 || op.fAnyOperationsAborted) throw new InvalidOperationException(Loc.F("disk.del.failed", "0x" + rc.ToString("X")));
            Log.Info("Leftover sent to Recycle Bin: " + dir + ".");
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd; public uint wFunc; public string pFrom; public string pTo; public ushort fFlags;
            public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string lpszProgressTitle;
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
    }
}
