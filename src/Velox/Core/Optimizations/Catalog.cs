using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using Velox.Core.Backup;
using Velox.Core.Scan;
using Velox.Core.Sys;
using Velox.Native;
using Velox.Localization;
using static Velox.Core.Optimizations.CatalogHelpers;

namespace Velox.Core.Optimizations
{
    /// <summary>
    /// Catálogo de optimizaciones y categorías. Textos en Localization\{es,en}\Opts.xaml.
    /// Si Recommended no se indica, la optimización se recomienda siempre.
    /// </summary>
    public static class Catalog
    {
        public static readonly List<Category> Categories = new List<Category>();
        public static readonly List<Optimization> All = new List<Optimization>();

        const string MmPath = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        const string DshPath = @"HKLM\SOFTWARE\Policies\Microsoft\Dsh";
        const string TaskbarAdv = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        const string TelemetryPolicy = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection";
        const string ContentDelivery = @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        const string GameBar = @"HKCU\Software\Microsoft\GameBar";
        const string AppCapture = @"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR";

        static readonly RegistryValueKind DWord = RegistryValueKind.DWord;
        static readonly RegistryValueKind Str = RegistryValueKind.String;

        static Catalog()
        {
            Categories.AddRange(new[]
            {
                new Category { Id = "power", Icon = "" },
                new Category { Id = "games", Icon = "" },
                new Category { Id = "input", Icon = "" },
                new Category { Id = "network", Icon = "" },
                new Category { Id = "background", Icon = "" },
                new Category { Id = "interface", Icon = "" },
            });

            All.AddRange(Power());
            All.AddRange(Games());
            All.AddRange(Input());
            All.AddRange(Network());
            All.AddRange(Background());
            All.AddRange(Interface());
        }

        static bool Laptop(SystemInfo s) => s != null && s.IsLaptop;
        // Equipo modesto: gráficos integrados o poca RAM (ahí sí se notan animaciones y transparencias)
        static bool LowEnd(SystemInfo s) => s != null && (!s.HasDedicatedGpu || s.RamTotalGB <= 8);
        // Ryzen X3D de dos chiplets: AMD aparca a propósito el chiplet sin V-Cache durante los juegos
        static bool DualCcdX3D(SystemInfo s) =>
            s?.Cpu != null && s.Cpu.IndexOf("X3D", StringComparison.OrdinalIgnoreCase) >= 0 &&
            new[] { "7900", "7950", "9900", "9950" }.Any(m => s.Cpu.Contains(m));
        static bool LowDisk(SystemInfo s) => s != null && s.DiskFreePct < 20;

