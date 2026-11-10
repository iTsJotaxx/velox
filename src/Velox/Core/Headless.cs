using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Velox.Core.Backup;
using Velox.Core.Optimizations;
using Velox.Core.Profiles;
using Velox.Core.Scan;
using Velox.Core.Tools;
using Velox.Localization;

namespace Velox.Core
{
    /// <summary>Opciones de la línea de comandos del modo sin ventana.</summary>
    public sealed class CliOptions
    {
        public enum Mode { None, Help, ApplyRecommended, Import, Profile }
        public Mode Action { get; set; }
        /// <summary>Archivo (--import=) o nombre de perfil (--profile=).</summary>
        public string Target { get; set; }
        public bool IncludeRisky { get; set; }
        public bool NoRestorePoint { get; set; }
        public string Error { get; set; }

        public bool IsHeadless => Action != Mode.None;

        /// <summary>Lee los argumentos. Sin ninguno de los del modo sin ventana devuelve Mode.None (se abre la ventana).</summary>
        public static CliOptions Parse(string[] args)
        {
            var o = new CliOptions();
            foreach (var raw in args ?? new string[0])
            {
                string a = raw.Trim();
                if (a.Equals("--apply-recommended", StringComparison.OrdinalIgnoreCase)) Set(o, Mode.ApplyRecommended, null);
                else if (a.StartsWith("--import=", StringComparison.OrdinalIgnoreCase)) Set(o, Mode.Import, a.Substring(9).Trim('"'));
                else if (a.StartsWith("--profile=", StringComparison.OrdinalIgnoreCase)) Set(o, Mode.Profile, a.Substring(10).Trim('"'));
                else if (a.Equals("--include-risky", StringComparison.OrdinalIgnoreCase)) o.IncludeRisky = true;
                else if (a.Equals("--no-restore-point", StringComparison.OrdinalIgnoreCase)) o.NoRestorePoint = true;
                else if (a.Equals("--help", StringComparison.OrdinalIgnoreCase) || a == "/?" || a == "-h") { if (o.Action == Mode.None) o.Action = Mode.Help; }
            }
            if ((o.Action == Mode.Import || o.Action == Mode.Profile) && string.IsNullOrWhiteSpace(o.Target)) o.Error = "missing value";
            // --include-risky solo tiene sentido con un perfil: el modo Recomendado nunca incluye ajustes de riesgo
            if (o.IncludeRisky && o.Action == Mode.ApplyRecommended) o.Error = "--include-risky only works with --import or --profile";
            return o;
        }

        static void Set(CliOptions o, Mode m, string target)
        {
            if (o.Action != Mode.None && o.Action != Mode.Help && o.Action != m) o.Error = "use only one of --apply-recommended, --import, --profile";
            o.Action = m;
            o.Target = target;
        }
    }

    /// <summary>
    /// Modo sin ventana, para automatizar después de formatear:
    ///   Velox.exe --apply-recommended            aplica la configuración recomendada (nunca ajustes de riesgo)
    ///   Velox.exe --import="C:\kit.velox.json"   aplica un archivo exportado (sin apps: esas se instalan desde la ventana)
    ///   Velox.exe --profile="Jugar"               aplica un perfil guardado
    ///   --include-risky      con --import/--profile, aplica también los ajustes de riesgo del archivo
    ///   --no-restore-point   no crea punto de restauración
    /// Códigos de salida: 0 todo bien · 1 algún cambio falló · 2 argumentos o archivo no válidos · 3 Velox ya está abierto.
    /// Escribe en la consola desde la que se lance (y siempre en el registro). Con «start /wait» o
    /// «Start-Process -Wait -PassThru» se puede leer el código de salida.
    /// </summary>
    public static class Headless
    {
        public const int ExitOk = 0, ExitSomeFailed = 1, ExitBadArgs = 2, ExitAlreadyOpen = 3;

        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

        static void Out(string s)
        {
            Log.Info("[cli] " + s);
            try { Console.WriteLine(s); } catch { }
        }

