using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace Velox.Core.Backup
{
    /// <summary>
    /// Un cambio hecho por Velox. Para optimizaciones y ajustes de Windows solo se guarda el id y la acción: los textos
    /// (nombre, BASE → VELOX) se ponen al mostrarlo, en el idioma de ese momento. Para lo demás (DNS) From/To llevan el
    /// valor tal cual.
    /// </summary>
    [DataContract(Name = "Change", Namespace = "")]
    public sealed class ChangeEntry
    {
        [DataMember(Order = 1)] public DateTime When { get; set; }
        /// <summary>Id de la optimización, «tw.X» para los ajustes de Windows, «dns» para el DNS.</summary>
        [DataMember(Order = 2)] public string Id { get; set; }
        /// <summary>"on" (se activó / se cambió), "off" (se volvió a la base).</summary>
        [DataMember(Order = 3)] public string Action { get; set; }
        /// <summary>Quién lo pidió: ui, recommended, undo-all, undo-one, reapply, profile, headless, auto.</summary>
        [DataMember(Order = 4)] public string Source { get; set; }
        [DataMember(Order = 5)] public bool Ok { get; set; } = true;
        [DataMember(Order = 6, EmitDefaultValue = false)] public string Error { get; set; }
        [DataMember(Order = 7, EmitDefaultValue = false)] public string From { get; set; }
        [DataMember(Order = 8, EmitDefaultValue = false)] public string To { get; set; }
    }

    /// <summary>
    /// Historial de cambios (%ProgramData%\Velox\history.xml). A diferencia de la copia de seguridad, que guarda solo el
    /// valor original y se borra al deshacer, aquí queda cada cambio con su fecha. Como mucho MaxEntries (los más viejos
    /// se descartan).
    /// </summary>
    public static class History
    {
        public const int MaxEntries = 1000;
        static readonly object Sync = new object();
        static List<ChangeEntry> _items = new List<ChangeEntry>();

        static string FilePath => Path.Combine(AppPaths.DataDir, "history.xml");

        static DataContractSerializer Serializer =>
            new DataContractSerializer(typeof(List<ChangeEntry>), new DataContractSerializerSettings
            {
                RootName = new System.Xml.XmlDictionary().Add("History"),
                RootNamespace = new System.Xml.XmlDictionary().Add(""),
            });

        public static void Load()
        {
            lock (Sync)
            {
                _items = new List<ChangeEntry>();
                if (!File.Exists(FilePath)) return;
                try
                {
                    using (var fs = File.OpenRead(FilePath))
                        _items = (List<ChangeEntry>)Serializer.ReadObject(fs) ?? new List<ChangeEntry>();
                }
                catch (Exception ex)
                {
                    // Un historial dañado no puede impedir usar Velox: se aparta y se empieza otro
                    try { File.Copy(FilePath, FilePath + ".damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); } catch { }
                    Log.Warn("History could not be read: " + ex.Message);
                }
            }
        }

        public static void Add(ChangeEntry e)
        {
            if (e == null || string.IsNullOrEmpty(e.Id)) return;
            if (e.When == default(DateTime)) e.When = DateTime.Now;
            lock (Sync)
            {
                _items.Add(e);
                if (_items.Count > MaxEntries) _items.RemoveRange(0, _items.Count - MaxEntries);
                try
                {
                    var tmp = FilePath + ".tmp";
                    using (var fs = File.Create(tmp)) Serializer.WriteObject(fs, _items);
                    if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                    else File.Move(tmp, FilePath);
                }
                catch (Exception ex) { Log.Warn("History could not be saved: " + ex.Message); }
            }
        }

        public static void Add(string id, bool on, string source, bool ok = true, string error = null, string from = null, string to = null) =>
            Add(new ChangeEntry { Id = id, Action = on ? "on" : "off", Source = source ?? "ui", Ok = ok, Error = error, From = from, To = to });

        /// <summary>Del más nuevo al más viejo.</summary>
        public static List<ChangeEntry> All()
        {
            lock (Sync) return Enumerable.Reverse(_items).ToList();
        }

        /// <summary>Última acción que salió bien para ese id ("on"/"off"), o null si no hay ninguna.</summary>
        public static string LastAction(string id)
        {
            lock (Sync)
                for (int i = _items.Count - 1; i >= 0; i--)
                    if (_items[i].Ok && string.Equals(_items[i].Id, id, StringComparison.OrdinalIgnoreCase)) return _items[i].Action;
            return null;
        }

        /// <summary>Solo pruebas: empieza vacío sin tocar el archivo.</summary>
        internal static void ResetForTests() { lock (Sync) _items = new List<ChangeEntry>(); }
    }
}
