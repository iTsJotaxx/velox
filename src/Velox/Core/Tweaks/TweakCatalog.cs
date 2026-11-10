using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Velox.Core.Backup;
using Velox.Core.Optimizations;
using Velox.Localization;
using Velox.Native;

namespace Velox.Core.Tweaks
{
    public sealed class TweakGroup
    {
        public string Id { get; set; }
        public string Icon { get; set; }
        public string Name => Loc.T("tweakgrp." + Id);
        public string Description => Loc.T("tweakgrp." + Id + ".desc");
    }

    /// <summary>
    /// Ajuste de comodidad de Windows (barra de tareas, Explorador...). Se aplica y se deshace con el mismo motor
    /// que las optimizaciones (copia en BackupStore con id "tw.{Key}"), pero no cuenta para la puntuación.
    /// Textos: "tweak.{Key}.name" y "tweak.{Key}.desc" en Localization\{es,en}\Tweaks.xaml.
    /// </summary>
    public sealed class Tweak
    {
        public Optimization Opt { get; set; }
        public string Group { get; set; }
        /// <summary>Se ve del todo al reiniciar el Explorador.</summary>
        public bool NeedsExplorer { get; set; }
        /// <summary>Solo para Windows 11 (texto del motivo si no está disponible).</summary>
        public bool Win11Only { get; set; }

        public string Key => Opt.Id.Substring(3);
        public string Name => Loc.T("tweak." + Key + ".name");
        public string Description => Loc.T("tweak." + Key + ".desc");
        public bool IsAvailable => OptimizationEngine.IsAvailable(Opt, NoSys);
        static readonly Scan.SystemInfo NoSys = new Scan.SystemInfo(); // las condiciones de los ajustes no usan el escaneo
        public bool IsApplied => OptimizationEngine.IsApplied(Opt);
    }

    public static class TweakCatalog
    {
        public static readonly List<TweakGroup> Groups = new List<TweakGroup>();
        public static readonly List<Tweak> All = new List<Tweak>();

        const string Adv = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        const string Personalize = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        const string ContentDelivery = @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        const string ClassicMenuClsid = @"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
        const string ClassicMenuInproc = ClassicMenuClsid + @"\InprocServer32";