        // ================================================================ Energía y procesador
        static IEnumerable<Optimization> Power()
        {
            yield return new Optimization
            {
                Id = "PowerPlan", Category = "power", Impact = Impact.Medium, Restart = RestartNeed.None,
                Recommended = s => !s.IsLaptop,
                Detect = UltimateActive,
                Apply = (id, s) => ApplyPowerPlan(id),
                Default = s => PowerCfg.SetActive(PowerCfg.Balanced),
            };
            yield return new Optimization
            {
                Id = "PowerMode", Category = "power", Impact = Impact.High, Restart = RestartNeed.None,
                Recommended = s => s.IsLaptop && !s.OnBattery,
                // En sobremesa el plan de energía ya decide; este control no hace daño, así que queda a tu gusto
                Preference = s => !s.IsLaptop,
                Available = s => PowerOverlay.Get() != null,
                Detect = () => string.Equals(PowerOverlay.Get(), PowerCfg.OverlayBestPerformance, StringComparison.OrdinalIgnoreCase),
                Apply = (id, s) =>
                {
                    Tracked.Record(id, new BackupRecord { Kind = "Overlay", Key = "Overlay", Data = PowerOverlay.Get() });
                    if (!PowerOverlay.Set(PowerCfg.OverlayBestPerformance)) throw new InvalidOperationException(Loc.T("err.powerMode"));
                },
                Default = s => PowerOverlay.Set(PowerCfg.OverlayBalanced),
            };
            yield return new Optimization
            {
                Id = "CoreParking", Category = "power", Impact = Impact.Low, Restart = RestartNeed.None,
                Recommended = s => false,
                Preference = s => !DualCcdX3D(s),
                // Algunos planes de otros programas limitan el rango y no admiten el 100 %
                Available = s => { var v = Active(PowerCfg.SubProcessor, PowerCfg.CpMinCores); return !(v != null && v.Max.HasValue && v.Max.Value < 100); },
                Detect = () => Active(PowerCfg.SubProcessor, PowerCfg.CpMinCores)?.AC == 100,
                Apply = (id, s) => Tracked.SetPowerValue(id, PowerCfg.SubProcessor, PowerCfg.CpMinCores, 100, null),
                Default = s => SetActiveValue(PowerCfg.SubProcessor, PowerCfg.CpMinCores, 10, null),
            };
            yield return new Optimization
            {
                Id = "PowerThrottling", Category = "power", Impact = Impact.Low, Restart = RestartNeed.Reboot,
                Recommended = s => false,
                Reg = new[] { Dw(@"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1, null) },
            };
            yield return new Optimization
            {
                Id = "Hibernate", Category = "power", Impact = Impact.Low, Restart = RestartNeed.None,
                Recommended = s => !s.IsLaptop && LowDisk(s),
                Preference = s => s.IsLaptop || !LowDisk(s),
                Detect = () => !PowerCfg.HibernationEnabled(),
                Apply = (id, s) =>
                {
                    Tracked.Record(id, new BackupRecord { Kind = "Hibernate", Key = "Hibernate", Flag = true });
                    PowerCfg.SetHibernation(false);
                },
                Default = s => PowerCfg.SetHibernation(true),
            };
            yield return new Optimization
            {
                Id = "PciAspm", Category = "power", Impact = Impact.Low, Restart = RestartNeed.None,
                Recommended = s => !s.IsLaptop,
                Available = s => PowerCfg.QueryActive(PowerCfg.SubPciExpress, PowerCfg.PciAspm) != null,
                Detect = () => PowerCfg.QueryActive(PowerCfg.SubPciExpress, PowerCfg.PciAspm)?.AC == 0,
                // Solo con el cargador (AC): el valor con batería no influye en un sobremesa y en portátiles conviene ahorrar
                Apply = (id, s) => Tracked.SetPowerValue(id, PowerCfg.SubPciExpress, PowerCfg.PciAspm, 0, null),
                Default = s => SetActiveValue(PowerCfg.SubPciExpress, PowerCfg.PciAspm, 1, null),
            };
        }

        // ================================================================ Juegos y FPS
        static IEnumerable<Optimization> Games()
        {
            yield return new Optimization
            {
                Id = "RefreshRate", Category = "games", Impact = Impact.High, Restart = RestartNeed.None,
                Available = s => s.Displays != null && s.Displays.Count > 0,
                Recommended = s => true,
                Detect = () => { var d = Display.GetDisplays(); return d.Count > 0 && !d.Any(x => x.MaxHz > x.CurrentHz); },
                Apply = (id, s) =>
                {
                    foreach (var d in Display.GetDisplays())
                    {
                        if (d.MaxHz <= d.CurrentHz) continue;
                        Tracked.Record(id, new BackupRecord { Kind = "Refresh", Key = "Refresh|" + d.Device, Path = d.Device, IntA = d.CurrentHz });
                        int code = Display.SetFrequency(d.Device, d.MaxHz);
                        if (code != 0) throw new InvalidOperationException(Loc.F("err.refresh", d.Device, d.MaxHz, code));
                        Log.Info(d.Device + ": " + d.CurrentHz + " Hz -> " + d.MaxHz + " Hz");
                    }
                },
            };
            yield return new Optimization
            {
                Id = "GameMode", Category = "games", Impact = Impact.Medium, Restart = RestartNeed.None,
                Reg = new[]
                {
                    Dw(GameBar, "AutoGameModeEnabled", 1, 0).WhenMissing(1),
                    Dw(GameBar, "AllowAutoGameMode", 1, 0).WhenMissing(1),
                },
            };
            yield return new Optimization
            {
                Id = "GameDVR", Category = "games", Impact = Impact.Medium, Restart = RestartNeed.None,
                Reg = new[]
                {
                    Dw(@"HKCU\System\GameConfigStore", "GameDVR_Enabled", 0, 1).WhenMissing(1),
                    Dw(AppCapture, "AppCaptureEnabled", 0, null).WhenMissing(1),
                    Dw(AppCapture, "HistoricalCaptureEnabled", 0, null).WhenMissing(0),
                },
            };
            yield return new Optimization
            {
                Id = "HAGS", Category = "games", Impact = Impact.Medium, Restart = RestartNeed.Reboot,
                Available = s => s.Gpus != null && s.Gpus.Count > 0,
                Recommended = s => s.ModernGpu,
                Reg = new[] { Dw(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, 1) },
            };
            yield return new Optimization
            {
                Id = "WindowedOpt", Category = "games", Impact = Impact.Medium, Restart = RestartNeed.None,
                Detect = () => GetDxSetting("SwapEffectUpgradeEnable") == "1",
                Apply = (id, s) => Tracked.SetReg(id, DxPath, "DirectXUserGlobalSettings", MergeDxSetting("SwapEffectUpgradeEnable", "1"), Str),
                Default = s => Reg.Set(DxPath, "DirectXUserGlobalSettings", MergeDxSetting("SwapEffectUpgradeEnable", "0"), Str),
            };
            yield return new Optimization
            {
                Id = "MMCSS", Category = "games", Impact = Impact.Low, Restart = RestartNeed.Reboot,
                Recommended = s => false,
                Preference = s => true,
                Reg = new[]
                {
                    Dw(MmPath, "SystemResponsiveness", 10, 20),
                    Dw(MmPath, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), 10),
                    Dw(MmPath + @"\Tasks\Games", "GPU Priority", 8, 8),
                    Dw(MmPath + @"\Tasks\Games", "Priority", 6, 2),
                    R(MmPath + @"\Tasks\Games", "Scheduling Category", "High", Str, "Medium"),
                    R(MmPath + @"\Tasks\Games", "SFIO Priority", "High", Str, "Normal"),
                },
            };
        }

        // ================================================================ Ratón y teclado
        static IEnumerable<Optimization> Input()
        {
            yield return new Optimization
            {
                Id = "MouseAccel", Category = "input", Impact = Impact.High, Restart = RestartNeed.None,
                Reg = new[]
                {
                    R(@"HKCU\Control Panel\Mouse", "MouseSpeed", "0", Str, "1"),
                    R(@"HKCU\Control Panel\Mouse", "MouseThreshold1", "0", Str, "6"),
                    R(@"HKCU\Control Panel\Mouse", "MouseThreshold2", "0", Str, "10"),
                },
                Sync = SyncMouse,
            };
            yield return new Optimization
            {
                Id = "KeyRepeat", Category = "input", Impact = Impact.Low, Restart = RestartNeed.None,
                Preference = s => true,
                Reg = new[]
                {
                    R(@"HKCU\Control Panel\Keyboard", "KeyboardDelay", "0", Str, "1"),
                    R(@"HKCU\Control Panel\Keyboard", "KeyboardSpeed", "31", Str, "31"),
                },
                Sync = SyncKeyboard,
            };
            yield return new Optimization
            {
                Id = "AccessKeys", Category = "input", Impact = Impact.Medium, Restart = RestartNeed.None,
                // Solo se cambian los bits necesarios del valor actual: 0x4 = atajo activo; 0x1 = Teclas de filtro encendidas
                Detect = () =>
                {
                    GetAccessFlags(out int sticky, out int filter, out int toggle);
                    return (sticky & 4) == 0 && (toggle & 4) == 0 && (filter & 5) == 0;
                },
                Apply = (id, s) =>
                {
                    GetAccessFlags(out int sticky, out int filter, out int toggle);
                    Tracked.SetReg(id, StickyPath, "Flags", (sticky & ~4).ToString(), Str);
                    Tracked.SetReg(id, FilterPath, "Flags", (filter & ~5).ToString(), Str);
                    Tracked.SetReg(id, TogglePath, "Flags", (toggle & ~4).ToString(), Str);
                },
                Default = s =>
                {
                    GetAccessFlags(out int sticky, out int filter, out int toggle);
                    Reg.Set(StickyPath, "Flags", (sticky | 4).ToString(), Str);
                    Reg.Set(FilterPath, "Flags", (filter | 4).ToString(), Str);
                    Reg.Set(TogglePath, "Flags", (toggle | 4).ToString(), Str);
                },
                Sync = SyncAccessKeys,
            };
            yield return new Optimization
            {
                Id = "UsbSuspend", Category = "input", Impact = Impact.Medium, Restart = RestartNeed.None,
                Detect = () => Active(PowerCfg.SubUsb, PowerCfg.UsbSelectiveSuspend)?.AC == 0,
                // En portátiles solo se cambia la corriente alterna (AC)
                Apply = (id, s) => Tracked.SetPowerValue(id, PowerCfg.SubUsb, PowerCfg.UsbSelectiveSuspend, 0, Laptop(s) ? (int?)null : 0),
                Default = s => SetActiveValue(PowerCfg.SubUsb, PowerCfg.UsbSelectiveSuspend, 1, 1),
            };
        }

        // ================================================================ Red
        static IEnumerable<Optimization> Network()
        {
            yield return new Optimization
            {
                Id = "Nagle", Category = "network", Impact = Impact.Low, Restart = RestartNeed.Reboot,
                Preference = s => true,
                Available = s => ActiveInterfaceKeys().Count > 0,
                Detect = () =>
                {
                    var keys = ActiveInterfaceKeys();
                    if (keys.Count == 0) return false;
                    return keys.All(k => Dw(k, "TcpAckFrequency", 1, null).IsSet() && Dw(k, "TCPNoDelay", 1, null).IsSet());
                },
                Apply = (id, s) =>
                {
                    foreach (var k in ActiveInterfaceKeys())
                    {
                        Tracked.SetReg(id, k, "TcpAckFrequency", 1, DWord);
                        Tracked.SetReg(id, k, "TCPNoDelay", 1, DWord);
                    }
                },
                Default = s =>
                {
                    foreach (var k in ActiveInterfaceKeys())
                    {
                        Reg.Remove(k, "TcpAckFrequency");
                        Reg.Remove(k, "TCPNoDelay");
                    }
                },
            };
            yield return new Optimization
            {
                Id = "DeliveryOpt", Category = "network", Impact = Impact.Low, Restart = RestartNeed.None,
                Reg = new[] { Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0, null) },
            };
        }

