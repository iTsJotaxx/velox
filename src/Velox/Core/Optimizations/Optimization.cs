using System;
using System.Collections.Generic;
using Velox.Core.Backup;
using Velox.Core.Scan;
using Velox.Localization;

namespace Velox.Core.Optimizations
{
    public enum Impact { High, Medium, Low }
    public enum RestartNeed { None, Reboot, Logoff }

    public sealed class Category
    {
        public string Id { get; set; }
        /// <summary>Glifo de Segoe Fluent Icons / MDL2 (p. ej. "").</summary>
        public string Icon { get; set; }
        public string Name => Loc.T("cat." + Id);
        public string Description => Loc.T("cat." + Id + ".desc");
    }

    /// <summary>
    /// Una optimización. Estado aplicado = todos los Reg coinciden Y (si hay) Detect devuelve true.
    /// Apply: primero los Reg (con copia), luego Apply extra. Revert: restaura la copia; si sigue aplicada
    /// (no había copia o el original ya era el optimizado), aplica los valores por defecto de Windows.
    /// Textos: "opt.{Id}.name" y "opt.{Id}.desc" en Localization\{es,en}\Opts.xaml.
    /// </summary>
    public sealed class Optimization
    {
        public string Id { get; set; }
        public string Category { get; set; }
        public Impact Impact { get; set; } = Impact.Medium;
        public RestartNeed Restart { get; set; } = RestartNeed.None;

        /// <summary>Valores de registro declarativos (opcional).</summary>
        public RegItem[] Reg { get; set; } = new RegItem[0];
        /// <summary>¿Se recomienda activarla en este equipo? (si no, la configuración recomendada la deja desactivada)</summary>
        public Func<SystemInfo, bool> Recommended { get; set; } = s => true;
        /// <summary>
        /// «A tu gusto»: en este equipo es cuestión de preferencia (o el efecto es mínimo), así que el modo
        /// Recomendado no la toca y no cuenta para la puntuación. null = nunca.
        /// </summary>
        public Func<SystemInfo, bool> Preference { get; set; }
        /// <summary>null = siempre disponible.</summary>
        public Func<SystemInfo, bool> Available { get; set; }
        /// <summary>Detección extra (además de Reg).</summary>
        public Func<bool> Detect { get; set; }
        /// <summary>Cambios no declarativos (id de la optimización, info del equipo). Usar Tracked.* para guardar copia.</summary>
        public Action<string, SystemInfo> Apply { get; set; }
        /// <summary>Valores por defecto de Windows (además de los Default de Reg) al desactivar sin copia.</summary>
        public Action<SystemInfo> Default { get; set; }
        /// <summary>Aplica al instante lo que hay en el registro (SystemParametersInfo, avisos...).</summary>
        public Action Sync { get; set; }

        public string Name => Loc.T("opt." + Id + ".name");
        public string Description => Loc.T("opt." + Id + ".desc");
    }

    public static class OptimizationEngine
    {
        public static bool IsApplied(Optimization o)
        {
            try
            {
                foreach (var it in o.Reg) if (!it.IsSet()) return false;
                if (o.Detect != null) return o.Detect();
                return o.Reg.Length > 0;
            }
            catch (Exception ex)
            {
                Log.Warn("Detect " + o.Id + ": " + ex.Message);
                return false;
            }
        }

        public static bool IsAvailable(Optimization o, SystemInfo s)
        {
            if (o.Available == null || s == null) return true;
            try { return o.Available(s); } catch { return false; }
        }

        public static bool IsRecommended(Optimization o, SystemInfo s)
        {
            if (s == null || o.Recommended == null) return false;
            try { return o.Recommended(s); } catch { return false; }
        }

        public static bool IsPreference(Optimization o, SystemInfo s)
        {
            if (s == null || o.Preference == null) return false;
            try { return o.Preference(s); } catch { return false; }
        }

        static void Sync(Optimization o)
        {
            if (o.Sync == null) return;
            try { o.Sync(); } catch (Exception ex) { Log.Warn("Sync " + o.Id + ": " + ex.Message); }
        }

        public static void Apply(Optimization o, SystemInfo s)
        {
            try
            {
                foreach (var it in o.Reg)
                {
                    // Si sin el valor Windows ya hace lo que se quiere, se deja sin valor (como viene de fábrica)
                    if (it.HasEffective && Core.Reg.ValuesEqual(it.Effective, it.Value, it.Kind)) Tracked.RemoveReg(o.Id, it.Path, it.Name);
                    else Tracked.SetReg(o.Id, it.Path, it.Name, it.Value, it.Kind);
                }
                o.Apply?.Invoke(o.Id, s);
            }
            catch
            {
                // Si falla a medias, se deshace lo que ya se había cambiado
                try { BackupStore.Restore(o.Id); Sync(o); } catch { }
                throw;
            }
            Sync(o);
            Log.Info("Enabled: " + o.Id);
        }

        public static void Revert(Optimization o, SystemInfo s)
        {
            bool hadBackup = BackupStore.Restore(o.Id);
            if (IsApplied(o))
            {
                foreach (var it in o.Reg)
                {
                    if (it.Default == null) Core.Reg.Remove(it.Path, it.Name);
                    else Core.Reg.Set(it.Path, it.Name, it.Default, it.Kind);
                }
                o.Default?.Invoke(s);
                if (!hadBackup) Log.Info(o.Id + ": no previous backup, Windows defaults applied.");
            }
            Sync(o);
            Log.Info("Disabled: " + o.Id);
        }
    }
}