        static TweakCatalog()
        {
            Groups.Add(new TweakGroup { Id = "taskbar", Icon = "" });
            Groups.Add(new TweakGroup { Id = "explorer", Icon = "" });
            Groups.Add(new TweakGroup { Id = "look", Icon = "" });
            Groups.Add(new TweakGroup { Id = "system", Icon = "" });

            // ------------------------------------------------ Barra de tareas
            Add("taskbar", "EndTask", explorer: false, minBuild: 22631,
                reg: new[] { Dw(Adv + @"\TaskbarDeveloperSettings", "TaskbarEndTask", 1, null).WhenMissing(0) },
                sync: () => Spi.BroadcastSettingChange("TraySettings"));
            Add("taskbar", "TaskbarLeft", explorer: false, minBuild: 22000,
                reg: new[] { Dw(Adv, "TaskbarAl", 0, 1).WhenMissing(1) },
                sync: () => Spi.BroadcastSettingChange("TraySettings"));
            Add("taskbar", "ClockSeconds", explorer: true,
                reg: new[] { Dw(Adv, "ShowSecondsInSystemClock", 1, 0).WhenMissing(0) },
                sync: () => Spi.BroadcastSettingChange("TraySettings"));
            Add("taskbar", "HideTaskView", explorer: false,
                reg: new[] { Dw(Adv, "ShowTaskViewButton", 0, 1).WhenMissing(1) },
                sync: () => Spi.BroadcastSettingChange("TraySettings"));

            // ------------------------------------------------ Explorador de archivos
            Add("explorer", "FileExt", explorer: false,
                reg: new[] { Dw(Adv, "HideFileExt", 0, 1).WhenMissing(1) }, sync: RefreshShell);
            Add("explorer", "Hidden", explorer: false,
                reg: new[] { Dw(Adv, "Hidden", 1, 2).WhenMissing(2) }, sync: RefreshShell);
            Add("explorer", "ThisPC", explorer: false,
                reg: new[] { Dw(Adv, "LaunchTo", 1, null).WhenMissing(2) });
            Add("explorer", "FullPath", explorer: false,
                reg: new[] { Dw(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState", "FullPath", 1, 0).WhenMissing(0) }, sync: RefreshShell);
            Add("explorer", "Checkboxes", explorer: false,
                reg: new[] { Dw(Adv, "AutoCheckSelect", 1, 0).WhenMissing(0) }, sync: RefreshShell);
            Add("explorer", "Compact", explorer: false, minBuild: 22000,
                reg: new[] { Dw(Adv, "UseCompactMode", 1, 0).WhenMissing(0) }, sync: RefreshShell);
            Add("explorer", "NoSyncAds", explorer: false,
                reg: new[] { Dw(Adv, "ShowSyncProviderNotifications", 0, 1).WhenMissing(1) });
            All.Add(new Tweak
            {
                Group = "explorer", NeedsExplorer = true, Win11Only = true,
                Opt = new Optimization
                {
                    Id = "tw.ClassicMenu", Category = "explorer", Restart = RestartNeed.None,
                    Available = s => Build >= 22000,
                    Detect = () => Reg.KeyExists(ClassicMenuInproc) && Reg.Get(ClassicMenuInproc, "").Existed,
                    Apply = (id, s) =>
                    {
                        Tracked.EnsureKey(id, ClassicMenuClsid);
                        Tracked.EnsureKey(id, ClassicMenuInproc);
                        Tracked.SetReg(id, ClassicMenuInproc, "", "", RegistryValueKind.String);
                    },
                    Default = s => Reg.DeleteKeyTree(ClassicMenuClsid),
                    Sync = RefreshShell,
                },
            });

            // ------------------------------------------------ Apariencia
            Add("look", "DarkMode", explorer: false,
                reg: new[]
                {
                    Dw(Personalize, "AppsUseLightTheme", 0, 1).WhenMissing(1),
                    Dw(Personalize, "SystemUsesLightTheme", 0, 1).WhenMissing(1),
                },
                sync: () => Spi.BroadcastSettingChange("ImmersiveColorSet"));
            Add("look", "LockScreenTips", explorer: false,
                reg: new[]
                {
                    Dw(ContentDelivery, "RotatingLockScreenOverlayEnabled", 0, 1).WhenMissing(1),
                    Dw(ContentDelivery, "SubscribedContent-338387Enabled", 0, 1).WhenMissing(1),
                });

            // ------------------------------------------------ Sistema
            Add("system", "StartupDelay", explorer: false,
                reg: new[] { Dw(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0, null) });
            Add("system", "VerboseStatus", explorer: false,
                reg: new[] { Dw(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "VerboseStatus", 1, null).WhenMissing(0) });

        }

        /// <summary>Claves que algún ajuste crea o borra enteras (para comprobar en las pruebas que vuelven a su estado).</summary>
        public static readonly string[] TrackedKeys = { ClassicMenuClsid, ClassicMenuInproc };

        static RegItem Dw(string path, string name, int value, int? def) => new RegItem(path, name, value, RegistryValueKind.DWord, def);
        static RegItem R(string path, string name, object value, RegistryValueKind kind, object def) => new RegItem(path, name, value, kind, def);

        static void Add(string group, string key, bool explorer, RegItem[] reg, Action sync = null, int minBuild = 0)
        {
            All.Add(new Tweak
            {
                Group = group,
                NeedsExplorer = explorer,
                Win11Only = minBuild >= 22000,
                Opt = new Optimization
                {
                    Id = "tw." + key,
                    Category = group,
                    Reg = reg,
                    Available = minBuild > 0 ? (Func<Scan.SystemInfo, bool>)(s => Build >= minBuild) : null,
                    Sync = sync,
                },
            });
        }

        static int _build = -1;
        /// <summary>Número de compilación de Windows (22000+ = Windows 11).</summary>
        public static int Build
        {
            get
            {
                if (_build < 0)
                {
                    var v = Reg.Get(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber");
                    _build = int.TryParse(Convert.ToString(v.Value), out int b) ? b : 0;
                }
                return _build;
            }
        }

        [DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        /// <summary>Avisa al Explorador de que han cambiado sus opciones (ventanas abiertas se refrescan).</summary>
        public static void RefreshShell()
        {
            try
            {
                Spi.BroadcastSettingChange("ShellState");
                SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED
            }
            catch (Exception ex) { Log.Warn("Shell refresh: " + ex.Message); }
        }
    }
}
