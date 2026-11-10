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

            // ---------------------------------------------------------------- Protecciones de drivers (no se instala nada)
            {
                bool prev = DriverSafety.SessionRebootPending;
                try
                {
                    DriverSafety.SessionRebootPending = true;
                    Check("safety: session reboot blocks", DriverSafety.Blockers().Count > 0);
                    var dummy = new List<DriverUpdate> { new DriverUpdate { Source = DriverSource.WindowsUpdate, Title = "dummy", Selected = true } };
                    var res = DriverUpdater.InstallAsync(dummy, null, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                    Check("safety: install blocked before doing anything", res.Blocked && res.Installed == 0 && res.Failed == 0 && res.Messages.Count > 0,
                          "blocked=" + res.Blocked + " installed=" + res.Installed + " failed=" + res.Failed);
                    bool threw = false;
                    try { RollbackNative.Rollback(new DeviceDriver { DeviceId = @"PCI\DUMMY\0", DeviceName = "dummy" }); }
                    catch (InvalidOperationException) { threw = true; }
                    Check("safety: rollback blocked", threw);
                }
                finally { DriverSafety.SessionRebootPending = prev; }
                Console.WriteLine("  (info) blockers now: " + string.Join(" | ", DriverSafety.Blockers()));
                Out.AppendLine("  (info) blockers now: " + string.Join(" | ", DriverSafety.Blockers()));
            }

            // ---------------------------------------------------------------- Catálogo de aplicaciones y winget
            {
                var ids = Velox.Core.Apps.AppCatalog.BuiltIn.Select(a => a.Id).ToList();
                Check("apps: catalog ids valid", ids.All(Velox.Core.Apps.AppCatalog.IsValidId), string.Join(", ", ids.Where(i => !Velox.Core.Apps.AppCatalog.IsValidId(i))));
                Check("apps: catalog ids unique", ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == ids.Count);
                var cats = Velox.Core.Apps.AppCatalog.Categories.Select(c => c.Id).ToList();
                Check("apps: every app has a known category", Velox.Core.Apps.AppCatalog.BuiltIn.All(a => cats.Contains(a.Category)));
                Check("apps: id with space rejected", !Velox.Core.Apps.AppCatalog.IsValidId("Evil.App --override x"));
                Check("apps: id with quote rejected", !Velox.Core.Apps.AppCatalog.IsValidId("Evil.\"App"));
                Check("apps: id without dot rejected", !Velox.Core.Apps.AppCatalog.IsValidId("notepad"));
                Check("apps: Microsoft Store id accepted", Velox.Core.Apps.AppCatalog.IsValidId("9NKSQGP7F2NH") && !Velox.Core.Apps.AppCatalog.IsValidId("9nksqgp7f2nh x"));
                Check("apps: store apps marked msstore", Velox.Core.Apps.AppCatalog.BuiltIn.Where(a => System.Text.RegularExpressions.Regex.IsMatch(a.Id, "^[0-9A-Z]{12}$")).All(a => a.Source == "msstore"));
                var lol = Velox.Core.Apps.AppCatalog.BuiltIn.FirstOrDefault(a => a.Id == "RiotGames.LeagueOfLegends");
                var val = Velox.Core.Apps.AppCatalog.BuiltIn.FirstOrDefault(a => a.Id == "RiotGames.Valorant");
                Check("apps: LoL and VALORANT are separate entries with regions", lol?.Variants?.Count == 14 && val?.Variants?.Count == 6
                      && lol.Variants.Concat(val.Variants).All(v => Velox.Core.Apps.AppCatalog.IsValidId(v.Id)) && !lol.Interactive && !val.Interactive);
                if (lol != null)
                {
                    var noRegion = Velox.Core.Apps.Winget.Install(lol, false, null, System.Threading.CancellationToken.None);
                    Check("apps: LoL without region is refused (nothing runs)", noRegion.Outcome == Velox.Core.Apps.AppInstallOutcome.Failed && noRegion.ExitCode == 0);
                    var badRegion = Velox.Core.Apps.Winget.Install(lol, false, null, System.Threading.CancellationToken.None, "RiotGames.Valorant.EU");
                    Check("apps: LoL with a region from another game is refused", badRegion.Outcome == Velox.Core.Apps.AppInstallOutcome.Failed);
                }
                // País de Windows -> servidor recomendado
                foreach (var c in new[] { "ES:EUW:EU", "DE:EUW:EU", "GB:EUW:EU", "PL:EUNE:EU", "SE:EUNE:EU", "TR:TR:EU", "RU:RU:EU", "US:NA:NA", "CA:NA:NA",
                                          "MX:LA1:LATAM", "CO:LA1:LATAM", "AR:LA2:LATAM", "CL:LA2:LATAM", "BR:BR:BR", "JP:JP:AP", "KR:KR:KR",
                                          "AU:OC1:AP", "SG:SG2:AP", "TW:TW2:AP", "VN:VN2:AP" })
                {
                    var p = c.Split(':');
                    string l2 = Velox.Core.Apps.RegionDetect.Suggest("RiotGames.LeagueOfLegends", p[0]);
                    string v2 = Velox.Core.Apps.RegionDetect.Suggest("RiotGames.Valorant", p[0]);
                    Check("region " + p[0] + " -> LoL " + p[1] + " / VALORANT " + p[2], l2 == p[1] && v2 == p[2], l2 + " / " + v2);
                }
                Check("region: every suggestion is a real variant",
                      new[] { "ES", "PL", "US", "MX", "AR", "BR", "TR", "RU", "AU", "JP", "KR", "SG", "TW", "VN" }.All(i =>
                          lol.Variants.Any(v => v.Code == Velox.Core.Apps.RegionDetect.Suggest(lol.Id, i)) &&
                          val.Variants.Any(v => v.Code == Velox.Core.Apps.RegionDetect.Suggest(val.Id, i))));
                Check("region: unknown country gives no suggestion", Velox.Core.Apps.RegionDetect.Suggest(lol.Id, "AQ") == null);
                Console.WriteLine("  (info) Windows country: " + Velox.Core.Apps.RegionDetect.CountryIso2());                var table = new List<string>
                {
                    "Name                       Id                         Version                 Match",
                    "------------------------------------------------------------------------------------------",
                    "VLC media player           VideoLAN.VLC               3.0.24                  Moniker: vlc",
                    "7-Zip ZS Non Darkmode      mcmilk.7zip-zstd-ndm       26.02-v1.5.7-R2         Tag: 7zip",
                    "Notepad++                  Notepad++.Notepad++        8.9.9",
                };
                var hits = Velox.Core.Apps.Winget.ParseSearch(table);
                Check("apps: search table parsed", hits.Count == 3 && hits[0].Id == "VideoLAN.VLC" && hits[0].Name == "VLC media player" && hits[0].Version == "3.0.24"
                                                  && hits[1].Id == "mcmilk.7zip-zstd-ndm" && hits[2].Id == "Notepad++.Notepad++",
                      string.Join(" | ", hits.Select(h => h.Name + "/" + h.Id + "/" + h.Version)));
            }

            // ---------------------------------------------------------------- Desinstalador
            {
                Velox.Core.Apps.Uninstaller.Split("\"C:\\Program Files\\App X\\uninst.exe\" /S", out var e1, out var a1);
                Check("unin: quoted command split", e1 == @"C:\Program Files\App X\uninst.exe" && a1 == "/S", e1 + " | " + a1);
                Velox.Core.Apps.Uninstaller.Split(@"C:\Program Files\App X\uninst.exe /S /X", out var e2, out var a2);
                Check("unin: unquoted command with spaces split", e2 == @"C:\Program Files\App X\uninst.exe" && a2 == "/S /X", e2 + " | " + a2);
                Velox.Core.Apps.Uninstaller.Split("MsiExec.exe /X{12345678-1234-1234-1234-123456789012}", out var e3, out var a3);
                Check("unin: msiexec split", e3 == "MsiExec.exe" && a3.StartsWith("/X{"), e3 + " | " + a3);
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                Check("unin: program folder is a safe leftover", Velox.Core.Apps.Uninstaller.IsSafeLeftover(Path.Combine(pf, "SomeApp")));
                Check("unin: Program Files itself is not", !Velox.Core.Apps.Uninstaller.IsSafeLeftover(pf));
                Check("unin: Common Files is not", !Velox.Core.Apps.Uninstaller.IsSafeLeftover(Path.Combine(pf, "Common Files")));
                Check("unin: LocalAppData\\Programs is not", !Velox.Core.Apps.Uninstaller.IsSafeLeftover(Path.Combine(la, "Programs")));
                Check("unin: Windows folder is not", !Velox.Core.Apps.Uninstaller.IsSafeLeftover(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32")));
                Check("unin: user profile is not", !Velox.Core.Apps.Uninstaller.IsSafeLeftover(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
                Check("unin: D:\\Games is not (outside known roots)", !Velox.Core.Apps.Uninstaller.IsSafeLeftover(@"D:\Games\Something"));
            }

            // ---------------------------------------------------------------- Protección: carpetas clave y programas imprescindibles
            {
                string Sf(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
                string win = Sf(Environment.SpecialFolder.Windows), sysd = Path.GetPathRoot(win);
                string pf = Sf(Environment.SpecialFolder.ProgramFiles), pd = Sf(Environment.SpecialFolder.CommonApplicationData);
                string prof = Sf(Environment.SpecialFolder.UserProfile), roam = Sf(Environment.SpecialFolder.ApplicationData), loc = Sf(Environment.SpecialFolder.LocalApplicationData);
                string downloads = Path.Combine(prof, "Downloads");
                var mustProtect = new[]
                {
                    sysd, win, Path.Combine(win, @"System32\drivers"), pf, Path.Combine(pf, "Common Files"), Path.Combine(pf, @"WindowsApps\SomePkg"),
                    pd, Path.Combine(pd, @"Microsoft\Windows"), Path.Combine(sysd, "Users"), Path.Combine(sysd, @"Users\Public"), Path.Combine(sysd, @"Users\OtroUsuario"),
                    prof, Path.Combine(prof, "AppData"), roam, loc, Path.Combine(prof, @"AppData\LocalLow"), Path.Combine(loc, "Microsoft"), Path.Combine(loc, "Packages"),
                    Path.Combine(roam, @"Microsoft\Windows\Start Menu"), Sf(Environment.SpecialFolder.Desktop), Sf(Environment.SpecialFolder.MyDocuments), downloads,
                    Sf(Environment.SpecialFolder.MyPictures), Sf(Environment.SpecialFolder.MyVideos), Sf(Environment.SpecialFolder.MyMusic),
                    Path.Combine(sysd, "Recovery"), Path.Combine(sysd, "System Volume Information"), @"D:\$Recycle.Bin", Path.Combine(sysd, "pagefile.sys"),
                    AppPaths.DataDir, Path.Combine(pd, @"Velox\backup.xml"),
                };
                foreach (var p in mustProtect)
                    Check("protect: " + p, Protection.PathReason(p) != null, "not protected");
                var mustAllow = new[]
                {
                    Path.Combine(pf, "SomeOldApp"), Path.Combine(downloads, "setup.exe"), Path.Combine(Sf(Environment.SpecialFolder.MyDocuments), "Old project"),
                    Path.Combine(Sf(Environment.SpecialFolder.Desktop), "nota.txt"), Path.Combine(loc, @"Temp\foo"), Path.Combine(loc, "SomeAppCache"),
                    @"D:\Games\Old", Path.Combine(sysd, "Games"),
                };
                foreach (var p in mustAllow)
                    Check("allow: " + p, Protection.PathReason(p) == null, Protection.PathReason(p));
                var installed = new[] { Path.Combine(pf, "Foo App") };
                Check("protect: installed program folder", Protection.PathReason(Path.Combine(pf, "Foo App"), installed) != null);
                Check("allow: cache inside installed program", Protection.PathReason(Path.Combine(pf, @"Foo App\cache"), installed) == null);

                foreach (var n in new[] { "Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.40.33810", "Microsoft .NET Runtime - 8.0.10 (x64)",
                                          "Microsoft Windows Desktop Runtime - 8.0.10 (x64)", "Microsoft Edge WebView2 Runtime", "Microsoft Edge",
                                          "NVIDIA Graphics Driver 566.36", "Realtek High Definition Audio Driver", "Intel(R) Chipset Device Software",
                                          "Oracle VirtualBox Guest Additions 7.2.22", "Microsoft Windows Application Compatibility Fix Database" })
                    Check("protect program: " + n, Protection.ProgramReason(n, "", null) != null);
                Check("protect store: App Installer", Protection.ProgramReason("Instalador de aplicaciones", "Microsoft Corporation", "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe") != null);
                Check("protect store: Microsoft Store", Protection.ProgramReason("Microsoft Store", "Microsoft Corporation", "Microsoft.WindowsStore_8wekyb3d8bbwe") != null);
                foreach (var n in new[] { "Brave", "Microsoft .NET SDK 8.0.427 (x64)", "Driver Booster 11", "Display Driver Uninstaller", "Spotify", "Steam",
                                          "Microsoft OneDrive", "Copilot", "7-Zip 26.06 (x64 edition)" })
                    Check("allow program: " + n, Protection.ProgramReason(n, "", null) == null, Protection.ProgramReason(n, "", null));
                Check("allow store: Solitaire", Protection.ProgramReason("Solitaire & Casual Games", "Microsoft Corporation", "Microsoft.MicrosoftSolitaireCollection_8wekyb3d8bbwe") == null);
            }

            // ---------------------------------------------------------------- Optimizaciones con riesgo
            {
                var all = Velox.Core.Optimizations.Catalog.All;
                var sys = new Velox.Core.Scan.SystemInfo { Hvci = true, RamTotalGB = 16 };
                foreach (var id in new[] { "FullscreenOpt", "Mpo", "MemoryIntegrity", "CpuMitigations" })
                    Check("catalog has " + id, all.Any(o => o.Id == id));
                Check("ids are unique", all.Select(o => o.Id).Distinct().Count() == all.Count);
                Check("retired optimizations are not in the catalog", !all.Any(o => Velox.Core.Optimizations.Catalog.Retired.Contains(o.Id)));                var risky = all.Where(o => o.IsRisky).ToList();
                Check("risky set", new HashSet<string>(risky.Select(o => o.Id)).SetEquals(new[] { "MemoryIntegrity", "CpuMitigations", "WSearch" }),
                      string.Join(",", risky.Select(o => o.Id)));
                foreach (var o in risky)
                {
                    Check("risk kind known: " + o.Id, o.Risk == "security" || o.Risk == "features", o.Risk);
                    Check("risky never recommended: " + o.Id, !Velox.Core.Optimizations.OptimizationEngine.IsRecommended(o, sys));
                    Check("risky is user's call: " + o.Id, Velox.Core.Optimizations.OptimizationEngine.IsPreference(o, sys));
                }
                Check("security risks need a reboot", risky.Where(o => o.Risk == "security").All(o => o.Restart == Velox.Core.Optimizations.RestartNeed.Reboot));
            }

            // ---------------------------------------------------------------- Limpieza automática de RAM: cuándo actúa
            {
                const double G = Velox.Core.Tools.MemSnapshot.GB;
                Velox.Core.Tools.MemSnapshot M(double total, double free, double standby, bool lists = true) =>
                    new Velox.Core.Tools.MemSnapshot { Total = total * G, Free = free * G, Standby = standby * G, Available = (free + standby) * G, HasLists = lists };
                bool Should(Velox.Core.Tools.MemSnapshot s) => Velox.Core.Tools.MemoryAutoCleaner.ShouldClean(s, 20);
                Check("auto clean: little free, big cache -> clean", Should(M(16, 1, 8)));
                Check("auto clean: plenty free -> leave", !Should(M(16, 6, 8)));
                Check("auto clean: little free but small cache -> leave (nothing to gain)", !Should(M(16, 0.5, 0.5)));
                Check("auto clean: cache under 10 % of a big machine -> leave", !Should(M(64, 2, 3)));
                Check("auto clean: lists unreadable -> leave", !Should(M(16, 1, 8, false)));
                Check("auto clean: no data -> leave", !Should(new Velox.Core.Tools.MemSnapshot()));
            }

            // ---------------------------------------------------------------- Inicio de Windows: nombres legibles
            {
                Check("startup exe: quoted", Velox.Core.Tools.StartupManager.ExePath("\"C:\\Program Files\\A B\\x.exe\" --min") == "C:\\Program Files\\A B\\x.exe");
                Check("startup exe: unquoted with spaces", Velox.Core.Tools.StartupManager.ExePath("C:\\Program Files\\A B\\x.exe /background") == "C:\\Program Files\\A B\\x.exe");
                Check("startup exe: empty", Velox.Core.Tools.StartupManager.ExePath("  ") == null);
                string cmdExe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
                Check("startup name: generic launcher keeps entry name", Velox.Core.Tools.StartupManager.FriendlyName("MyScript", "\"" + cmdExe + "\" /c x.bat") == "MyScript");
                Check("startup name: missing file keeps entry name", Velox.Core.Tools.StartupManager.FriendlyName("Gone", "C:\\nope\\gone.exe") == "Gone");
                string notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
                string fn = Velox.Core.Tools.StartupManager.FriendlyName("np_1234ABCD", notepad);
                Check("startup name: uses file description", fn.Length > 1 && fn != "np_1234ABCD", fn);
            }

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