        // ================================================================ Segundo plano y RAM
        static IEnumerable<Optimization> Background()
        {
            yield return new Optimization
            {
                Id = "BgApps", Category = "background", Impact = Impact.Medium, Restart = RestartNeed.None,
                Recommended = s => s.RamTotalGB <= 16,
                Preference = s => s.RamTotalGB > 16,
                Reg = new[]
                {
                    Dw(@"HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1, 0).WhenMissing(0),
                    Dw(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Search", "BackgroundAppGlobalToggle", 0, null).WhenMissing(1),
                },
            };
            yield return new Optimization
            {
                Id = "EdgeBg", Category = "background", Impact = Impact.Low, Restart = RestartNeed.None,
                Available = s => s.EdgeInstalled,
                Recommended = s => !s.EdgeIsDefaultBrowser,
                Reg = new[]
                {
                    Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0, null),
                    Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0, null),
                },
            };
            yield return new Optimization
            {
                Id = "Widgets", Category = "background", Impact = Impact.Low, Restart = RestartNeed.Logoff,
                // Con la protección UCPD de Windows 11 activa, ningún programa externo puede cambiarlo
                Available = s => !s.WidgetsLocked,
                Detect = WidgetsOff,
                Apply = (id, s) =>
                {
                    // Windows 11 protege estas claves frente a programas externos (UCPD) en algunas versiones
                    try { Tracked.SetReg(id, DshPath, "AllowNewsAndInterests", 0, DWord); return; }
                    catch (Exception ex) { Log.Warn("Widgets policy blocked by Windows: " + ex.Message); }
                    try { Tracked.SetReg(id, TaskbarAdv, "TaskbarDa", 0, DWord); }
                    catch (Exception ex)
                    {
                        Log.Warn("Widgets TaskbarDa blocked by Windows: " + ex.Message);
                        throw new InvalidOperationException(Loc.T("err.widgets"));
                    }
                },
                Default = s =>
                {
                    try { Reg.Remove(DshPath, "AllowNewsAndInterests"); } catch { }
                    try { Reg.Set(TaskbarAdv, "TaskbarDa", 1, DWord); } catch { }
                    if (WidgetsOff()) throw new InvalidOperationException(Loc.T("err.widgetsRevert"));
                },
            };
            yield return new Optimization
            {
                Id = "Tips", Category = "background", Impact = Impact.Low, Restart = RestartNeed.None,
                Reg = new[]
                {
                    Dw(ContentDelivery, "SubscribedContent-338388Enabled", 0, 1).WhenMissing(1),
                    Dw(ContentDelivery, "SubscribedContent-338389Enabled", 0, 1).WhenMissing(1),
                    Dw(ContentDelivery, "SubscribedContent-353694Enabled", 0, 1).WhenMissing(1),
                    Dw(ContentDelivery, "SubscribedContent-353696Enabled", 0, 1).WhenMissing(1),
                    Dw(ContentDelivery, "SilentInstalledAppsEnabled", 0, 1).WhenMissing(1),
                    Dw(ContentDelivery, "SystemPaneSuggestionsEnabled", 0, 1).WhenMissing(1),
                    Dw(ContentDelivery, "SoftLandingEnabled", 0, 1).WhenMissing(1),
                    Dw(@"HKCU\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0, null).WhenMissing(1),
                },
            };
            yield return new Optimization
            {
                Id = "Telemetry", Category = "background", Impact = Impact.Medium, Restart = RestartNeed.None,
                Detect = () => Services.IsDisabledOrMissing("DiagTrack") && Services.IsDisabledOrMissing("dmwappushservice"),
                Apply = (id, s) =>
                {
                    Tracked.SetService(id, "DiagTrack", ServiceMode.Disabled);
                    Tracked.SetService(id, "dmwappushservice", ServiceMode.Disabled);
                    Tracked.SetReg(id, TelemetryPolicy, "AllowTelemetry", 0, DWord);
                    foreach (var t in TelemetryTasks)
                    {
                        try { Tracked.SetTask(id, t, false); }
                        catch (Exception ex) { Log.Warn("Task " + t + " could not be disabled: " + ex.Message); }
                    }
                },
                Default = s =>
                {
                    if (Services.Exists("DiagTrack")) { Services.SetStart("DiagTrack", ServiceMode.Auto); Services.TryStart("DiagTrack"); }
                    if (Services.Exists("dmwappushservice")) Services.SetStart("dmwappushservice", ServiceMode.Manual);
                    Reg.Remove(TelemetryPolicy, "AllowTelemetry");
                    foreach (var t in TelemetryTasks)
                    {
                        try { if (ScheduledTasks.Exists(t)) ScheduledTasks.SetEnabled(t, true); }
                        catch { }
                    }
                },
            };
            yield return new Optimization
            {
                Id = "SysMain", Category = "background", Impact = Impact.Low, Restart = RestartNeed.None,
                Available = s => Services.GetStartInfo("SysMain") != null,
                Recommended = s => false,
                Preference = s => s.DiskType != "HDD",
                Detect = () => Services.IsDisabledOrMissing("SysMain"),
                Apply = (id, s) => Tracked.SetService(id, "SysMain", ServiceMode.Disabled),
                Default = s => { Services.SetStart("SysMain", ServiceMode.Auto); Services.TryStart("SysMain"); },
            };
            yield return new Optimization
            {
                Id = "WSearch", Category = "background", Impact = Impact.Medium, Restart = RestartNeed.None,
                Available = s => Services.GetStartInfo("WSearch") != null,
                Recommended = s => s.DiskType == "HDD",
                Detect = () => Services.IsDisabledOrMissing("WSearch"),
                Apply = (id, s) => Tracked.SetService(id, "WSearch", ServiceMode.Disabled),
                Default = s => { Services.SetStart("WSearch", ServiceMode.DelayedAuto); Services.TryStart("WSearch"); },
            };
            yield return new Optimization
            {
                Id = "ActivityHistory", Category = "background", Impact = Impact.Low, Restart = RestartNeed.None,
                Reg = new[]
                {
                    Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0, null),
                    Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0, null),
                    Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0, null),
                },
            };
            yield return new Optimization
            {
                Id = "UnusedServices", Category = "background", Impact = Impact.Low, Restart = RestartNeed.None,
                Preference = s => true,
                Available = s => UnusedServiceNames.Any(Services.Exists),
                Detect = () => UnusedServiceNames.All(Services.IsDisabledOrMissing),
                Apply = (id, s) =>
                {
                    foreach (var n in UnusedServiceNames)
                        if (Services.Exists(n)) Tracked.SetService(id, n, ServiceMode.Disabled);
                },
                Default = s =>
                {
                    foreach (var n in UnusedServiceNames)
                        if (Services.Exists(n)) Services.SetStart(n, n == "MapsBroker" ? ServiceMode.DelayedAuto : ServiceMode.Manual);
                },
            };
        }

        static bool WidgetsOff() =>
            Dw(DshPath, "AllowNewsAndInterests", 0, null).IsSet() ||
            Dw(TaskbarAdv, "TaskbarDa", 0, null).WhenMissing(1).IsSet();

        // ================================================================ Interfaz de Windows
        static IEnumerable<Optimization> Interface()
        {
            yield return new Optimization
            {
                Id = "Animations", Category = "interface", Impact = Impact.Low, Restart = RestartNeed.Logoff,
                Recommended = LowEnd,
                Preference = s => !LowEnd(s),
                Reg = new[]
                {
                    R(@"HKCU\Control Panel\Desktop\WindowMetrics", "MinAnimate", "0", Str, "1").WhenMissing("1"),
                    Dw(TaskbarAdv, "TaskbarAnimations", 0, 1).WhenMissing(1),
                    Dw(@"HKCU\Software\Microsoft\Windows\DWM", "EnableAeroPeek", 0, 1).WhenMissing(1),
                },
                Apply = (id, s) =>
                {
                    Tracked.Record(id, new BackupRecord { Kind = "ClientAnim", Key = "ClientAnim", Flag = Spi.GetClientAreaAnimation() });
                    Spi.SetClientAreaAnimation(false);
                },
                Default = s => Spi.SetClientAreaAnimation(true),
                Sync = SyncMinAnimate,
            };
            yield return new Optimization
            {
                Id = "Transparency", Category = "interface", Impact = Impact.Low, Restart = RestartNeed.None,
                Recommended = LowEnd,
                Preference = s => !LowEnd(s),
                Reg = new[] { Dw(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0, 1).WhenMissing(1) },
                Sync = () => Spi.BroadcastSettingChange("ImmersiveColorSet"),
            };
            yield return new Optimization
            {
                Id = "MenuDelay", Category = "interface", Impact = Impact.Low, Restart = RestartNeed.None,
                Preference = s => true,
                Reg = new[] { R(@"HKCU\Control Panel\Desktop", "MenuShowDelay", "50", Str, "400").WhenMissing("400") },
                Sync = SyncMenuDelay,
            };
            yield return new Optimization
            {
                Id = "SearchWeb", Category = "interface", Impact = Impact.Low, Restart = RestartNeed.Logoff,
                Reg = new[] { Dw(@"HKCU\Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1, null) },
            };
        }
    }
}
