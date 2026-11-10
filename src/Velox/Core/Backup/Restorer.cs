using System;
using Velox.Core.Sys;
using Velox.Native;

namespace Velox.Core.Backup
{
    /// <summary>Devuelve un valor guardado a su estado original. Lanza excepción si no lo consigue.</summary>
    public static class Restorer
    {
        public static void Restore(BackupRecord r)
        {
            switch (r.Kind)
            {
                case "Reg":
                    if (r.Existed)
                    {
                        var kind = r.RegKind;
                        if (kind == Microsoft.Win32.RegistryValueKind.Unknown || kind == Microsoft.Win32.RegistryValueKind.None)
                            throw new InvalidOperationException("Unsupported registry value type " + r.ValueKind);
                        Reg.Set(r.Path, r.Name, r.GetRegValue(), kind);
                    }
                    else Reg.Remove(r.Path, r.Name);
                    break;

                case "RegKey":
                    if (!r.Existed) Reg.DeleteKeyTree(r.Path);
                    break;

                case "Svc":
                    if (r.IntA.HasValue)
                    {
                        bool delayed = false;
                        try { delayed = r.Existed && Convert.ToInt64(r.GetRegValue() ?? 0) == 1; } catch { }
                        var mode = Services.ModeFromStart(r.IntA.Value, delayed);
                        if (mode.HasValue)
                        {
                            Services.SetStart(r.Name, mode.Value);
                            // sc.exe toca DelayedAutostart: se deja exactamente como estaba
                            string path = @"HKLM\SYSTEM\CurrentControlSet\Services\" + r.Name;
                            if (r.Existed) Reg.Set(path, "DelayedAutostart", r.GetRegValue(), r.RegKind);
                            else Reg.Remove(path, "DelayedAutostart");
                            if (r.IntA.Value == 2) Services.TryStart(r.Name);
                        }
                    }
                    break;

                case "Task":
                    if (ScheduledTasks.Exists(r.Path)) ScheduledTasks.SetEnabled(r.Path, r.Flag == true);
                    break;

                case "PowerVal":
                    PowerCfg.SetValue(r.Path, r.Name, r.Extra, r.IntA, r.IntB);
                    break;

                case "PowerScheme":
                    if (!string.IsNullOrEmpty(r.Path)) PowerCfg.SetActive(r.Path);
                    // Borrar el plan creado por Velox es solo limpieza
                    if (!string.IsNullOrEmpty(r.Extra) && !string.Equals(r.Extra, PowerCfg.ActiveScheme(), StringComparison.OrdinalIgnoreCase))
                        PowerCfg.TryDelete(r.Extra);
                    break;

                case "Overlay":
                    if (!string.IsNullOrEmpty(r.Data) && !PowerOverlay.Set(r.Data))
                        throw new InvalidOperationException("Windows refused to restore the power mode (only possible with the Balanced plan).");
                    break;

                case "Refresh":
                    int code = Display.SetFrequency(r.Path, r.IntA ?? 60);
                    if (code != 0) throw new InvalidOperationException("Display " + r.Path + " could not be set back to " + r.IntA + " Hz (code " + code + ")");
                    break;

                case "ClientAnim":
                    Spi.SetClientAreaAnimation(r.Flag == true);
                    break;

                case "Hibernate":
                    PowerCfg.SetHibernation(r.Flag == true);
                    break;

                default:
                    throw new InvalidOperationException("Unknown backup kind " + r.Kind);
            }
        }
    }
}
