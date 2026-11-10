using System;
using Microsoft.Win32;
using Velox.Core.Sys;

namespace Velox.Core.Backup
{
    /// <summary>
    /// Cambios con copia de seguridad: guardan el valor original (una sola vez) y luego cambian.
    /// Si el cambio falla, la copia recién guardada se descarta.
    /// </summary>
    public static class Tracked
    {
        public static void SetReg(string id, string path, string name, object value, RegistryValueKind kind)
        {
            string key = "Reg|" + path + "|" + name;
            bool isNew = !BackupStore.ContainsKey(id, key);
            var rec = new BackupRecord { Kind = "Reg", Key = key, Path = path, Name = name };
            rec.SetRegState(Reg.Get(path, name));
            BackupStore.Add(id, rec);
            try { Reg.Set(path, name, value, kind); }
            catch
            {
                if (isNew) BackupStore.RemoveKey(id, key);
                throw;
            }
        }

        /// <summary>Borra un valor de registro guardando antes su copia (al deshacer vuelve a estar como estaba).</summary>
        public static void RemoveReg(string id, string path, string name)
        {
            var st = Reg.Get(path, name);
            if (!st.Existed) return;
            string key = "Reg|" + path + "|" + name;
            bool isNew = !BackupStore.ContainsKey(id, key);
            var rec = new BackupRecord { Kind = "Reg", Key = key, Path = path, Name = name };
            rec.SetRegState(st);
            BackupStore.Add(id, rec);
            try { Reg.Remove(path, name); }
            catch
            {
                if (isNew) BackupStore.RemoveKey(id, key);
                throw;
            }
        }

        /// <summary>Crea una clave de registro; si no existía, al deshacer se borra entera.</summary>
        public static void EnsureKey(string id, string path)
        {
            string key = "Key|" + path;
            bool isNew = !BackupStore.ContainsKey(id, key);
            BackupStore.Add(id, new BackupRecord { Kind = "RegKey", Key = key, Path = path, Existed = Reg.KeyExists(path) });
            try { Reg.CreateKey(path); }
            catch
            {
                if (isNew) BackupStore.RemoveKey(id, key);
                throw;
            }
        }

        public static void SetService(string id, string name, ServiceMode mode)
        {
            var info = Services.GetStartInfo(name);
            if (info == null) { Log.Info("Service " + name + " does not exist; skipped."); return; }
            string key = "Svc|" + name;
            bool isNew = !BackupStore.ContainsKey(id, key);
            var rec = new BackupRecord { Kind = "Svc", Key = key, Name = name, IntA = info.Start };
            rec.SetRegState(info.DelayedRaw);
            BackupStore.Add(id, rec);
            try { Services.SetStart(name, mode); }
            catch
            {
                if (isNew) BackupStore.RemoveKey(id, key);
                throw;
            }
            if (mode == ServiceMode.Disabled) Services.TryStop(name);
        }

        public static void SetTask(string id, string fullPath, bool enable)
        {
            bool? cur = ScheduledTasks.IsEnabled(fullPath);
            if (cur == null) return; // la tarea no existe en esta versión de Windows
            string key = "Task|" + fullPath;
            bool isNew = !BackupStore.ContainsKey(id, key);
            BackupStore.Add(id, new BackupRecord { Kind = "Task", Key = key, Path = fullPath, Flag = cur });
            try { ScheduledTasks.SetEnabled(fullPath, enable); }
            catch
            {
                if (isNew) BackupStore.RemoveKey(id, key);
                throw;
            }
        }

        /// <summary>Cambia un ajuste del plan de energía activo. Comprueba antes que el valor esté dentro del rango del plan.</summary>
        public static void SetPowerValue(string id, string sub, string setting, int? ac, int? dc)
        {
            string scheme = PowerCfg.ActiveScheme() ?? throw new InvalidOperationException("Active power plan could not be read.");
            var cur = PowerCfg.Query(scheme, sub, setting) ?? throw new InvalidOperationException("Current power setting could not be read; nothing changed.");
            foreach (var v in new[] { ac, dc })
                if (v.HasValue && cur.Max.HasValue && (v.Value < cur.Min || v.Value > cur.Max))
                    throw new InvalidOperationException(Localization.Loc.F("err.powerRange", PowerCfg.ActiveSchemeName(), cur.Min, cur.Max));
            string key = "PowerVal|" + scheme + "|" + setting;
            bool isNew = !BackupStore.ContainsKey(id, key);
            BackupStore.Add(id, new BackupRecord { Kind = "PowerVal", Key = key, Path = scheme, Name = sub, Extra = setting, IntA = cur.AC, IntB = cur.DC });
            try { PowerCfg.SetValue(scheme, sub, setting, ac, dc); }
            catch
            {
                if (isNew) BackupStore.RemoveKey(id, key);
                throw;
            }
        }

        /// <summary>Guarda un registro genérico (plan de energía, modo, Hz, animación...).</summary>
        public static void Record(string id, BackupRecord rec) => BackupStore.Add(id, rec);
    }
}
