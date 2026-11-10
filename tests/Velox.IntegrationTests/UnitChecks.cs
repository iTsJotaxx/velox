using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Velox.Core;
using Velox.Core.Drivers;

namespace Velox.IntegrationTests
{
    /// <summary>
    /// Comprobaciones de lógica pura (no cambian nada del sistema ni usan la red): reglas de drivers, emparejado de
    /// dispositivos, nombres de NVIDIA y duplicados entre fuentes. Uso: Velox.IntegrationTests.exe [salida.txt] --unit
    /// </summary>
    static class UnitChecks
    {
        static readonly StringBuilder Out = new StringBuilder();
        static int _fails, _total;

        static void Check(string name, bool ok, string detail = null)
        {
            _total++;
            if (!ok) _fails++;
            string line = (ok ? "[OK] " : "[XX] ") + name + (ok || detail == null ? "" : "  -> " + detail);
            Console.WriteLine(line);
            Out.AppendLine(line);
        }

        public static int Run(string outFile)
        {
            string dir = Path.Combine(Path.GetTempPath(), "Velox-UnitChecks");
            AppPaths.Initialize(dir);
            Log.Initialize();

            // ---------------------------------------------------------------- Firmware: nunca
            Check("firmware by title", DriverRules.IsFirmware("Lenovo - Firmware - 1.2.3", null, null, null));
            Check("firmware by BIOS word", DriverRules.IsFirmware("Dell System BIOS update", null, null, null));
            Check("firmware by class", DriverRules.IsFirmware("x", "Firmware", null, null));
            Check("firmware by UEFI hwid", DriverRules.IsFirmware("x", null, null, @"UEFI\RES_{1234}"));
            Check("not firmware (biosphere is not BIOS)", !DriverRules.IsFirmware("Biosphere audio driver", "Media", null, @"PCI\VEN_1&DEV_2"));

            // ---------------------------------------------------------------- Versiones
            var d1 = new DateTime(2024, 1, 1); var d2 = new DateTime(2025, 1, 1);
            Check("WU newer by date and version", DriverRules.IsWuUpdateNewer(d2, "Intel - Net - 2.0.0.1", d1, "1.0.0.0"));
            Check("WU not newer: same date", !DriverRules.IsWuUpdateNewer(d1, "Intel - Net - 2.0.0.1", d1, "1.0.0.0"));
            Check("WU not newer: newer date but lower version", !DriverRules.IsWuUpdateNewer(d2, "Intel - Net - 0.9.0.0", d1, "1.0.0.0"));
            Check("WU not newer: unknown dates", !DriverRules.IsWuUpdateNewer(null, "x 2.0.0.0", d1, "1.0.0.0"));
            Check("NVIDIA 32.0.15.7216 = 572.16", DriverRules.NvidiaInstalledToNumber("32.0.15.7216") == 57216);
            Check("NVIDIA 32.0.16.1047 = 610.47", DriverRules.NvidiaInstalledToNumber("32.0.16.1047") == 61047);
            Check("NVIDIA web 621.14", DriverRules.NvidiaWebToNumber("621.14") == 62114);
            Check("NVIDIA bad version rejected", DriverRules.NvidiaInstalledToNumber("32.0.15") == null && DriverRules.NvidiaInstalledToNumber("32.0.15.x1") == null
                                                 && DriverRules.NvidiaWebToNumber("621") == null && DriverRules.NvidiaWebToNumber("621.1a") == null);

            // ---------------------------------------------------------------- Nombres NVIDIA (catálogo con y sin prefijo)
            Check("NVIDIA name prefix stripped", NvidiaSource.Normalize("NVIDIA GeForce RTX 3060") == NvidiaSource.Normalize("GeForce RTX 3060"));
            Check("NVIDIA name both prefixed", NvidiaSource.Normalize("NVIDIA GeForce  RTX 4070") == NvidiaSource.Normalize("NVIDIA GeForce RTX 4070"));
            Check("NVIDIA Ti is different", NvidiaSource.Normalize("NVIDIA GeForce RTX 3060 Ti") != NvidiaSource.Normalize("GeForce RTX 3060"));

            // ---------------------------------------------------------------- URL de descarga de NVIDIA
            Check("NVIDIA url ok", Throws(() => NvidiaSource.ValidateDownloadUri("https://us.download.nvidia.com/Windows/621.14/621.14-desktop-win10-win11-64bit-international-dch-whql.exe", "621.14")) == false);
            Check("NVIDIA url http rejected", Throws(() => NvidiaSource.ValidateDownloadUri("http://us.download.nvidia.com/Windows/621.14/a.exe", "621.14")));
            Check("NVIDIA url foreign host rejected", Throws(() => NvidiaSource.ValidateDownloadUri("https://download.nvidia.com.evil.com/Windows/621.14/a.exe", "621.14")));
            Check("NVIDIA url other version rejected", Throws(() => NvidiaSource.ValidateDownloadUri("https://us.download.nvidia.com/Windows/500.00/a.exe", "621.14")));
            Check("NVIDIA url not exe rejected", Throws(() => NvidiaSource.ValidateDownloadUri("https://us.download.nvidia.com/Windows/621.14/a.zip", "621.14")));

            // ---------------------------------------------------------------- Emparejado de Windows Update con dispositivos
            var inst = new List<DeviceDriver>
            {
                new DeviceDriver { DeviceId = @"PCI\VEN_8086&DEV_1234&SUBSYS_00011028&REV_01\3&11583659&0&10", HardwareId = @"PCI\VEN_8086&DEV_1234&SUBSYS_00011028&REV_01" },
                new DeviceDriver { DeviceId = @"PCI\VEN_8086&DEV_9999&SUBSYS_00011028&REV_01\3&11583659&0&18", HardwareId = @"PCI\VEN_8086&DEV_9999&SUBSYS_00011028&REV_01" },
            };
            Check("match exact hwid", WindowsUpdateSource.Match(inst, @"PCI\VEN_8086&DEV_1234&SUBSYS_00011028&REV_01").Count == 1);
            Check("match vendor+device prefix", WindowsUpdateSource.Match(inst, @"PCI\VEN_8086&DEV_1234").Count == 1);
            Check("no match on partial device id", WindowsUpdateSource.Match(inst, @"PCI\VEN_8086&DEV_12").Count == 0);
            Check("no match on vendor only", WindowsUpdateSource.Match(inst, @"PCI\VEN_8086").Count == 0);
            Check("no match without bus", WindowsUpdateSource.Match(inst, "VEN_8086&DEV_1234").Count == 0);

            // ---------------------------------------------------------------- Windows Update y NVIDIA para la misma gráfica
            Check("dup: NVIDIA newer keeps NVIDIA", Dedupe("32.0.15.6094", "572.16") == "Nvidia");
            Check("dup: WU newer keeps WU", Dedupe("32.0.15.7680", "572.16") == "WindowsUpdate");
            Check("dup: same version keeps NVIDIA", Dedupe("32.0.15.7216", "572.16") == "Nvidia");
            var other = new List<DriverUpdate>
            {
                new DriverUpdate { Source = DriverSource.Nvidia, NewVersion = "572.16", DeviceId = @"PCI\GPU" },
                new DriverUpdate { Source = DriverSource.WindowsUpdate, NewVersion = "1.0.0.0", DeviceId = @"PCI\AUDIO" },
            };
            DriverEngine.RemoveDuplicateGpuOffers(other);
            Check("dup: other devices untouched", other.Count == 2);

            // ---------------------------------------------------------------- Limpiar temporales: solo lo viejo
            {
                string t = Path.Combine(dir, "tempclean");
                if (Directory.Exists(t)) Directory.Delete(t, true);
                var old = DateTime.Now.AddDays(-3);
                Directory.CreateDirectory(Path.Combine(t, "oldDir", "emptyOld"));
                Directory.CreateDirectory(Path.Combine(t, "newEmpty"));
                File.WriteAllText(Path.Combine(t, "old.tmp"), "x");
                File.WriteAllText(Path.Combine(t, "new.tmp"), "x");
                File.WriteAllText(Path.Combine(t, "oldDir", "old.log"), "x");
                File.WriteAllText(Path.Combine(t, "oldDir", "fresh.dat"), "x"); // archivo recién creado dentro de una carpeta vieja
                foreach (var f in new[] { "old.tmp", @"oldDir\old.log" }) { var p = Path.Combine(t, f); File.SetCreationTime(p, old); File.SetLastWriteTime(p, old); }
                foreach (var d in new[] { "oldDir", @"oldDir\emptyOld" }) { var p = Path.Combine(t, d); Directory.SetCreationTime(p, old); Directory.SetLastWriteTime(p, old); }
                Velox.Core.Tools.TempCleaner.CleanDir(new DirectoryInfo(t), DateTime.Now.AddHours(-24), true);
                Check("temp: old file deleted", !File.Exists(Path.Combine(t, "old.tmp")));
                Check("temp: new file kept", File.Exists(Path.Combine(t, "new.tmp")));
                Check("temp: old file inside old folder deleted", !File.Exists(Path.Combine(t, "oldDir", "old.log")));
                Check("temp: fresh file inside old folder kept", File.Exists(Path.Combine(t, "oldDir", "fresh.dat")));
                Check("temp: old empty folder removed", !Directory.Exists(Path.Combine(t, "oldDir", "emptyOld")));
                Check("temp: new empty folder kept", Directory.Exists(Path.Combine(t, "newEmpty")));
            }

            string summary = "Unit checks: " + (_total - _fails) + "/" + _total + " passed.";
            Console.WriteLine(summary); Out.AppendLine(summary);
            if (outFile != null) File.WriteAllText(outFile, Out.ToString(), Encoding.UTF8);
            return _fails == 0 ? 0 : 1;
        }

        static string Dedupe(string wuVersion, string nvVersion)
        {
            var list = new List<DriverUpdate>
            {
                new DriverUpdate { Source = DriverSource.WindowsUpdate, NewVersion = wuVersion, DeviceId = @"PCI\VEN_10DE&DEV_2504\4&1" },
                new DriverUpdate { Source = DriverSource.Nvidia, NewVersion = nvVersion, DeviceId = @"pci\ven_10de&dev_2504\4&1" },
            };
            DriverEngine.RemoveDuplicateGpuOffers(list);
            return list.Count == 1 ? list[0].Source.ToString() : "count=" + list.Count;
        }

        static bool Throws(Action a)
        {
            try { a(); return false; } catch { return true; }
        }
    }
}
