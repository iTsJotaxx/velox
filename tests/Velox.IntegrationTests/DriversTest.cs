using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using Velox.Core;
using Velox.Core.Drivers;
using Velox.Localization;

namespace Velox.IntegrationTests
{
    /// <summary>
    /// Prueba REAL de instalación de drivers con el mismo motor que usa la interfaz.
    /// Por fases y con parada automática: primero los de Windows Update, se verifica la versión instalada,
    /// y solo si todo fue bien sigue con NVIDIA. Instala drivers de verdad: ejecutar solo como administrador.
    /// Uso: Velox.IntegrationTests.exe salida.txt --drivers [--no-nvidia] [--no-wu]
    /// </summary>
    static class DriversTest
    {
        sealed class FileProgress : IProgress<string>
        {
            readonly string _path; string _last;
            public FileProgress(string path) { _path = path; }
            public void Report(string value)
            {
                if (value == _last) return;
                _last = value;
                Write("    > " + value);
            }
        }

        static string _out;
        static readonly object WriteLock = new object();

        static void Write(string s)
        {
            var line = DateTime.Now.ToString("HH:mm:ss") + "  " + s;
            // El progreso llega desde varios hilos: sin bloqueo, dos escrituras a la vez chocan en el archivo
            lock (WriteLock)
            {
                Console.WriteLine(line);
                if (_out != null) File.AppendAllText(_out, line + Environment.NewLine, Encoding.UTF8);
            }
        }

        /// <summary>Vuelve al driver anterior del dispositivo cuyo nombre contenga el texto y comprueba la versión.</summary>
        static int RunRollback(string name)
        {
            var d = DriverInventory.GetInstalled().FirstOrDefault(x => x.DeviceName != null && x.DeviceName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            if (d == null) { Write("No encuentro un dispositivo que contenga '" + name + "'."); Write("DONE"); return 1; }
            Write("Antes: " + d.DeviceName + " " + d.DriverVersion);
            try
            {
                string msg = DriverRollback.RollbackAsync(d).GetAwaiter().GetResult();
                Write("Resultado: " + msg);
            }
            catch (Exception ex) { Write("Error controlado: " + ex.Message); Write("DONE"); return 1; }
            Thread.Sleep(6000);
            Write("Después: " + (ReadVersionById(d.DeviceId) ?? "(no se pudo leer)"));
            Write("DONE");
            return 0;
        }

        public static int Run(string outFile, bool doWu, bool doNvidia, string only = null, string rollback = null)
        {
            _out = outFile;
            if (_out != null && File.Exists(_out)) File.Delete(_out);
            try
            {
                // La interfaz necesita una aplicación WPF para cargar los textos
                new System.Windows.Application();
                AppPaths.Initialize(null);
                Log.Initialize();
                try { Loc.SetLanguage("es"); } catch (Exception ex) { Write("(textos no cargados: " + ex.Message + ")"); }

                if (rollback != null) return RunRollback(rollback);

                var progress = new FileProgress(outFile);
                Write("=== Búsqueda de drivers ===");
                var found = DriverUpdater.SearchAsync(progress).GetAwaiter().GetResult();
                if (only != null) found = found.Where(u => u.DeviceName != null && u.DeviceName.IndexOf(only, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                foreach (var w in DriverUpdater.LastSearchWarnings) Write("AVISO: " + w);
                foreach (var u in found)
                    Write("  " + u.SourceText.PadRight(14) + u.DeviceName + ": " + u.CurrentVersion + " -> " + u.NewVersion + " (" + u.SizeText + ")");
                if (found.Count == 0) { Write("No hay actualizaciones."); Write("DONE"); return 0; }

                var wu = found.Where(u => u.Source == DriverSource.WindowsUpdate).ToList();
                var nv = found.Where(u => u.Source == DriverSource.Nvidia).ToList();

                if (doWu && wu.Count > 0)
                {
                    foreach (var u in found) u.Selected = u.Source == DriverSource.WindowsUpdate;
                    Write("=== Fase A: " + wu.Count + " drivers de Windows Update ===");
                    var res = DriverUpdater.InstallAsync(wu, progress, CancellationToken.None).GetAwaiter().GetResult();
                    Write("Resultado: instalados=" + res.Installed + " fallidos=" + res.Failed + " reinicio=" + res.RebootRequired);
                    foreach (var m in res.Messages) Write("  - " + m);
                    Verify(wu);
                    if (res.Failed > 0) { Write("ABORT: hubo fallos en la fase A; no se instala NVIDIA."); Write("DONE"); return 1; }
                }

                if (doNvidia && nv.Count > 0)
                {
                    foreach (var u in found) u.Selected = u.Source == DriverSource.Nvidia;
                    Write("=== Fase B: NVIDIA ===");
                    var res = DriverUpdater.InstallAsync(nv, progress, CancellationToken.None).GetAwaiter().GetResult();
                    Write("Resultado: instalados=" + res.Installed + " fallidos=" + res.Failed + " reinicio=" + res.RebootRequired);
                    foreach (var m in res.Messages) Write("  - " + m);
                    VerifyNvidia(nv[0]);
                    if (res.Failed > 0) { Write("FALLO en NVIDIA."); Write("DONE"); return 1; }
                }

                Write("DONE");
                return 0;
            }
            catch (Exception ex)
            {
                Write("EXCEPCIÓN: " + ex);
                Write("DONE");
                return 3;
            }
        }

        static void Verify(List<DriverUpdate> updates)
        {
            Write("--- Verificación de versiones instaladas");
            foreach (var u in updates)
            {
                string now = ReadVersionById(u.DeviceId);
                bool ok = string.Equals(now, u.NewVersion, StringComparison.OrdinalIgnoreCase);
                Write((ok ? "[OK] " : "[??] ") + u.DeviceName + ": ahora " + (now ?? "(no se pudo leer)") + ", esperada " + u.NewVersion +
                      (ok ? "" : "  (puede requerir reiniciar para verse)"));
            }
        }

        static void VerifyNvidia(DriverUpdate u)
        {
            Write("--- Verificación NVIDIA");
            string now = ReadVersionById(u.DeviceId);
            int? n = DriverRules.NvidiaInstalledToNumber(now);
            string text = n.HasValue ? DriverRules.NvidiaNumberToText(n.Value) : "?";
            bool ok = text == u.NewVersion;
            Write((ok ? "[OK] " : "[??] ") + u.DeviceName + ": ahora " + now + " (= NVIDIA " + text + "), esperada " + u.NewVersion);
        }

        static string ReadVersionById(string deviceId)
        {
            try
            {
                string id = (deviceId ?? "").Replace("\\", "\\\\").Replace("'", "''");
                using (var s = new ManagementObjectSearcher("SELECT DriverVersion FROM Win32_PnPSignedDriver WHERE DeviceID = '" + id + "'"))
                    foreach (ManagementBaseObject o in s.Get()) { var v = Convert.ToString(o["DriverVersion"]); if (!string.IsNullOrEmpty(v)) return v; }
            }
            catch { }
            return null;
        }
    }
}
