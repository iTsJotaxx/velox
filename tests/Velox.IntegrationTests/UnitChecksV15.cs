using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Velox.Core;
using Velox.Core.Apps;
using Velox.Core.Backup;
using Velox.Core.Optimizations;
using Velox.Core.Profiles;
using Velox.Core.Scan;
using Velox.Core.Tools;

namespace Velox.IntegrationTests
{
    /// <summary>
    /// Comprobaciones de las funciones de la 1.5 que no cambian nada del sistema: perfiles, actualizaciones de winget,
    /// paquetes DNS, historial, comprobación tras reiniciar, asistente, limpieza profunda (en una carpeta de prueba),
    /// modo sin ventana e informe de salud.
    /// </summary>
    static class UnitChecksV15
    {
        public static void Run(Action<string, bool, string> check, string dir)
        {
            void Check(string name, bool ok, string detail = null) => check(name, ok, detail);

            // ---------------------------------------------------------------- Perfiles: guardar, leer, validar
            {
                string f = Path.Combine(dir, "p1" + ProfileStore.Extension);
                var p = new VeloxProfile { Name = "Prueba" };
                p.Optimizations["GameDVR"] = true; p.Optimizations["MenuDelay"] = false;
                p.Tweaks["tw.FileExt"] = true;
                p.Apps = new List<ProfileApp> { new ProfileApp { Id = "VideoLAN.VLC", Name = "VLC" }, new ProfileApp { Id = "RiotGames.Valorant", Variant = "RiotGames.Valorant.EU" } };
                p.Answers = new ProfileAnswers { Competitive = true };
                ProfileStore.Save(p, f);
                var q = ProfileStore.Load(f);
                Check("profile: round trip name", q.Name == "Prueba");
                Check("profile: round trip optimizations", q.Optimizations.Count == 2 && q.Optimizations["GameDVR"] && !q.Optimizations["MenuDelay"]);
                Check("profile: round trip tweaks", q.Tweaks.ContainsKey("tw.FileExt") && q.Tweaks["tw.FileExt"]);
                Check("profile: round trip apps and region", q.Apps.Count == 2 && q.Apps[1].Variant == "RiotGames.Valorant.EU");
                Check("profile: round trip answers", q.Answers != null && q.Answers.Competitive == true && q.Answers.Laptop == null);
                Check("profile: file is readable JSON", File.ReadAllText(f).Contains("\"GameDVR\": true") || File.ReadAllText(f).Contains("\"GameDVR\":true"), File.ReadAllText(f).Substring(0, Math.Min(200, File.ReadAllText(f).Length)));

                string bad = Path.Combine(dir, "bad" + ProfileStore.Extension);
                File.WriteAllText(bad, "esto no es json {");
                Check("profile: garbage rejected", Throws(() => ProfileStore.Load(bad)));
                File.WriteAllText(bad, "{\"Schema\": 99, \"Name\": \"x\"}");
                Check("profile: newer schema rejected", Throws(() => ProfileStore.Load(bad)));
                File.WriteAllText(bad, new string(' ', 1024 * 1024 + 10));
                Check("profile: huge file rejected", Throws(() => ProfileStore.Load(bad)));
                Check("profile: missing file rejected", Throws(() => ProfileStore.Load(Path.Combine(dir, "nope.velox.json"))));
                Check("profile: safe file name", ProfileStore.SafeName("  a<b>c:d/e\\f|g?h*i\"j  ") == "abcdefghij" && ProfileStore.SafeName(new string('x', 80)).Length == 40);

                // Plan: lo desconocido, lo que no aplica y las apps no válidas se descartan con motivo
                var sys = new SystemInfo { RamTotalGB = 16 };
                var plan = ProfileStore.Plan(new VeloxProfile
                {
                    Optimizations = new Dictionary<string, bool> { { "NoExiste", true }, { "MemoryIntegrity", true }, { "CpuMitigations", !OptimizationEngine.IsApplied(Catalog.All.First(o => o.Id == "CpuMitigations")) } },
                    Apps = new List<ProfileApp> { new ProfileApp { Id = "bad id!" }, new ProfileApp { Id = "VideoLAN.VLC" } },
                }, sys);
                Check("plan: unknown id skipped", plan.Skipped.Any(s => s.StartsWith("NoExiste")));
                bool hvciAvail = OptimizationEngine.IsAvailable(Catalog.All.First(o => o.Id == "MemoryIntegrity"), sys);
                Check("plan: unavailable risky item skipped (memory integrity off here)", hvciAvail || plan.Skipped.Count >= 2, string.Join(" | ", plan.Skipped));
                Check("plan: available risky item becomes a change", plan.Changes.Any(c => c.Opt.Id == "CpuMitigations"));
                Check("plan: invalid app skipped", plan.Skipped.Any(s => s.Contains("bad id!")));
                Check("plan: valid app kept", plan.AppsToInstall.Count == 1 && plan.AppsToInstall[0].Id == "VideoLAN.VLC");
                var installed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "VideoLAN.VLC", "3.0" } };
                var plan2 = ProfileStore.Plan(new VeloxProfile { Apps = new List<ProfileApp> { new ProfileApp { Id = "VideoLAN.VLC" } } }, sys, installed);
                Check("plan: installed app not proposed again", plan2.AppsToInstall.Count == 0);
            }

