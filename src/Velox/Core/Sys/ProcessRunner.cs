using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

namespace Velox.Core.Sys
{
    public sealed class ProcResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; }
        public bool TimedOut { get; set; }
    }

    /// <summary>Ejecuta herramientas de consola de Windows (powercfg, sc, netsh...) sin ventana y captura su salida.</summary>
    public static class ProcessRunner
    {
        static Encoding OemEncoding
        {
            get
            {
                try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
                catch { return Encoding.Default; }
            }
        }

        public static ProcResult Run(string file, string args, int timeoutMs = 60000)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = OemEncoding,
                StandardErrorEncoding = OemEncoding,
            };
            using (var p = Process.Start(psi))
            {
                Task<string> o = p.StandardOutput.ReadToEndAsync();
                Task<string> e = p.StandardError.ReadToEndAsync();
                bool exited = p.WaitForExit(timeoutMs);
                if (!exited)
                {
                    try { p.Kill(); } catch { }
                    return new ProcResult { ExitCode = -1, Output = "", TimedOut = true };
                }
                p.WaitForExit();
                return new ProcResult { ExitCode = p.ExitCode, Output = (o.Result + "\n" + e.Result).Trim() };
            }
        }

        public static Task<ProcResult> RunAsync(string file, string args, int timeoutMs = 60000) =>
            Task.Run(() => Run(file, args, timeoutMs));

        /// <summary>Abre una consola visible que ejecuta el comando y se queda abierta (para diagnósticos largos).</summary>
        public static void OpenConsole(string title, string command)
        {
            var psi = new ProcessStartInfo("cmd.exe", "/k title " + title + " & " + command)
            {
                UseShellExecute = true,
            };
            Process.Start(psi);
        }

        /// <summary>Abre un programa, archivo o URI (ms-settings:...) con el shell.</summary>
        public static void Shell(string target, string args = null)
        {
            var psi = new ProcessStartInfo(target) { UseShellExecute = true };
            if (!string.IsNullOrEmpty(args)) psi.Arguments = args;
            Process.Start(psi);
        }
    }
}
