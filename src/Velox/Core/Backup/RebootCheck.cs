using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace Velox.Core.Backup
{
    /// <summary>Cambio que necesita reiniciar (o cerrar sesión) para notarse: se comprueba en el primer arranque posterior.</summary>
    [DataContract(Name = "Pending", Namespace = "")]
    public sealed class RebootPending
    {
        [DataMember(Order = 1)] public string Id { get; set; }
        /// <summary>true = se activó (debería seguir aplicado); false = se devolvió a la base.</summary>
        [DataMember(Order = 2)] public bool On { get; set; }
        [DataMember(Order = 3)] public DateTime AppliedUtc { get; set; }
    }

    public sealed class RebootVerdict
    {
        public string Id { get; set; }
        public bool On { get; set; }
        /// <summary>El valor sigue como se dejó.</summary>
        public bool Kept { get; set; }
        /// <summary>null si Windows no informa del efecto real; si no, si está en marcha como se esperaba.</summary>
        public bool? Effective { get; set; }
        public bool Ok => Kept && Effective != false;
    }

    /// <summary>
    /// Comprobación tras reiniciar (%ProgramData%\Velox\reboot-pending.xml). Antes de reiniciar el valor ya está escrito,
    /// así que releerlo solo demuestra que sobrevivió al reinicio (que Windows Update o una directiva no lo deshicieron).
    /// Donde Windows informa del efecto real (integridad de memoria) se comprueba también eso.
    /// </summary>
    public static class RebootCheck
    {
        static readonly object Sync = new object();
        static string FilePath => Path.Combine(AppPaths.DataDir, "reboot-pending.xml");

        static DataContractSerializer Serializer =>
            new DataContractSerializer(typeof(List<RebootPending>), new DataContractSerializerSettings
            {
                RootName = new System.Xml.XmlDictionary().Add("RebootPending"),
                RootNamespace = new System.Xml.XmlDictionary().Add(""),
            });

        static List<RebootPending> Read()
        {
            try
            {
                if (!File.Exists(FilePath)) return new List<RebootPending>();
                using (var fs = File.OpenRead(FilePath)) return (List<RebootPending>)Serializer.ReadObject(fs) ?? new List<RebootPending>();
            }
            catch (Exception ex) { Log.Warn("Reboot check list could not be read: " + ex.Message); return new List<RebootPending>(); }
        }

        static void Write(List<RebootPending> list)
        {
            try
            {
                if (list.Count == 0) { if (File.Exists(FilePath)) File.Delete(FilePath); return; }
                using (var fs = File.Create(FilePath)) Serializer.WriteObject(fs, list);
            }
            catch (Exception ex) { Log.Warn("Reboot check list could not be saved: " + ex.Message); }
        }

        /// <summary>Anota un cambio que necesita reiniciar. Si se deshace antes de reiniciar, gana la última acción.</summary>
        public static void Track(string id, bool on)
        {
            lock (Sync)
            {
                var list = Read();
                list.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
                list.Add(new RebootPending { Id = id, On = on, AppliedUtc = DateTime.UtcNow });
                Write(list);
            }
        }

        public static List<RebootPending> Pending() { lock (Sync) return Read(); }

        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();

        /// <summary>Hora (UTC) del último arranque de Windows. GetTickCount64 no se desborda (Environment.TickCount sí, a los 25 días).</summary>
        public static DateTime LastBootUtc() => DateTime.UtcNow - TimeSpan.FromMilliseconds(GetTickCount64());

        /// <summary>
        /// Lógica pura (se prueba sin reiniciar): separa lo que ya pasó por un reinicio y da su veredicto.
        /// remaining = lo que todavía espera a un reinicio.
        /// </summary>
        public static List<RebootVerdict> Evaluate(List<RebootPending> pending, DateTime lastBootUtc,
            Func<string, bool?> isApplied, Func<string, bool?> effective, out List<RebootPending> remaining)
        {
            var verdicts = new List<RebootVerdict>();
            remaining = new List<RebootPending>();
            foreach (var p in pending ?? new List<RebootPending>())
            {
                // Margen de un minuto: el reloj de arranque y el de la anotación no son exactamente el mismo
                if (lastBootUtc <= p.AppliedUtc.AddMinutes(1)) { remaining.Add(p); continue; }
                bool? applied = isApplied(p.Id);
                if (applied == null) continue; // ya no existe en esta versión: nada que comprobar
                bool? eff = effective(p.Id);
                verdicts.Add(new RebootVerdict
                {
                    Id = p.Id,
                    On = p.On,
                    Kept = applied.Value == p.On,
                    Effective = eff == null ? (bool?)null : eff.Value == p.On,
                });
            }
            return verdicts;
        }

        /// <summary>Al arrancar Velox: comprueba lo que ya pasó por un reinicio y lo quita de la lista.</summary>
        public static List<RebootVerdict> CheckNow(Func<string, bool?> isApplied, Func<string, bool?> effective)
        {
            lock (Sync)
            {
                var all = Read();
                if (all.Count == 0) return new List<RebootVerdict>();
                var verdicts = Evaluate(all, LastBootUtc(), isApplied, effective, out var remaining);
                Write(remaining);
                foreach (var v in verdicts)
                    Log.Info("After restart: " + v.Id + (v.On ? " (on)" : " (off)") + " -> " + (v.Ok ? "confirmed" : "NOT in effect")
                             + (v.Effective.HasValue ? " (checked with Windows)" : "") + ".");
                return verdicts;
            }
        }
    }
}
