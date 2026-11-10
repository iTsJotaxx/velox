using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;

namespace Velox.Localization
{
    /// <summary>
    /// Textos en español e inglés. Cada idioma tiene varios diccionarios XAML en Localization\{idioma}\
    /// (UI, Opts, Tools, Diag, Findings, Drivers, Disk, Tweaks, Mem). En XAML: {DynamicResource clave}; en código: Loc.T("clave").
    /// </summary>
    public static class Loc
    {
        public static readonly string[] Files = { "UI", "Opts", "Tools", "Diag", "Findings", "Drivers", "Disk", "Tweaks", "Mem" };
        public static readonly string[] Supported = { "es", "en" };

        static Dictionary<string, string> _strings = new Dictionary<string, string>();
        static ResourceDictionary _container;

        public static string Language { get; private set; } = "en";
        public static event Action LanguageChanged;

        public static string SystemLanguage() =>
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en";

        public static void SetLanguage(string lang)
        {
            if (Array.IndexOf(Supported, lang) < 0) lang = SystemLanguage();
            var app = Application.Current;
            if (_container == null)
            {
                _container = new ResourceDictionary();
                app.Resources.MergedDictionaries.Add(_container);
            }
            _container.MergedDictionaries.Clear();
            var strings = new Dictionary<string, string>();
            foreach (var f in Files)
            {
                try
                {
                    var d = (ResourceDictionary)Application.LoadComponent(new Uri("/Velox;component/Localization/" + lang + "/" + f + ".xaml", UriKind.Relative));
                    _container.MergedDictionaries.Add(d);
                    foreach (DictionaryEntry e in d)
                        if (e.Key is string k && e.Value is string v) strings[k] = v;
                }
                catch (Exception ex) { Core.Log.Warn("Language file " + lang + "/" + f + ": " + ex.Message); }
            }
            _strings = strings;
            Language = lang;
            // Números y fechas con el formato del idioma de la app (63.8 GB en inglés, 63,8 GB en español)
            var culture = CultureInfo.GetCultureInfo(lang == "es" ? "es-ES" : "en-US");
            System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            LanguageChanged?.Invoke();
        }

        /// <summary>Texto traducido; si falta devuelve la propia clave (así se ve qué falta).</summary>
        public static string T(string key) => key != null && _strings.TryGetValue(key, out var v) ? v : key;

        public static string F(string key, params object[] args)
        {
            try { return string.Format(T(key), args); }
            catch (FormatException) { return T(key); }
        }

        public static bool Has(string key) => key != null && _strings.ContainsKey(key);
    }
}
