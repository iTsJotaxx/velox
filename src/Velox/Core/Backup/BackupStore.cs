using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace Velox.Core.Backup
{
    /// <summary>
    /// Copia de seguridad persistente: para cada optimización (o "game:juego.exe"), la lista de valores
    /// originales que había antes de cambiarlos. Se guarda en %ProgramData%\Velox\backup.xml tras cada cambio.
    /// </summary>
    public static class BackupStore
    {
        static readonly object Sync = new object();
        static Dictionary<string, List<BackupRecord>> _data = new Dictionary<string, List<BackupRecord>>();

        static DataContractSerializer Serializer =>
            new DataContractSerializer(typeof(Dictionary<string, List<BackupRecord>>), new DataContractSerializerSettings { RootName = new System.Xml.XmlDictionary().Add("Backup"), RootNamespace = new System.Xml.XmlDictionary().Add("") });

        public static void Load()
        {
            lock (Sync)
            {
                _data = new Dictionary<string, List<BackupRecord>>();
                if (!File.Exists(AppPaths.BackupFile)) return;
                try
                {
                    using (var fs = File.OpenRead(AppPaths.BackupFile))
                        _data = (Dictionary<string, List<BackupRecord>>)Serializer.ReadObject(fs) ?? new Dictionary<string, List<BackupRecord>>();
                }
                catch (Exception ex)
                {
                    var bad = AppPaths.BackupFile + ".damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    try { File.Copy(AppPaths.BackupFile, bad, true); } catch { }
                    Log.Error("Backup file could not be read (copied to " + bad + ")", ex);
                }
            }
        }

        static void SaveLocked()
        {
            var tmp = AppPaths.BackupFile + ".tmp";
            using (var fs = File.Create(tmp)) Serializer.WriteObject(fs, _data);
            if (File.Exists(AppPaths.BackupFile)) File.Replace(tmp, AppPaths.BackupFile, null);
            else File.Move(tmp, AppPaths.BackupFile);
        }

        public static void Save() { lock (Sync) SaveLocked(); }

        public static bool Has(string id)
        {
            lock (Sync) return _data.TryGetValue(id, out var l) && l.Count > 0;
        }

        public static List<string> Ids()
        {
            lock (Sync) return _data.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key).ToList();
        }

        public static List<BackupRecord> Get(string id)
        {
            lock (Sync) return _data.TryGetValue(id, out var l) ? new List<BackupRecord>(l) : new List<BackupRecord>();
        }

        public static bool ContainsKey(string id, string key)
        {
            lock (Sync) return _data.TryGetValue(id, out var l) && l.Any(r => r.Key == key);
        }

        /// <summary>Guarda un valor original. Si ya había uno con la misma clave se conserva el primero (el auténtico).</summary>
        public static void Add(string id, BackupRecord rec)
        {
            lock (Sync)
            {
                if (!_data.TryGetValue(id, out var l)) { l = new List<BackupRecord>(); _data[id] = l; }
                if (l.Any(r => r.Key == rec.Key)) return;
                l.Add(rec);
                SaveLocked();
            }
        }

        public static void RemoveKey(string id, string key)
        {
            lock (Sync)
            {
                if (!_data.TryGetValue(id, out var l)) return;
                l.RemoveAll(r => r.Key == key);
                if (l.Count == 0) _data.Remove(id);
                SaveLocked();
            }
        }

        /// <summary>Restaura en orden inverso. Los registros que fallan se conservan para reintentar. Devuelve true si había copia.</summary>
        public static bool Restore(string id)
        {
            List<BackupRecord> records;
            lock (Sync)
            {
                if (!_data.TryGetValue(id, out var l) || l.Count == 0) return false;
                records = new List<BackupRecord>(l);
            }
            records.Reverse();
            var failed = new List<BackupRecord>();
            foreach (var r in records)
            {
                try { Restorer.Restore(r); }
                catch (Exception ex)
                {
                    failed.Insert(0, r);
                    Log.Error("Restore " + r.Key, ex);
                }
            }
            lock (Sync)
            {
                if (failed.Count > 0) { _data[id] = failed; Log.Warn(id + ": " + failed.Count + " values could not be restored; kept for retry."); }
                else _data.Remove(id);
                SaveLocked();
            }
            return true;
        }

        public static int RestoreAll()
        {
            int n = 0;
            foreach (var id in Ids()) if (Restore(id)) n++;
            return n;
        }
    }
}
