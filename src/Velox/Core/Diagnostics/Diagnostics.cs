using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Velox.Core.Scan;
using Velox.Core.Sys;

namespace Velox.Core.Diagnostics
{
    public enum DiagKind
    {
        /// <summary>Abre una consola visible con la herramienta (sfc, DISM, chkdsk...).</summary>
        Console,
        /// <summary>Abre una herramienta o página de Windows (dxdiag, perfmon, ms-settings...).</summary>
        External,
        /// <summary>Se ejecuta dentro de Velox y muestra el resultado (ping, estado de discos...).</summary>
        InApp,
    }

    public sealed class DiagResult
    {
        public bool Success { get; set; }
        /// <summary>Resumen corto ya traducido.</summary>
        public string Message { get; set; }
        /// <summary>Detalle de varias líneas (puede ser null).</summary>
        public string Details { get; set; }
    }

    public sealed class DiagAction
    {
        public string Id { get; set; }
        public string Group { get; set; }        // "repair", "hardware", "network", "reports"
        public string Icon { get; set; }         // glifo Segoe Fluent Icons
        public DiagKind Kind { get; set; }
        public bool NeedsConfirm { get; set; }   // muestra confirmación antes (p. ej. reinicia o tarda mucho)
        public string Name => Localization.Loc.T("diag." + Id + ".name");
        public string Description => Localization.Loc.T("diag." + Id + ".desc");
        public string ConfirmText => Localization.Loc.T("diag." + Id + ".confirm");
    }

    /// <summary>Diagnósticos de Windows: catálogo de acciones y su ejecución.</summary>
    public static class DiagnosticsCatalog
    {
        static DiagAction A(string id, string group, string icon, DiagKind kind, bool confirm = false) =>
            new DiagAction { Id = id, Group = group, Icon = icon, Kind = kind, NeedsConfirm = confirm };

        public static readonly List<DiagAction> All = new List<DiagAction>
        {
            // Reparación
            A("sfc",           "repair",   "", DiagKind.Console),
            A("dism",          "repair",   "", DiagKind.Console),
            A("chkdsk",        "repair",   "", DiagKind.Console),
            A("troubleshoot",  "repair",   "", DiagKind.External),
            A("windowsupdate", "repair",   "", DiagKind.External),

            // Hardware
            A("diskhealth",    "hardware", "", DiagKind.InApp),
            A("memdiag",       "hardware", "", DiagKind.External, true),
            A("dxdiag",        "hardware", "", DiagKind.External),
            A("battery",       "hardware", "", DiagKind.InApp),
            A("energy",        "hardware", "", DiagKind.InApp, true),
            A("perfreport",    "hardware", "", DiagKind.External),
            A("reliability",   "hardware", "", DiagKind.External),
            A("msinfo",        "hardware", "", DiagKind.External),
            A("events",        "hardware", "", DiagKind.External),

            // Red
            A("ping",          "network",  "", DiagKind.InApp),
            A("netinfo",       "network",  "", DiagKind.InApp),
            A("flushdns",      "network",  "", DiagKind.InApp),
            A("netreset",      "network",  "", DiagKind.InApp, true),
        };

        /// <summary>La batería solo existe en portátiles; el resto de acciones siempre están disponibles.</summary>
        public static bool IsAvailable(DiagAction a, SystemInfo s)
        {
            if (a != null && a.Id == "battery") return s?.IsLaptop == true;
            return true;
        }

        public static async Task<DiagResult> RunAsync(DiagAction a, SystemInfo s)
        {
            if (a == null) return new DiagResult { Success = false, Message = Localization.Loc.T("diag.msg.error") };
            Log.Info("Diagnostic start: " + a.Id);
            DiagResult r;
            try
            {
                switch (a.Kind)
                {
                    case DiagKind.Console:
                        r = RunConsole(a);
                        break;
                    case DiagKind.External:
                        r = RunExternal(a);
                        break;
                    default:
                        r = await Task.Run(() => DiagRunners.RunInApp(a.Id)).ConfigureAwait(false);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Diagnostic " + a.Id + " failed", ex);
                r = new DiagResult { Success = false, Message = Localization.Loc.F("diag.msg.exception", ex.Message) };
            }
            if (r == null) r = new DiagResult { Success = false, Message = Localization.Loc.T("diag.msg.error") };
            Log.Write(r.Success ? LogLevel.Info : LogLevel.Warn,
                "Diagnostic end: " + a.Id + " -> " + (r.Success ? "OK" : "FAIL") + " | " + r.Message);
            return r;
        }

        static DiagResult RunConsole(DiagAction a)
        {
            string cmd;
            switch (a.Id)
            {
                case "sfc": cmd = "sfc /scannow"; break;
                case "dism": cmd = "DISM /Online /Cleanup-Image /RestoreHealth"; break;
                case "chkdsk": cmd = "chkdsk %SystemDrive% /scan"; break; // análisis en línea, no bloquea el disco
                default: throw new NotSupportedException(a.Id);
            }
            // Los caracteres especiales de cmd romperían el título
            string name = a.Name.Replace("&", "").Replace("|", "").Replace("<", "").Replace(">", "").Replace("^", "").Replace("%", "");
            ProcessRunner.OpenConsole("Velox - " + name, cmd);
            return new DiagResult { Success = true, Message = Localization.Loc.T("diag.msg.console") };
        }

        static DiagResult RunExternal(DiagAction a)
        {
            switch (a.Id)
            {
                case "troubleshoot": ProcessRunner.Shell("ms-settings:troubleshoot"); break;
                case "windowsupdate": ProcessRunner.Shell("ms-settings:windowsupdate"); break;
                case "memdiag": ProcessRunner.Shell("mdsched.exe"); break;
                case "dxdiag": ProcessRunner.Shell("dxdiag.exe"); break;
                case "perfreport": ProcessRunner.Shell("perfmon.exe", "/report"); break;
                case "reliability": ProcessRunner.Shell("perfmon.exe", "/rel"); break;
                case "msinfo": ProcessRunner.Shell("msinfo32.exe"); break;
                case "events": ProcessRunner.Shell("eventvwr.msc"); break;
                default: throw new NotSupportedException(a.Id);
            }
            string key = "diag." + a.Id + ".result";
            return new DiagResult
            {
                Success = true,
                Message = Localization.Loc.Has(key) ? Localization.Loc.T(key) : Localization.Loc.T("diag.msg.opened"),
            };
        }
    }
}
