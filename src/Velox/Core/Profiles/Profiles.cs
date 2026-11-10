using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Velox.Core.Apps;
using Velox.Core.Optimizations;
using Velox.Core.Scan;
using Velox.Core.Tweaks;
using Velox.Localization;

namespace Velox.Core.Profiles
{
    [DataContract(Name = "App", Namespace = "")]
    public sealed class ProfileApp
    {
        [DataMember(Order = 1)] public string Id { get; set; }
        [DataMember(Order = 2, EmitDefaultValue = false)] public string Name { get; set; }
        /// <summary>Paquete de la región elegida (Riot), si la app tiene varias.</summary>
        [DataMember(Order = 3, EmitDefaultValue = false)] public string Variant { get; set; }
        [DataMember(Order = 4, EmitDefaultValue = false)] public string Source { get; set; }
    }

    /// <summary>Respuestas del asistente de primer uso (null = no contestada: se usa lo que detecta Velox).</summary>
    [DataContract(Name = "Answers", Namespace = "")]
    public sealed class ProfileAnswers
    {
        [DataMember(Order = 1, EmitDefaultValue = false)] public bool? Laptop { get; set; }
        [DataMember(Order = 2, EmitDefaultValue = false)] public bool? Competitive { get; set; }
        [DataMember(Order = 3, EmitDefaultValue = false)] public bool? Obs { get; set; }
    }

    [DataContract(Name = "Memory", Namespace = "")]
    public sealed class ProfileMemory
    {
        [DataMember(Order = 1)] public bool WorkingSets { get; set; } = true;
        [DataMember(Order = 2)] public bool Modified { get; set; } = true;
        [DataMember(Order = 3)] public bool FileCache { get; set; } = true;
        [DataMember(Order = 4)] public bool Standby { get; set; } = true;
        [DataMember(Order = 5)] public bool Combine { get; set; } = true;
        [DataMember(Order = 6)] public bool AutoClean { get; set; }
        [DataMember(Order = 7)] public int AutoThreshold { get; set; } = 20;
    }

    /// <summary>
    /// Configuración de Velox en un archivo (.json): qué optimizaciones y ajustes de Windows están activados, qué apps del
    /// catálogo hay instaladas, las opciones de Memoria y las respuestas del asistente. Es lo mismo para «Exportar» (kit
    /// para después de formatear) y para los perfiles guardados.
    /// </summary>
    [DataContract(Name = "VeloxProfile", Namespace = "")]
    public sealed class VeloxProfile
    {
        public const int CurrentSchema = 1;
        [DataMember(Order = 1)] public int Schema { get; set; } = CurrentSchema;
        [DataMember(Order = 2)] public string Name { get; set; }
        [DataMember(Order = 3)] public DateTime Created { get; set; } = DateTime.Now;
        [DataMember(Order = 4)] public string AppVersion { get; set; } = AppInfo.Version;
        [DataMember(Order = 5)] public Dictionary<string, bool> Optimizations { get; set; } = new Dictionary<string, bool>();
        [DataMember(Order = 6)] public Dictionary<string, bool> Tweaks { get; set; } = new Dictionary<string, bool>();
        [DataMember(Order = 7, EmitDefaultValue = false)] public List<ProfileApp> Apps { get; set; }
        [DataMember(Order = 8, EmitDefaultValue = false)] public ProfileMemory Memory { get; set; }
        [DataMember(Order = 9, EmitDefaultValue = false)] public ProfileAnswers Answers { get; set; }

        public int SettingsCount => (Optimizations?.Count(kv => kv.Value) ?? 0) + (Tweaks?.Count(kv => kv.Value) ?? 0);
    }

    /// <summary>Qué haría importar un perfil en este equipo.</summary>
    public sealed class ImportPlan
    {
        public VeloxProfile Profile { get; set; }
        /// <summary>Cambios a aplicar (solo lo que difiere del estado actual).</summary>
        public List<OptChange> Changes { get; } = new List<OptChange>();
        /// <summary>Lo que no se puede aplicar aquí, ya traducido («nombre: motivo»).</summary>
        public List<string> Skipped { get; } = new List<string>();
        /// <summary>Apps válidas que todavía no están instaladas.</summary>
        public List<ProfileApp> AppsToInstall { get; } = new List<ProfileApp>();
        public int UnchangedCount { get; set; }
    }

