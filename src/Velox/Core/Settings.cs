using System;
using System.IO;
using System.Runtime.Serialization;

namespace Velox.Core
{
    /// <summary>Preferencias del usuario (idioma, punto de restauración...). Se guardan en %AppData%\Velox\settings.xml.</summary>
    [DataContract(Name = "Settings", Namespace = "")]
    public sealed class Settings
    {
        [DataMember] public string Language { get; set; }            // "es", "en" o null = idioma de Windows
        [DataMember] public bool CreateRestorePoint { get; set; } = true;
        [DataMember] public bool ExpandAllCategories { get; set; }

        // Qué hace «Liberar RAM» (sección Memoria)
        [DataMember] public bool MemWorkingSets { get; set; } = true;
        [DataMember] public bool MemModified { get; set; } = true;
        [DataMember] public bool MemFileCache { get; set; } = true;
        [DataMember] public bool MemStandby { get; set; } = true;
        [DataMember] public bool MemCombine { get; set; } = true;
        /// <summary>Limpieza automática de la lista en espera mientras Velox está abierto.</summary>
        [DataMember] public bool AutoMemClean { get; set; }
        /// <summary>% de RAM disponible por debajo del cual actúa la limpieza automática.</summary>
        [DataMember] public int AutoMemThreshold { get; set; } = 20;

        public static Settings Current { get; private set; } = new Settings();

        public static void Load()
        {
            try
            {
                if (!File.Exists(AppPaths.SettingsFile)) { Current = new Settings(); return; }
                using (var fs = File.OpenRead(AppPaths.SettingsFile))
                    Current = (Settings)new DataContractSerializer(typeof(Settings)).ReadObject(fs);
            }
            catch (Exception ex)
            {
                Log.Warn("Settings could not be read: " + ex.Message);
                Current = new Settings();
            }
        }

        public static void Save()
        {
            try
            {
                var tmp = AppPaths.SettingsFile + ".tmp";
                using (var fs = File.Create(tmp))
                    new DataContractSerializer(typeof(Settings)).WriteObject(fs, Current);
                if (File.Exists(AppPaths.SettingsFile)) File.Delete(AppPaths.SettingsFile);
                File.Move(tmp, AppPaths.SettingsFile);
            }
            catch (Exception ex) { Log.Warn("Settings could not be saved: " + ex.Message); }
        }

        [OnDeserializing]
        void OnDeserializing(StreamingContext c)
        {
            // El serializador no llama al constructor: valores por defecto para lo que no esté en el archivo
            CreateRestorePoint = true;
            MemWorkingSets = MemModified = MemFileCache = MemStandby = MemCombine = true;
            AutoMemThreshold = 20;
        }
    }
}
