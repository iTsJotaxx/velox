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
        void OnDeserializing(StreamingContext c) { CreateRestorePoint = true; }
    }
}
