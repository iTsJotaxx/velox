using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Velox.Core.Apps
{
    /// <summary>
    /// Región recomendada para los juegos de Riot según el país configurado en Windows
    /// (Configuración > Hora e idioma > Región > País o región).
    /// </summary>
    public static class RegionDetect
    {
        [DllImport("kernel32.dll")] static extern int GetUserGeoID(int geoClass);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetGeoInfoW(int location, int geoType, StringBuilder data, int size, int langId);

        const int GEOCLASS_NATION = 16, GEO_ISO2 = 4;

        /// <summary>Código ISO del país de Windows (ES, MX, US...), o null si no se sabe.</summary>
        public static string CountryIso2()
        {
            try
            {
                int id = GetUserGeoID(GEOCLASS_NATION);
                if (id > 0)
                {
                    var sb = new StringBuilder(10);
                    if (GetGeoInfoW(id, GEO_ISO2, sb, sb.Capacity, 0) > 0 && sb.Length == 2) return sb.ToString().ToUpperInvariant();
                }
            }
            catch { }
            try { return RegionInfo.CurrentRegion.TwoLetterISORegionName.ToUpperInvariant(); } catch { return null; }
        }

        /// <summary>Nombre del país en el idioma de la app (para explicar la recomendación).</summary>
        public static string CountryName(string iso2)
        {
            try { return new RegionInfo(iso2).DisplayName; } catch { return iso2; }
        }

        // ------------------------------------------------------------ League of Legends (servidores)

        static readonly Dictionary<string, string> Lol = Build(new Dictionary<string, string>
        {
            ["EUW"] = "ES PT FR DE IT GB IE NL BE LU CH AT MT MC AD SM VA LI GI",
            ["EUNE"] = "SE NO FI DK IS EE LV LT PL CZ SK HU RO BG GR HR SI RS BA MK AL ME CY MD UA XK",
            ["NA"] = "US CA",
            ["LA1"] = "MX GT HN SV NI CR PA CU DO PR HT JM CO VE EC PE BZ TT BS BB",
            ["LA2"] = "AR CL UY PY BO",
            ["BR"] = "BR",
            ["TR"] = "TR AZ GE",
            ["RU"] = "RU BY KZ AM KG UZ TJ TM",
            ["OC1"] = "AU NZ FJ PG",
            ["JP"] = "JP",
            ["KR"] = "KR",
            ["SG2"] = "SG MY ID PH TH",
            ["TW2"] = "TW HK MO",
            ["VN2"] = "VN",
        });

        // ------------------------------------------------------------ VALORANT (regiones)

        static readonly Dictionary<string, string> Valorant = Build(new Dictionary<string, string>
        {
            ["EU"] = "ES PT FR DE IT GB IE NL BE LU CH AT MT MC AD SM VA LI GI SE NO FI DK IS EE LV LT PL CZ SK HU RO BG GR HR SI RS BA MK AL ME CY MD UA XK " +
                     "TR AZ GE RU BY KZ AM KG UZ TJ TM AE SA QA KW BH OM IL JO LB EG MA DZ TN ZA",
            ["NA"] = "US CA",
            ["LATAM"] = "MX GT HN SV NI CR PA CU DO PR HT JM CO VE EC PE BZ TT BS BB AR CL UY PY BO",
            ["BR"] = "BR",
            ["KR"] = "KR",
            ["AP"] = "JP AU NZ SG MY ID PH TH VN TW HK MO IN PK BD LK",
        });

        static Dictionary<string, string> Build(Dictionary<string, string> byRegion)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in byRegion)
                foreach (var c in kv.Value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    if (!map.ContainsKey(c)) map[c] = kv.Key;
            return map;
        }

        /// <summary>Código de región recomendado (de app.Variants) para ese país, o null si no hay recomendación.</summary>
        public static string Suggest(string appId, string iso2)
        {
            if (string.IsNullOrEmpty(iso2)) return null;
            Dictionary<string, string> map =
                appId.StartsWith("RiotGames.LeagueOfLegends", StringComparison.OrdinalIgnoreCase) ? Lol :
                appId.StartsWith("RiotGames.Valorant", StringComparison.OrdinalIgnoreCase) ? Valorant : null;
            return map != null && map.TryGetValue(iso2, out var r) ? r : null;
        }
    }
}