        /// <summary>Ya con la instancia única, el registro y la app (para los textos) preparados.</summary>
        public static int Run(CliOptions o)
        {
            if (o.Action == CliOptions.Mode.Help)
            {
                Out("Velox " + AppInfo.Version);
                Out("  --apply-recommended            " + Loc.T("cli.help.rec"));
                Out("  --import=<file.velox.json>     " + Loc.T("cli.help.import"));
                Out("  --profile=<name>               " + Loc.T("cli.help.profile"));
                Out("  --include-risky                " + Loc.T("cli.help.risky"));
                Out("  --no-restore-point             " + Loc.T("cli.help.norp"));
                Out(Loc.T("cli.help.codes"));
                return ExitOk;
            }
            if (o.Error != null) { Out(Loc.T("cli.badArgs") + " " + o.Error); return ExitBadArgs; }

            // El archivo se comprueba antes de analizar el equipo (que tarda): un error se dice enseguida
            VeloxProfile p = null;
            if (o.Action != CliOptions.Mode.ApplyRecommended)
            {
                try { p = ProfileStore.Load(o.Action == CliOptions.Mode.Profile ? ProfileStore.PathFor(o.Target) : Path.GetFullPath(o.Target)); }
                catch (Exception ex) { Out(ex.Message); return ExitBadArgs; }
            }

            Out(Loc.T("cli.scanning"));
            var sys = SystemScanner.Scan(null);

            List<OptChange> changes;
            var notes = new List<string>();
            string what;
            if (p == null)
            {
                what = Loc.T("cli.recommended");
                changes = RecommendedChanges(sys);
            }
            else
            {
                what = p.Name;
                var plan = ProfileStore.Plan(p, sys, null);
                changes = plan.Changes;
                foreach (var s in plan.Skipped) notes.Add("  · " + s);
                if (!o.IncludeRisky)
                {
                    foreach (var c in changes.Where(c => c.Enable && c.Opt.IsRisky).ToList())
                    {
                        notes.Add("  · " + c.Opt.Name + ": " + Loc.T("cli.riskySkipped"));
                        changes.Remove(c);
                    }
                }
                if (p.Apps != null && p.Apps.Count > 0) notes.Add(Loc.F("cli.appsNote", p.Apps.Count));
            }

            Out(Loc.F("cli.plan", what, changes.Count));
            foreach (var n in notes) Out(n);
            if (changes.Count == 0) { Out(Loc.T("cli.nothing")); return ExitOk; }

            if (Settings.Current.CreateRestorePoint && !o.NoRestorePoint)
            {
                Out(Loc.T("ui.busy.restorePoint"));
                try
                {
                    var rp = RestorePointTool.CreateAsync("Velox").GetAwaiter().GetResult();
                    // Sin ventana no se puede preguntar: se sigue (Velox guarda copia de cada valor igualmente) y se avisa
                    if (rp.Result == RestorePointResult.Failed) Out("  ! " + rp.Message);
                }
                catch (Exception ex) { Out("  ! " + ex.Message); }
            }

            int failed = 0;
            foreach (var c in changes)
            {
                try
                {
                    if (c.Enable) OptimizationEngine.Apply(c.Opt, sys, "headless");
                    else OptimizationEngine.Revert(c.Opt, sys, "headless");
                    Out((c.Enable ? "  + " : "  - ") + c.Opt.Name + (c.Opt.Restart == RestartNeed.Reboot ? "  (" + Loc.T("ui.restart.reboot") + ")" : ""));
                }
                catch (Exception ex)
                {
                    failed++;
                    Out("  x " + c.Opt.Name + ": " + ex.Message);
                }
            }
            Out(failed == 0 ? Loc.F("cli.done", changes.Count) : Loc.F("cli.doneSome", changes.Count - failed, failed));
            return failed == 0 ? ExitOk : ExitSomeFailed;
        }

        /// <summary>
        /// Lo mismo que el botón «Recomendado»: cada optimización que cuenta (no «a tu gusto», no de riesgo, disponible y no
        /// bloqueada) pasa a su estado recomendado, activando o desactivando. Los ajustes de Windows no se tocan.
        /// </summary>
        public static List<OptChange> RecommendedChanges(SystemInfo sys)
        {
            var list = new List<OptChange>();
            foreach (var o in Catalog.All)
            {
                if (!OptimizationEngine.IsAvailable(o, sys) || OptimizationEngine.IsLocked(o, sys)) continue;
                if (o.IsRisky || OptimizationEngine.IsPreference(o, sys)) continue;
                bool want = OptimizationEngine.IsRecommended(o, sys);
                if (OptimizationEngine.IsApplied(o) != want) list.Add(new OptChange(o, want));
            }
            return list;
        }

        /// <summary>Punto de entrada desde Program.Main (antes de abrir ninguna ventana).</summary>
        public static int Main(CliOptions o, string instanceName)
        {
            AttachConsole(-1); // la consola desde la que se lanzó (si la hay)
            try { Console.WriteLine(); } catch { }
            using (var mutex = new Mutex(true, instanceName, out bool createdNew))
            {
                AppPaths.Initialize();
                Log.Initialize();
                if (!createdNew)
                {
                    Log.Warn("[cli] Velox is already open: windowless mode cancelled.");
                    try { Console.WriteLine("Velox is already open. Close it and try again. / Velox ya está abierto: ciérralo y vuelve a intentarlo."); } catch { }
                    return ExitAlreadyOpen;
                }
                Log.Info("=== Velox " + AppInfo.Version + " (windowless: " + o.Action + ") ===");
                Settings.Load();
                BackupStore.Load();
                History.Load();
                foreach (var id in Catalog.Retired)
                    try { if (BackupStore.Restore(id)) History.Add(id, false, "retired"); } catch { }
                // La app sin ventana: solo para tener los textos (diccionarios de idioma) y los recursos
                var app = new App();
                app.InitializeComponent();
                Loc.SetLanguage(Settings.Current.Language ?? Loc.SystemLanguage());
                int code;
                try { code = Run(o); }
                catch (Exception ex) { Log.Error("[cli] Unexpected error", ex); Out(ex.Message); code = ExitSomeFailed; }
                Log.Info("=== Velox windowless finished (exit " + code + ") ===");
                return code;
            }
        }
    }
}