            // ---------------------------------------------------------------- winget upgrade: tabla real de esta máquina
            {
                var lines = new List<string>
                {
                    "   - ", "   \\ ",
                    "Name                             Id                              Version    Available Source",
                    "--------------------------------------------------------------------------------------------",
                    "App Installer                    Microsoft.AppInstaller          1.29.379.0 1.29.380  winget",
                    "Microsoft .NET SDK 8.0.427 (x64) Microsoft.DotNet.SDK.8          8.0.427    8.0.428   winget",
                    "WindowsAppRuntime.1.5            Microsoft.WindowsAppRuntime.1.5 1.5.8      1.5.9     winget",
                    "7-Zip 23.01 (x64)                7zip.7zip                       23.01      26.00     winget",
                    "4 upgrades available.",
                    "",
                    "The following packages have an upgrade available, but require explicit targeting for upgrade:",
                    "Name    Id            Version Available Source",
                    "-----------------------------------------------",
                    "Pinned  Some.Pinned   1.0     2.0       winget",
                };
                var u = Winget.ParseUpgrades(lines);
                Check("upgrade: rows of the first table only", u.Count == 4, string.Join(",", u.Select(x => x.Id)));
                Check("upgrade: columns", u.Any(x => x.Id == "7zip.7zip" && x.Version == "23.01" && x.Available == "26.00" && x.Name.StartsWith("7-Zip")));
                Check("upgrade: winget itself excluded", u.First(x => x.Id == "Microsoft.AppInstaller").Excluded);
                Check("upgrade: .NET SDK unticked (runtime/kit)", u.First(x => x.Id == "Microsoft.DotNet.SDK.8").IsRuntime && !u.First(x => x.Id == "Microsoft.DotNet.SDK.8").SelectedByDefault);
                Check("upgrade: app runtime unticked", !u.First(x => x.Id == "Microsoft.WindowsAppRuntime.1.5").SelectedByDefault);
                Check("upgrade: normal app ticked", u.First(x => x.Id == "7zip.7zip").SelectedByDefault);
                Check("upgrade: pinned table ignored", !u.Any(x => x.Id == "Some.Pinned"));
                Check("upgrade: empty output", Winget.ParseUpgrades(new List<string> { "No installed package found matching input criteria." }).Count == 0);
            }