    public static class ProfileStore
    {
        /// <summary>Un perfil razonable ocupa pocos KB: un archivo enorme no es un perfil de Velox.</summary>
        const long MaxFileBytes = 1024 * 1024;
        public const string Extension = ".velox.json";

        static DataContractJsonSerializer Serializer => new DataContractJsonSerializer(typeof(VeloxProfile),
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true, DateTimeFormat = new DateTimeFormat("yyyy-MM-ddTHH:mm:ss") });

        // ------------------------------------------------------------ Foto del estado actual

        /// <summary>
        /// El estado real de este equipo. installed = paquetes de winget (id → versión) para incluir las apps del
        /// catálogo que hay instaladas; null para no incluir apps (perfiles de ajustes).
        /// </summary>
        public static VeloxProfile Capture(SystemInfo s, string name, Dictionary<string, string> installed = null)
        {
            var p = new VeloxProfile { Name = name };
            foreach (var o in Catalog.All)
                if (OptimizationEngine.IsAvailable(o, s) && !OptimizationEngine.IsLocked(o, s))
                    p.Optimizations[o.Id] = OptimizationEngine.IsApplied(o);
            foreach (var t in TweakCatalog.All)
                if (OptimizationEngine.IsAvailable(t.Opt, s) && !OptimizationEngine.IsLocked(t.Opt, s))
                    p.Tweaks[t.Opt.Id] = OptimizationEngine.IsApplied(t.Opt);

            var st = Settings.Current;
            p.Memory = new ProfileMemory
            {
                WorkingSets = st.MemWorkingSets, Modified = st.MemModified, FileCache = st.MemFileCache, Standby = st.MemStandby,
                Combine = st.MemCombine, AutoClean = st.AutoMemClean, AutoThreshold = st.AutoMemThreshold,
            };
            if (st.UserLaptop != null || st.UserCompetitive != null || st.UserObs != null)
                p.Answers = new ProfileAnswers { Laptop = st.UserLaptop, Competitive = st.UserCompetitive, Obs = st.UserObs };

            if (installed != null)
            {
                p.Apps = new List<ProfileApp>();
                foreach (var a in AppCatalog.BuiltIn.Concat(AppCatalog.LoadCustom()))
                {
                    if (a.Variants != null)
                    {
                        var v = a.Variants.FirstOrDefault(x => installed.ContainsKey(x.Id));
                        if (v != null) p.Apps.Add(new ProfileApp { Id = a.Id, Name = a.Name, Variant = v.Id });
                    }
                    else if (installed.ContainsKey(a.Id))
                        p.Apps.Add(new ProfileApp { Id = a.Id, Name = a.Name, Source = a.Source });
                }
            }
            return p;
        }

        // ------------------------------------------------------------ Archivos

        public static void Save(VeloxProfile p, string path)
        {
            var tmp = path + ".tmp";
            using (var fs = File.Create(tmp))
            using (var w = JsonReaderWriterFactory.CreateJsonWriter(fs, Encoding.UTF8, true, true, "  "))
                Serializer.WriteObject(w, p);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            Log.Info("Profile saved: " + path + " (" + p.SettingsCount + " settings on" + (p.Apps != null ? ", " + p.Apps.Count + " apps" : "") + ").");
        }

