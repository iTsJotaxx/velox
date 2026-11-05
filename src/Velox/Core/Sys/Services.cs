using System;
using System.ServiceProcess;

namespace Velox.Core.Sys
{
    public enum ServiceMode { Auto, DelayedAuto, Manual, Disabled }

    public sealed class ServiceStartInfo
    {
        /// <summary>Valor Start del registro: 2 = automático, 3 = manual, 4 = deshabilitado.</summary>
        public int? Start { get; set; }
        public bool Delayed { get; set; }
        /// <summary>Valor DelayedAutostart tal cual (para restaurarlo exactamente).</summary>
        public RegState DelayedRaw { get; set; }
    }

    public static class Services
    {
        static string KeyPath(string name) => @"HKLM\SYSTEM\CurrentControlSet\Services\" + name;

        /// <summary>null si el servicio no existe.</summary>
        public static ServiceStartInfo GetStartInfo(string name)
        {
            if (!Reg.KeyExists(KeyPath(name))) return null;
            var raw = Reg.Get(KeyPath(name), "DelayedAutostart");
            int? delayed = Reg.GetInt(KeyPath(name), "DelayedAutostart");
            return new ServiceStartInfo { Start = Reg.GetInt(KeyPath(name), "Start"), Delayed = delayed == 1, DelayedRaw = raw };
        }

        public static bool Exists(string name) => Reg.KeyExists(KeyPath(name));

        public static bool IsDisabledOrMissing(string name)
        {
            var i = GetStartInfo(name);
            return i == null || i.Start == 4;
        }

        public static string ModeArg(ServiceMode m)
        {
            switch (m)
            {
                case ServiceMode.Auto: return "auto";
                case ServiceMode.DelayedAuto: return "delayed-auto";
                case ServiceMode.Manual: return "demand";
                default: return "disabled";
            }
        }

        public static ServiceMode? ModeFromStart(int start, bool delayed)
        {
            switch (start)
            {
                case 2: return delayed ? ServiceMode.DelayedAuto : ServiceMode.Auto;
                case 3: return ServiceMode.Manual;
                case 4: return ServiceMode.Disabled;
                default: return null; // 0/1 = drivers de arranque: no se tocan
            }
        }

        /// <summary>Cambia el tipo de inicio con sc.exe (actualiza el registro y el administrador de servicios).</summary>
        public static void SetStart(string name, ServiceMode mode)
        {
            var r = ProcessRunner.Run("sc.exe", "config \"" + name + "\" start= " + ModeArg(mode), 30000);
            if (r.ExitCode != 0) throw new InvalidOperationException("sc.exe config " + name + " (" + ModeArg(mode) + "): " + r.Output);
        }

        public static void TryStop(string name, int timeoutMs = 20000)
        {
            try
            {
                using (var sc = new ServiceController(name))
                {
                    if (sc.Status == ServiceControllerStatus.Stopped) return;
                    if (sc.CanStop) { sc.Stop(); sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromMilliseconds(timeoutMs)); }
                }
            }
            catch (Exception ex) { Log.Warn("Stop " + name + ": " + ex.Message); }
        }

        public static void TryStart(string name, int timeoutMs = 20000)
        {
            try
            {
                using (var sc = new ServiceController(name))
                {
                    if (sc.Status == ServiceControllerStatus.Running) return;
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromMilliseconds(timeoutMs));
                }
            }
            catch (Exception ex) { Log.Warn("Start " + name + ": " + ex.Message); }
        }

        public static string StatusText(string name)
        {
            try { using (var sc = new ServiceController(name)) return sc.Status.ToString(); }
            catch { return "Missing"; }
        }
    }
}