            // ---------------------------------------------------------------- DNS: paquetes y direcciones
            {
                var q = DnsTool.BuildQuery(0xBEEF, "google.com");
                Check("dns: header", q[0] == 0xBE && q[1] == 0xEF && q[2] == 0x01 && q[5] == 1, BitConverter.ToString(q.Take(12).ToArray()));
                Check("dns: question name", q[12] == 6 && System.Text.Encoding.ASCII.GetString(q, 13, 6) == "google" && q[19] == 3 && q[23] == 0);
                Check("dns: type A class IN", q.Length == 12 + 12 + 4 && q[25] == 1 && q[27] == 1);
                var resp = new byte[] { 0xBE, 0xEF, 0x81, 0x80, 0, 1, 0, 2, 0, 0, 0, 0 };
                Check("dns: parse ok", DnsTool.ParseResponse(resp, 0xBEEF, out int rc, out int an) && rc == 0 && an == 2);
                Check("dns: wrong id rejected", !DnsTool.ParseResponse(resp, 0x1234, out _, out _));
                var notResp = (byte[])resp.Clone(); notResp[2] = 0x01;
                Check("dns: query bit rejected", !DnsTool.ParseResponse(notResp, 0xBEEF, out _, out _));
                var nx = (byte[])resp.Clone(); nx[3] = 0x83;
                Check("dns: NXDOMAIN code", DnsTool.ParseResponse(nx, 0xBEEF, out int rc3, out _) && rc3 == 3);
                Check("dns: split servers", DnsTool.SplitServers("1.1.1.1,1.0.0.1").SequenceEqual(new[] { "1.1.1.1", "1.0.0.1" }) && DnsTool.SplitServers("8.8.8.8 8.8.4.4").Count == 2);
                Check("dns: IPv6 and junk ignored", DnsTool.SplitServers("fe80::1, hola, 9.9.9.9").SequenceEqual(new[] { "9.9.9.9" }));
                Check("dns: describe", DnsTool.Describe("1.1.1.1,1.0.0.1") == "Cloudflare" && DnsTool.Describe("") != "" && DnsTool.Describe("10.0.0.1") == "10.0.0.1");
                Check("dns: bad label rejected", Throws(() => DnsTool.BuildQuery(1, "a..b")));
            }

            // ---------------------------------------------------------------- Historial
            {
                History.ResetForTests();
                History.Add("GameDVR", true, "ui");
                History.Add("GameDVR", false, "undo-one");
                History.Add("Tips", true, "ui", false, "boom");
                Check("history: last action", History.LastAction("GameDVR") == "off");
                Check("history: failed entries don't count", History.LastAction("Tips") == null);
                Check("history: newest first", History.All()[0].Id == "Tips" && History.All()[0].Error == "boom");
                for (int i = 0; i < History.MaxEntries + 5; i++) History.Add("X" + i, true, "ui");
                Check("history: capped", History.All().Count == History.MaxEntries && History.All()[0].Id == "X" + (History.MaxEntries + 4));
                History.Load();
                Check("history: saved to disk and read back", History.All().Count == History.MaxEntries);
                History.ResetForTests();
            }

            // ---------------------------------------------------------------- Comprobación tras reiniciar (sin reiniciar)
            {
                var t = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
                var pending = new List<RebootPending>
                {
                    new RebootPending { Id = "HAGS", On = true, AppliedUtc = t },
                    new RebootPending { Id = "MemoryIntegrity", On = true, AppliedUtc = t },
                    new RebootPending { Id = "Mpo", On = false, AppliedUtc = t },
                    new RebootPending { Id = "Gone", On = true, AppliedUtc = t },
                    new RebootPending { Id = "Later", On = true, AppliedUtc = t.AddHours(3) },
                };
                var applied = new Dictionary<string, bool?> { { "HAGS", true }, { "MemoryIntegrity", true }, { "Mpo", true }, { "Gone", null }, { "Later", true } };
                var v = RebootCheck.Evaluate(pending, t.AddHours(2), id => applied[id], id => id == "MemoryIntegrity" ? (bool?)false : null, out var remaining);
                Check("reboot: only changes before the restart are checked", v.Count == 3 && remaining.Count == 1 && remaining[0].Id == "Later", v.Count + "/" + remaining.Count);
                Check("reboot: value kept -> ok", v.First(x => x.Id == "HAGS").Ok);
                Check("reboot: Windows says it is not running -> not ok", !v.First(x => x.Id == "MemoryIntegrity").Ok && v.First(x => x.Id == "MemoryIntegrity").Kept);
                Check("reboot: reverted to on after being turned off -> not kept", !v.First(x => x.Id == "Mpo").Kept);
                var v2 = RebootCheck.Evaluate(pending, t.AddSeconds(30), id => applied[id], id => null, out var rem2);
                Check("reboot: no restart yet -> nothing checked", v2.Count == 0 && rem2.Count == pending.Count);
                Check("reboot: boot time sane", Math.Abs((DateTime.UtcNow - RebootCheck.LastBootUtc()).TotalDays) < 400);
            }