        /// <summary>Lee y valida un perfil. Lanza InvalidOperationException con un mensaje ya traducido.</summary>
        public static VeloxProfile Load(string path)
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) throw new InvalidOperationException(Loc.T("prof.err.missing"));
            if (fi.Length > MaxFileBytes) throw new InvalidOperationException(Loc.T("prof.err.notProfile"));
            VeloxProfile p;
            try
            {
                using (var fs = fi.OpenRead()) p = (VeloxProfile)Serializer.ReadObject(fs);
            }
            catch (Exception ex)
            {
                Log.Warn("Profile " + path + " could not be read: " + ex.Message);
                throw new InvalidOperationException(Loc.T("prof.err.notProfile"));
            }
            if (p == null || p.Schema < 1) throw new InvalidOperationException(Loc.T("prof.err.notProfile"));
            if (p.Schema > VeloxProfile.CurrentSchema) throw new InvalidOperationException(Loc.T("prof.err.newer"));
            p.Optimizations = p.Optimizations ?? new Dictionary<string, bool>();
            p.Tweaks = p.Tweaks ?? new Dictionary<string, bool>();
            if (string.IsNullOrWhiteSpace(p.Name)) p.Name = Path.GetFileName(path).Replace(Extension, "");
            return p;
        }

        // ------------------------------------------------------------ Qué cambiaría

        /// <summary>
        /// Compara el perfil con este equipo: solo entra lo que cambia, y se descarta (con motivo) lo que aquí no existe,
        /// no aplica o bloquea Windows. Las apps se validan (identificador de winget correcto) y no se instala nada: solo
        /// se marcan.
        /// </summary>
        public static ImportPlan Plan(VeloxProfile p, SystemInfo s, Dictionary<string, string> installed = null)
        {
            var plan = new ImportPlan { Profile = p };
            void Add(string id, bool want, bool tweak)
            {
                var o = tweak ? TweakCatalog.All.Select(t => t.Opt).FirstOrDefault(x => x.Id == id)
                              : Catalog.All.FirstOrDefault(x => x.Id == id);
                if (o == null) { plan.Skipped.Add(id + ": " + Loc.T("prof.skip.unknown")); return; }
                if (!OptimizationEngine.IsAvailable(o, s)) { if (want) plan.Skipped.Add(o.Name + ": " + Loc.T("prof.skip.na")); return; }
                if (OptimizationEngine.IsLocked(o, s)) { if (want) plan.Skipped.Add(o.Name + ": " + Loc.T("prof.skip.locked")); return; }
                if (OptimizationEngine.IsApplied(o) == want) { plan.UnchangedCount++; return; }
                plan.Changes.Add(new OptChange(o, want));
            }
            foreach (var kv in p.Optimizations) Add(kv.Key, kv.Value, false);
            foreach (var kv in p.Tweaks) Add(kv.Key, kv.Value, true);

            if (p.Apps != null)
                foreach (var a in p.Apps)
                {
                    if (a == null || !AppCatalog.IsValidId(a.Id) || (a.Variant != null && !AppCatalog.IsValidId(a.Variant)))
                    {
                        plan.Skipped.Add((a?.Name ?? a?.Id ?? "?") + ": " + Loc.T("prof.skip.badApp"));
                        continue;
                    }
                    if (installed != null && installed.ContainsKey(a.Variant ?? a.Id)) continue;
                    plan.AppsToInstall.Add(a);
                }
            return plan;
        }

        // ------------------------------------------------------------ Perfiles guardados

        public static string ProfilesDir
        {
            get
            {
                var d = Path.Combine(Path.GetDirectoryName(AppPaths.SettingsFile) ?? ".", "profiles");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        /// <summary>Nombre de archivo seguro (sin caracteres no válidos, como mucho 40).</summary>
        public static string SafeName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in (name ?? "").Trim())
                if (Array.IndexOf(Path.GetInvalidFileNameChars(), c) < 0) sb.Append(c);
            var r = sb.ToString().Trim().Trim('.');
            if (r.Length > 40) r = r.Substring(0, 40).Trim();
            return r;
        }

        public static string PathFor(string name) => Path.Combine(ProfilesDir, SafeName(name) + Extension);

        public static List<VeloxProfile> List()
        {
            var list = new List<VeloxProfile>();
            foreach (var f in Directory.GetFiles(ProfilesDir, "*" + Extension))
            {
                try { list.Add(Load(f)); }
                catch (Exception ex) { Log.Warn("Profile " + f + ": " + ex.Message); }
            }
            return list.OrderByDescending(p => p.Created).ToList();
        }

        public static void Delete(string name)
        {
            var f = PathFor(name);
            if (File.Exists(f)) File.Delete(f);
            Log.Info("Profile deleted: " + name + ".");
        }
    }
}
