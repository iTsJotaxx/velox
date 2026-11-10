using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Velox.Core.Drivers;
using Velox.Localization;

namespace Velox.Core.Apps
{
    public enum AppInstallOutcome { Installed, Updated, UpToDate, RebootNeeded, Failed, Cancelled }

    public sealed class AppInstallResult
    {
        public AppInstallOutcome Outcome { get; set; }
        /// <summary>Motivo ya traducido si falló.</summary>
        public string Error { get; set; }
        public int ExitCode { get; set; }
    }

    public sealed class WingetSearchHit
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public string Version { get; set; }
    }

    /// <summary>
    /// Instalación de aplicaciones con winget (el gestor de paquetes de Windows): descarga desde la web oficial de cada
    /// programa, comprueba el SHA-256 del instalador con el manifiesto del repositorio de Microsoft y lo instala en silencio.
    /// Solo se usa el origen «winget» (nunca otros orígenes que el usuario haya añadido).
    /// </summary>
    public static class Winget
    {
        // Códigos de salida de winget (HRESULT 0x8A15xxxx)
        const int UpdateNotApplicable = unchecked((int)0x8A15002B);   // ya está en la última versión
        const int PackageAlreadyInstalled = unchecked((int)0x8A150061);
        const int RebootRequiredToFinish = unchecked((int)0x8A150109);
        const int RebootRequiredForInstall = unchecked((int)0x8A15010A);
        const int NoApplicablePackage = unchecked((int)0x8A150014);   // no existe en el origen
        const int InstallerFailed = unchecked((int)0x8A150010);       // el instalador del programa devolvió error
        const int HashMismatch = unchecked((int)0x8A150011);
        static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(30);

        static string _path;

        /// <summary>Ruta de winget.exe, o null si este Windows no lo tiene (falta «Instalador de aplicaciones»).</summary>
        public static string Path
        {
            get
            {
                if (_path != null) return _path.Length == 0 ? null : _path;
                string p = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");
                if (!File.Exists(p))
                {
                    p = null;
                    foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                    {
                        try
                        {
                            string c = System.IO.Path.Combine(dir.Trim(), "winget.exe");
                            if (dir.Trim().Length > 0 && File.Exists(c)) { p = c; break; }
                        }
                        catch { }
                    }
                }
                _path = p ?? "";
                return p;
            }
        }

        public static bool IsAvailable => Path != null;

        const string Common = "--source winget --accept-source-agreements --disable-interactivity";

        // ------------------------------------------------------------ Ejecución

        sealed class RunResult { public int Code; public List<string> Lines = new List<string>(); public bool TimedOut; }

        static RunResult Run(string args, TimeSpan timeout, CancellationToken ct, Action<string> onLine = null, bool allowCancel = false)
        {
            string exe = Path ?? throw new InvalidOperationException(Loc.T("apps.err.noWinget"));
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            var res = new RunResult();
            using (var p = new Process { StartInfo = psi })
            {
                DataReceivedEventHandler h = (s, e) =>
                {
                    if (e.Data == null) return;
                    string line = e.Data.Trim();
                    if (line.Length == 0) return;
                    lock (res.Lines) res.Lines.Add(line);
                    onLine?.Invoke(line);
                };
                p.OutputDataReceived += h;
                p.ErrorDataReceived += h;
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                var sw = Stopwatch.StartNew();
                while (!p.WaitForExit(250))
                {
                    if (allowCancel && ct.IsCancellationRequested) { TryKill(p); throw new OperationCanceledException(ct); }
                    if (sw.Elapsed > timeout) { res.TimedOut = true; TryKill(p); break; }
                }
                if (!res.TimedOut) { p.WaitForExit(); res.Code = p.ExitCode; } // vacía la salida pendiente
            }
            return res;
        }

        static void TryKill(Process p) { try { if (!p.HasExited) p.Kill(); } catch { } }

        const int AdminContextNotAllowed = unchecked((int)0x8A150056);

        /// <summary>Ejecuta un comando de winget y, si va elevado y winget se niega por estar instalado para el usuario, lo repite como usuario.</summary>
        internal static int RunUninstallCommand(string args, TimeSpan timeout)
        {
            var r = Run(args, timeout, CancellationToken.None);
            const int UserScopeAsAdmin = unchecked((int)0x8A15007D); // instalado para el usuario: no se quita como administrador
            if (!r.TimedOut && (r.Code == UserScopeAsAdmin || r.Code == AdminContextNotAllowed) && IsElevated())
            {
                Log.Info("winget: user-scope package; running the uninstall as the signed-in user.");
                r = RunAsUser(args, timeout, null);
            }
            return r.TimedOut ? -1 : r.Code;
        }

        internal static bool IsElevated()
        {
            using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
                return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        /// <summary>
        /// Ejecuta winget SIN permisos de administrador, como el usuario que tiene la sesión abierta. Velox va elevado, así
        /// que se usa una tarea programada de un solo uso con «privilegios mínimos» y token interactivo (lo que hace Windows
        /// para abrir programas como usuario normal). La salida va a un archivo que se lee para el progreso; al acabar, la
        /// tarea y los archivos se borran.
        /// </summary>
        static RunResult RunAsUser(string args, TimeSpan timeout, Action<string> onLine)
        {
            var res = new RunResult { Code = -1 };
            string exe = Path ?? throw new InvalidOperationException(Loc.T("apps.err.noWinget"));
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Velox-winget");
            Directory.CreateDirectory(dir);
            string id = Guid.NewGuid().ToString("N");
            string log = System.IO.Path.Combine(dir, id + ".log");
            string cmd = System.IO.Path.Combine(dir, id + ".cmd");
            string codeFile = System.IO.Path.Combine(dir, id + ".code");
            string name = "Velox-UserInstall-" + id;
            File.WriteAllText(cmd, "@echo off\r\nchcp 65001 >nul\r\n\"" + exe + "\" " + args + " > \"" + log + "\" 2>&1\r\nset rc=%errorlevel%\r\n>\"" + codeFile + "\" echo %rc%\r\nexit /b %rc%\r\n", new UTF8Encoding(false));

            string user;
            using (var wi = System.Security.Principal.WindowsIdentity.GetCurrent()) user = wi.Name;
            string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
                "<Principals><Principal id=\"Author\"><UserId>" + System.Security.SecurityElement.Escape(user) + "</UserId>" +
                "<LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>" +
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled>" +
                "<Hidden>true</Hidden><ExecutionTimeLimit>PT" + (int)timeout.TotalMinutes + "M</ExecutionTimeLimit></Settings>" +
                "<Actions Context=\"Author\"><Exec><Command>conhost.exe</Command>" +
                "<Arguments>--headless cmd.exe /c \"" + System.Security.SecurityElement.Escape(cmd) + "\"</Arguments></Exec></Actions></Task>";

            dynamic folder = null;
            try
            {
                dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true));
                svc.Connect();
                folder = svc.GetFolder("\\");
                dynamic task = folder.RegisterTask(name, xml, 6 /* crear o actualizar */, null, null, 3 /* token interactivo */, null);
                task.Run(null);

                var sw = Stopwatch.StartNew();
                long read = 0;
                Thread.Sleep(1500);
                while (true)
                {
                    read = TailLog(log, read, onLine, res);
                    int state = 0;
                    try { state = (int)task.State; } catch { }
                    if (state != 4 /* en ejecución */ && sw.Elapsed > TimeSpan.FromSeconds(3)) break;
                    if (sw.Elapsed > timeout) { res.TimedOut = true; try { task.Stop(0); } catch { } break; }
                    Thread.Sleep(500);
                }
                TailLog(log, read, onLine, res);
                if (!res.TimedOut)
                {
                    // El código de salida de winget lo escribe el propio script (el de la tarea es el de conhost)
                    int code;
                    if (File.Exists(codeFile) && int.TryParse(File.ReadAllText(codeFile).Trim(), out code)) res.Code = code;
                    else { try { res.Code = (int)task.LastTaskResult; } catch { } }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("winget as user: " + ex.Message);
                res.Lines.Add(ex.Message);
            }
            finally
            {
                try { folder?.DeleteTask(name, 0); } catch { }
                try { File.Delete(cmd); } catch { }
                try { File.Delete(log); } catch { }
                try { File.Delete(codeFile); } catch { }
            }
            return res;
        }

        // Lee lo nuevo del archivo de salida (winget escribe el progreso con retornos de carro)
        static long TailLog(string log, long from, Action<string> onLine, RunResult res)
        {
            try
            {
                if (!File.Exists(log)) return from;
                using (var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length <= from) return from;
                    fs.Seek(from, SeekOrigin.Begin);
                    var buf = new byte[fs.Length - from];
                    int n = fs.Read(buf, 0, buf.Length);
                    foreach (var part in Encoding.UTF8.GetString(buf, 0, n).Split('\r', '\n'))
                    {
                        string line = part.Trim();
                        if (line.Length == 0) continue;
                        lock (res.Lines) res.Lines.Add(line);
                        onLine?.Invoke(line);
                    }
                    return from + n;
                }
            }
            catch { return from; }
        }

        // ------------------------------------------------------------ Instalado

        /// <summary>Paquetes instalados que winget reconoce: id → versión. Vacío si no se pudo leer.</summary>
        public static Dictionary<string, string> GetInstalled()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "velox-winget-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var r = Run("export -o \"" + file + "\" --include-versions " + Common, TimeSpan.FromMinutes(3), CancellationToken.None);
                if (!File.Exists(file)) { Log.Warn("winget export produced no file (code 0x" + r.Code.ToString("X8") + ")."); return map; }
                var root = MiniJson.Parse(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                if (root != null && root.TryGetValue("Sources", out var sources) && sources is IEnumerable srcList)
                    foreach (var s in srcList)
                        if (s is Dictionary<string, object> sd && sd.TryGetValue("Packages", out var pk) && pk is IEnumerable pkgs)
                            foreach (var o in pkgs)
                                if (o is Dictionary<string, object> pd && pd.TryGetValue("PackageIdentifier", out var id) && id != null)
                                    map[Convert.ToString(id)] = pd.TryGetValue("Version", out var v) && v != null ? Convert.ToString(v) : "";
                Log.Info("winget: " + map.Count + " installed package(s) recognized.");
            }
            catch (Exception ex) { Log.Warn("winget export: " + ex.Message); }
            finally { try { if (File.Exists(file)) File.Delete(file); } catch { } }
            return map;
        }

        // ------------------------------------------------------------ Instalar / actualizar

        /// <summary>
        /// Instala la aplicación o, si ya está, la actualiza a la última versión (winget hace las dos cosas con «install»).
        /// onStatus recibe el paso actual ya traducido (descargando 40 %, verificando, instalando...).
        /// La cancelación solo se atiende antes de empezar: un instalador a medias es peor que dejarlo terminar.
        /// </summary>
        public static AppInstallResult Install(AppEntry app, bool wasInstalled, Action<string> onStatus, CancellationToken ct)
        {
            if (!AppCatalog.IsValidId(app.Id)) return new AppInstallResult { Outcome = AppInstallOutcome.Failed, Error = Loc.T("apps.err.badId") };
            ct.ThrowIfCancellationRequested();
            Log.Info("winget: installing " + app.Id + (wasInstalled ? " (already installed: upgrade)" : "") + "...");
            onStatus?.Invoke(Loc.T("apps.status.preparing"));

            var sw = Stopwatch.StartNew();
            string args = "install --id \"" + app.Id + "\" --exact --silent --accept-package-agreements " + Common;
            var r = Run(args, InstallTimeout, ct, line => Progress(line, onStatus));
            // Algunos instaladores (Spotify...) se niegan a funcionar como administrador: se repite como usuario normal
            if (!r.TimedOut && r.Code == AdminContextNotAllowed && IsElevated())
            {
                Log.Info("winget: " + app.Id + " refuses to install as administrator; retrying as the signed-in user.");
                onStatus?.Invoke(Loc.T("apps.status.asUser"));
                r = RunAsUser(args, InstallTimeout, line => Progress(line, onStatus));
            }
            var res = new AppInstallResult { ExitCode = r.Code };

            if (r.TimedOut) { res.Outcome = AppInstallOutcome.Failed; res.Error = Loc.F("apps.err.timeout", (int)InstallTimeout.TotalMinutes); }
            else if (r.Code == 0) res.Outcome = wasInstalled ? AppInstallOutcome.Updated : AppInstallOutcome.Installed;
            else if (r.Code == UpdateNotApplicable || r.Code == PackageAlreadyInstalled) res.Outcome = AppInstallOutcome.UpToDate;
            else if (r.Code == RebootRequiredToFinish || r.Code == RebootRequiredForInstall) res.Outcome = AppInstallOutcome.RebootNeeded;
            else
            {
                res.Outcome = AppInstallOutcome.Failed;
                if (r.Code == NoApplicablePackage) res.Error = Loc.T("apps.err.notFound");
                else if (r.Code == HashMismatch) res.Error = Loc.T("apps.err.hash");
                else if (r.Code == InstallerFailed) res.Error = Loc.F("apps.err.installer", LastMeaningful(r.Lines));
                else res.Error = Loc.F("apps.err.code", "0x" + r.Code.ToString("X8"), LastMeaningful(r.Lines));
            }
            Log.Info("winget: " + app.Id + " -> " + res.Outcome + " (0x" + r.Code.ToString("X8") + ", " + (int)sw.Elapsed.TotalSeconds + " s)"
                     + (res.Error != null ? ": " + res.Error : "") + ".");
            return res;
        }

        static readonly Regex SizeProgress = new Regex(@"([\d.,]+)\s*(KB|MB|GB)\s*/\s*([\d.,]+)\s*(KB|MB|GB)", RegexOptions.IgnoreCase);
        static readonly Regex PctProgress = new Regex(@"(\d{1,3})\s*%");

        // Traduce las líneas de winget a un estado corto para la interfaz
        static void Progress(string line, Action<string> onStatus)
        {
            if (onStatus == null) return;
            var m = SizeProgress.Match(line);
            if (m.Success)
            {
                double done = ToMB(m.Groups[1].Value, m.Groups[2].Value), total = ToMB(m.Groups[3].Value, m.Groups[4].Value);
                if (total > 0) onStatus(Loc.F("apps.status.downloadingPct", (int)Math.Min(100, done * 100 / total), total.ToString("0")));
                return;
            }
            if (line.IndexOf("Downloading", StringComparison.OrdinalIgnoreCase) >= 0) { onStatus(Loc.T("apps.status.downloading")); return; }
            if (line.IndexOf("verified installer hash", StringComparison.OrdinalIgnoreCase) >= 0) { onStatus(Loc.T("apps.status.verified")); return; }
            if (line.IndexOf("Starting package install", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("Trying to upgrade", StringComparison.OrdinalIgnoreCase) >= 0) { onStatus(Loc.T("apps.status.installing")); return; }
            var p = PctProgress.Match(line);
            if (p.Success && line.IndexOf('█') >= 0) onStatus(Loc.F("apps.status.downloadingPct", int.Parse(p.Groups[1].Value), "?"));
        }

        static double ToMB(string num, string unit)
        {
            if (!double.TryParse(num.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) return 0;
            switch (unit.ToUpperInvariant()) { case "GB": return v * 1024; case "KB": return v / 1024; default: return v; }
        }

        // Última línea con texto útil (sin barras de progreso) para explicar un fallo
        static string LastMeaningful(List<string> lines)
        {
            lock (lines)
                for (int i = lines.Count - 1; i >= 0; i--)
                {
                    string l = lines[i];
                    if (l.IndexOf('█') >= 0 || l.IndexOf('▒') >= 0 || Regex.IsMatch(l, @"^[\-\\|/]$")) continue;
                    if (l.Length > 4) return l.Length > 200 ? l.Substring(0, 200) : l;
                }
            return "-";
        }

        // ------------------------------------------------------------ Buscar

        /// <summary>Busca en el repositorio oficial de winget. La tabla se lee por la posición de las columnas de la cabecera.</summary>
        public static List<WingetSearchHit> Search(string query, int max = 30)
        {
            var hits = new List<WingetSearchHit>();
            query = (query ?? "").Trim();
            if (query.Length < 2) return hits;
            query = query.Replace("\"", "");
            var r = Run("search \"" + query + "\" " + Common, TimeSpan.FromSeconds(60), CancellationToken.None);
            return ParseSearch(r.Lines, max);
        }

        /// <summary>Lee la tabla de «winget search» (Name, Id, Version, ...) por la posición de las columnas de la cabecera.</summary>
        internal static List<WingetSearchHit> ParseSearch(List<string> lines, int max = 30)
        {
            var hits = new List<WingetSearchHit>();
            int header = lines.FindIndex(l => Regex.IsMatch(l, @"^-{10,}$"));
            if (header < 1) return hits;
            string head = lines[header - 1];
            // Inicio de cada columna: donde empieza una palabra tras dos o más espacios (o al principio)
            var starts = new List<int> { 0 };
            foreach (Match m in Regex.Matches(head, @"\s{2,}(\S)")) starts.Add(m.Groups[1].Index);
            if (starts.Count < 3) return hits;
            for (int i = header + 1; i < lines.Count && hits.Count < max; i++)
            {
                string l = lines[i];
                if (l.Length <= starts[1]) continue;
                string Col(int c) => c + 1 < starts.Count
                    ? (l.Length > starts[c] ? l.Substring(starts[c], Math.Min(l.Length, starts[c + 1]) - starts[c]).Trim() : "")
                    : (l.Length > starts[c] ? l.Substring(starts[c]).Trim() : "");
                string id = Col(1).Split(' ')[0];
                if (!AppCatalog.IsValidId(id)) continue;
                hits.Add(new WingetSearchHit { Name = Col(0).TrimEnd('…'), Id = id, Version = Col(2) });
            }
            return hits;
        }
    }
}