            // ---------------------------------------------------------------- Asistente: respuestas y recomendación
            {
                var s = new SystemInfo { RamTotalGB = 16, IsLaptop = true };
                s.IsLaptopDetected = true;
                s.ApplyAnswers(false, true, true);
                Check("wizard: laptop answer overrides detection", !s.IsLaptop && s.IsLaptopDetected);
                Check("wizard: competitive -> exclusive fullscreen recommended", OptimizationEngine.IsRecommended(Find("FullscreenOpt"), s) && !OptimizationEngine.IsPreference(Find("FullscreenOpt"), s));
                Check("wizard: OBS -> keep background apps at full speed recommended", OptimizationEngine.IsRecommended(Find("PowerThrottling"), s));
                s.ApplyAnswers(null, null, null);
                Check("wizard: no answers -> detection back", s.IsLaptop);
                Check("wizard: no answers -> fullscreen is your call", !OptimizationEngine.IsRecommended(Find("FullscreenOpt"), s) && OptimizationEngine.IsPreference(Find("FullscreenOpt"), s));
                Check("wizard: no answers -> power throttling not recommended", !OptimizationEngine.IsRecommended(Find("PowerThrottling"), s));
                Check("wizard: risky never recommended even competitive", !Catalog.All.Where(o => o.IsRisky).Any(o => OptimizationEngine.IsRecommended(o, new SystemInfo { Competitive = true, RecordsObs = true })));
            }

            // ---------------------------------------------------------------- Modo portátil
            {
                var desk = new SystemInfo { IsLaptop = false };
                var lap = new SystemInfo { IsLaptop = true };
                foreach (var id in new[] { "BatteryNoBoost", "BatterySaverAt30" })
                {
                    var o = Find(id);
                    Check("laptop: " + id + " hidden on desktops", !OptimizationEngine.IsAvailable(o, desk));
                    Check("laptop: " + id + " available on laptops", OptimizationEngine.IsAvailable(o, lap));
                    Check("laptop: " + id + " is your call", OptimizationEngine.IsPreference(o, lap) && !OptimizationEngine.IsRecommended(o, lap));
                }
            }

            // ---------------------------------------------------------------- Limpieza profunda en una carpeta de prueba
            {
                string root = Path.Combine(dir, "deepclean");
                if (Directory.Exists(root)) Directory.Delete(root, true);
                string cache = Path.Combine(root, "cache"), keep = Path.Combine(root, "keep"), thumbs = Path.Combine(root, "thumbs");
                Directory.CreateDirectory(Path.Combine(cache, "sub", "deep"));
                Directory.CreateDirectory(keep);
                Directory.CreateDirectory(thumbs);
                File.WriteAllText(Path.Combine(cache, "a.bin"), new string('x', 1000));
                File.WriteAllText(Path.Combine(cache, "sub", "deep", "b.bin"), new string('x', 500));
                File.WriteAllText(Path.Combine(keep, "important.txt"), "no borrar");
                File.WriteAllText(Path.Combine(thumbs, "thumbcache_256.db"), new string('x', 300));
                File.WriteAllText(Path.Combine(thumbs, "other.db"), "x");
                // Una unión dentro de la caché que apunta a «keep»: nunca se puede seguir
                var mk = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + Path.Combine(cache, "link") + "\" \"" + keep + "\"") { CreateNoWindow = true, UseShellExecute = false });
                mk.WaitForExit(10000);
                bool junction = Directory.Exists(Path.Combine(cache, "link"));

                var cat = new CleanCategory { Id = "test", Roots = { cache } };
                var m = DeepCleaner.Measure(cat);
                Check("clean: measure counts files, not through the junction", m.Files == 2 && m.Bytes == 1500, m.Files + " files, " + m.Bytes + " bytes, junction=" + junction);
                long freed = DeepCleaner.Clean(cat);
                Check("clean: frees what was measured", freed == 1500, freed.ToString());
                Check("clean: root kept, subfolders gone", Directory.Exists(cache) && !Directory.Exists(Path.Combine(cache, "sub")));
                Check("clean: junction target untouched", File.Exists(Path.Combine(keep, "important.txt")));

                var thumbCat = new CleanCategory { Id = "t", Roots = { thumbs }, FilePattern = "thumbcache_*.db" };
                Check("clean: pattern only", DeepCleaner.Clean(thumbCat) == 300 && File.Exists(Path.Combine(thumbs, "other.db")));

                string young = Path.Combine(root, "young");
                Directory.CreateDirectory(young);
                File.WriteAllText(Path.Combine(young, "new.tmp"), "x");
                File.WriteAllText(Path.Combine(young, "old.tmp"), "xx");
                File.SetLastWriteTime(Path.Combine(young, "old.tmp"), DateTime.Now.AddDays(-3));
                var ageCat = new CleanCategory { Id = "a", Roots = { young }, MinAge = TimeSpan.FromHours(24) };
                Check("clean: only older than the limit", DeepCleaner.Clean(ageCat) == 2 && File.Exists(Path.Combine(young, "new.tmp")));
                var blocked = new CleanCategory { Id = "b", Roots = { young }, Blocker = () => "no" };
                Check("clean: blocked category does nothing", DeepCleaner.Clean(blocked) == 0 && File.Exists(Path.Combine(young, "new.tmp")));
                Check("clean: real categories never include user folders", DeepCleaner.Categories().SelectMany(c => c.Roots).All(r =>
                    r.IndexOf("Documents", StringComparison.OrdinalIgnoreCase) < 0 && r.IndexOf("Downloads", StringComparison.OrdinalIgnoreCase) < 0 && r.IndexOf("Desktop", StringComparison.OrdinalIgnoreCase) < 0 && !r.Contains("$Recycle")));
            }

            // ---------------------------------------------------------------- Modo sin ventana: argumentos
            {
                var a = CliOptions.Parse(new[] { "--apply-recommended" });
                Check("cli: apply recommended", a.Action == CliOptions.Mode.ApplyRecommended && a.Error == null);
                var b = CliOptions.Parse(new[] { "--import=\"C:\\x y\\kit.velox.json\"", "--include-risky", "--no-restore-point" });
                Check("cli: import with quotes and flags", b.Action == CliOptions.Mode.Import && b.Target == "C:\\x y\\kit.velox.json" && b.IncludeRisky && b.NoRestorePoint && b.Error == null);
                Check("cli: profile", CliOptions.Parse(new[] { "--profile=Jugar" }).Target == "Jugar");
                Check("cli: risky not allowed with recommended", CliOptions.Parse(new[] { "--apply-recommended", "--include-risky" }).Error != null);
                Check("cli: two actions rejected", CliOptions.Parse(new[] { "--apply-recommended", "--profile=x" }).Error != null);
                Check("cli: empty import rejected", CliOptions.Parse(new[] { "--import=" }).Error != null);
                Check("cli: normal start is not windowless", !CliOptions.Parse(new[] { "--page=tools" }).IsHeadless && !CliOptions.Parse(new string[0]).IsHeadless);
                Check("cli: help", CliOptions.Parse(new[] { "--help" }).Action == CliOptions.Mode.Help);
                Check("cli: recommended never includes risky", Headless.RecommendedChanges(new SystemInfo { RamTotalGB = 16 }).All(c => !c.Opt.IsRisky));
            }

            // ---------------------------------------------------------------- Informe de salud
            {
                var ok = HealthReport.Build(new HealthInputs { DiskFreeGB = 200, DiskSizeGB = 500, DisksTotal = 2, DisksUnhealthy = 0, RamAvailablePct = 50, RamTotalGB = 16, UptimeDays = 1, StartupApps = 4, DefenderRealtime = true });
                Check("health: all fine", ok.Count == 7 && ok.All(i => !i.Attention), string.Join(",", ok.Where(i => i.Attention).Select(i => i.Area)));
                var bad = HealthReport.Build(new HealthInputs { DiskFreeGB = 10, DiskSizeGB = 500, DisksTotal = 2, DisksUnhealthy = 1, RamAvailablePct = 5, RamTotalGB = 8, RecommendedPending = 2, Reverted = 1, RebootPending = true, StartupApps = 14, DefenderRealtime = false });
                Check("health: every problem flagged", bad.All(i => i.Attention), string.Join(",", bad.Where(i => !i.Attention).Select(i => i.Area)));
                Check("health: unknown data left out", HealthReport.Build(new HealthInputs { DiskSizeGB = 0 }).Select(i => i.Area).SequenceEqual(new[] { "opts", "windows" }));
                Check("health: long uptime flagged", HealthReport.Build(new HealthInputs { UptimeDays = 9 }).First(i => i.Area == "windows").Attention);
            }
        }

        static Optimization Find(string id) => OptimizationEngine.Find(id) ?? throw new Exception("missing optimization " + id);

        static bool Throws(Action a)
        {
            try { a(); return false; } catch (InvalidOperationException) { return true; } catch (ArgumentException) { return true; }
        }
    }
}
