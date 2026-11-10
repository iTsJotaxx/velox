using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Velox.Localization;

namespace Velox.Core.Apps
{
    /// <summary>Aplicación instalable. Id = identificador exacto del repositorio oficial de winget.</summary>
    [DataContract(Name = "App", Namespace = "")]
    public sealed class AppEntry
    {
        [DataMember(Order = 1)] public string Id { get; set; }
        [DataMember(Order = 2)] public string Name { get; set; }
        [DataMember(Order = 3)] public string Category { get; set; }
        /// <summary>Origen en winget: null = repositorio «winget»; "msstore" = Microsoft Store (WhatsApp, Instagram...).</summary>
        [DataMember(Order = 4, EmitDefaultValue = false)] public string Source { get; set; }
        public string SourceName => string.IsNullOrEmpty(Source) ? "winget" : Source;
        /// <summary>
        /// Versiones por región (Riot): al marcarla hay que elegir una, y se instala el paquete de esa región.
        /// null = una sola versión (Id).
        /// </summary>
        public List<AppVariant> Variants { get; set; }
        /// <summary>Se instala con la ventana del propio instalador (para elegir carpeta, juego...), no en silencio.</summary>
        public bool Interactive { get; set; }
        /// <summary>Añadida por el usuario (se guarda en %AppData%\Velox\apps.json y se puede quitar).</summary>
        public bool IsCustom { get; set; }
    }

    public sealed class AppVariant
    {
        public string Id { get; set; }
        /// <summary>Código corto (EUW, NA...). El nombre visible es Loc "apps.region.{Code}".</summary>
        public string Code { get; set; }
        public string Name => Loc.T("apps.region." + Code);
        /// <summary>Código que usa Riot en el juego (LAN, LAS, OCE...).</summary>
        public string Short
        {
            get
            {
                switch (Code)
                {
                    case "LA1": return "LAN";
                    case "LA2": return "LAS";
                    case "OC1": return "OCE";
                    case "SG2": return "SG";
                    case "TW2": return "TW";
                    case "VN2": return "VN";
                    default: return Code;
                }
            }
        }
    }

    public sealed class AppCategory
    {
        public string Id { get; set; }
        public string Icon { get; set; }
        public string Name => Loc.T("apps.cat." + Id);
    }

    /// <summary>
    /// Catálogo de aplicaciones de Velox (estilo Ninite). Todos los identificadores están comprobados contra el
    /// repositorio oficial de winget («winget show --id X --exact»). Para añadir una aplicación al catálogo basta con
    /// una línea en Build(); el usuario puede añadir las suyas desde la propia app.
    /// </summary>
    public static class AppCatalog
    {
        public const string CustomCategory = "custom";

        public static readonly List<AppCategory> Categories = new List<AppCategory>
        {
            new AppCategory { Id = "web", Icon = "" },
            new AppCategory { Id = "ai", Icon = "" },
            new AppCategory { Id = "aitools", Icon = "" },
            new AppCategory { Id = "chat", Icon = "" },
            new AppCategory { Id = "media", Icon = "" },
            new AppCategory { Id = "imaging", Icon = "" },
            new AppCategory { Id = "docs", Icon = "" },
            new AppCategory { Id = "games", Icon = "" },
            new AppCategory { Id = "utils", Icon = "" },
            new AppCategory { Id = "security", Icon = "" },
            new AppCategory { Id = "dev", Icon = "" },
            new AppCategory { Id = "runtimes", Icon = "" },
            new AppCategory { Id = CustomCategory, Icon = "" },
        };

        public static readonly List<AppEntry> BuiltIn = Build();

        static List<AppEntry> Build()
        {
            var l = new List<AppEntry>();
            void A(string cat, string id, string name, string source = null) => l.Add(new AppEntry { Id = id, Name = name, Category = cat, Source = source });

            A("web", "Google.Chrome", "Google Chrome");
            A("web", "Mozilla.Firefox", "Firefox");
            A("web", "Brave.Brave", "Brave");
            A("web", "Opera.OperaGX", "Opera GX");
            A("web", "Vivaldi.Vivaldi", "Vivaldi");

            // Inteligencia artificial: los asistentes (apps de chat)
            A("ai", "Anthropic.Claude", "Claude");
            A("ai", "OpenAI.ChatGPT", "ChatGPT");
            A("ai", "Google.GeminiDesktop", "Gemini");
            A("ai", "GitHub.CopilotApp", "GitHub Copilot");
            A("ai", "Perplexity.Comet", "Comet (Perplexity)");
            A("ai", "ElementLabs.LMStudio", "LM Studio");
            A("ai", "Anysphere.Cursor", "Cursor");
            A("ai", "Codeium.Windsurf", "Windsurf");

            // Complementos de IA: herramientas que amplían lo que hace la IA
            A("aitools", "Obsidian.Obsidian", "Obsidian (vault de notas)");
            A("aitools", "Anthropic.ClaudeCode", "Claude Code (terminal)");
            A("aitools", "OpenAI.Codex", "Codex CLI (OpenAI)");
            A("aitools", "GitHub.Copilot", "GitHub Copilot CLI");
            A("aitools", "Ollama.Ollama", "Ollama (modelos locales)");
            A("aitools", "Comfy.ComfyUI-Desktop", "ComfyUI (imágenes)");
            A("aitools", "Upscayl.Upscayl", "Upscayl (mejorar imágenes)");
            A("aitools", "Docker.DockerDesktop", "Docker (servidores MCP)");


            A("chat", "Discord.Discord", "Discord");
            A("chat", "Telegram.TelegramDesktop", "Telegram");
            A("chat", "OpenWhisperSystems.Signal", "Signal");
            A("chat", "Zoom.Zoom", "Zoom");
            A("chat", "SlackTechnologies.Slack", "Slack");
            A("chat", "Microsoft.Teams", "Microsoft Teams");
            A("chat", "9NKSQGP7F2NH", "WhatsApp", "msstore");
            A("chat", "9NBLGGH5L9XT", "Instagram", "msstore");

            A("media", "VideoLAN.VLC", "VLC");
            A("media", "Spotify.Spotify", "Spotify");
            A("media", "OBSProject.OBSStudio", "OBS Studio");
            A("media", "Audacity.Audacity", "Audacity");
            A("media", "HandBrake.HandBrake", "HandBrake");
            A("media", "Stremio.Stremio", "Stremio");

            A("imaging", "GIMP.GIMP.3", "GIMP");
            A("imaging", "dotPDN.PaintDotNet", "Paint.NET");
            A("imaging", "KDE.Krita", "Krita");
            A("imaging", "Inkscape.Inkscape", "Inkscape");
            A("imaging", "BlenderFoundation.Blender", "Blender");
            A("imaging", "IrfanSkiljan.IrfanView", "IrfanView");
            A("imaging", "ShareX.ShareX", "ShareX");

            A("docs", "TheDocumentFoundation.LibreOffice", "LibreOffice");
            A("docs", "ONLYOFFICE.DesktopEditors", "ONLYOFFICE");
            A("docs", "Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat Reader");
            A("docs", "SumatraPDF.SumatraPDF", "SumatraPDF");
            A("docs", "Notepad++.Notepad++", "Notepad++");
            A("docs", "Notion.Notion", "Notion");
            A("docs", "Anki.Anki", "Anki");

            A("games", "Valve.Steam", "Steam");
            A("games", "EpicGames.EpicGamesLauncher", "Epic Games Launcher");
            A("games", "ElectronicArts.EADesktop", "EA app");
            A("games", "Ubisoft.Connect", "Ubisoft Connect");
            A("games", "GOG.Galaxy", "GOG Galaxy");
            A("games", "Blizzard.BattleNet", "Battle.net");
            // Riot publica un instalador por juego y región (cada uno incluye el Riot Client). Al marcarlos se pide confirmar
            // la región, preseleccionada según el país de Windows (RegionDetect).
            l.Add(new AppEntry
            {
                Id = "RiotGames.LeagueOfLegends", Name = "League of Legends", Category = "games",
                Variants = new[] { "EUW", "EUNE", "NA", "LA1", "LA2", "BR", "TR", "RU", "OC1", "JP", "KR", "SG2", "TW2", "VN2" }
                    .Select(c => new AppVariant { Code = c, Id = "RiotGames.LeagueOfLegends." + c }).ToList(),
            });
            l.Add(new AppEntry
            {
                Id = "RiotGames.Valorant", Name = "VALORANT", Category = "games",
                Variants = new[] { "EU", "NA", "LATAM", "BR", "KR", "AP" }
                    .Select(c => new AppVariant { Code = c, Id = "RiotGames.Valorant." + c }).ToList(),
            });
            A("games", "Guru3D.Afterburner", "MSI Afterburner");

            A("utils", "7zip.7zip", "7-Zip");
            A("utils", "RARLab.WinRAR", "WinRAR");
            A("utils", "voidtools.Everything", "Everything");
            A("utils", "Microsoft.PowerToys", "PowerToys");
            A("utils", "CPUID.CPU-Z", "CPU-Z");
            A("utils", "TechPowerUp.GPU-Z", "GPU-Z");
            A("utils", "REALiX.HWiNFO", "HWiNFO");
            A("utils", "CrystalDewWorld.CrystalDiskInfo", "CrystalDiskInfo");
            A("utils", "CrystalDewWorld.CrystalDiskMark", "CrystalDiskMark");
            A("utils", "Rufus.Rufus", "Rufus");
            A("utils", "qBittorrent.qBittorrent", "qBittorrent");
            A("utils", "AnyDesk.AnyDesk", "AnyDesk");
            A("utils", "TeamViewer.TeamViewer", "TeamViewer");

            A("security", "Malwarebytes.Malwarebytes", "Malwarebytes");
            A("security", "Bitwarden.Bitwarden", "Bitwarden");
            A("security", "KeePassXCTeam.KeePassXC", "KeePassXC");
            A("security", "Proton.ProtonVPN", "Proton VPN");

            A("dev", "Microsoft.VisualStudioCode", "Visual Studio Code");
            A("dev", "Git.Git", "Git");
            A("dev", "Python.Python.3.13", "Python 3.13");
            A("dev", "OpenJS.NodeJS.LTS", "Node.js LTS");
            A("dev", "Microsoft.WindowsTerminal", "Windows Terminal");

            A("runtimes", "Microsoft.VCRedist.2015+.x64", "Visual C++ 2015-2022 (x64)");
            A("runtimes", "Microsoft.VCRedist.2015+.x86", "Visual C++ 2015-2022 (x86)");
            A("runtimes", "Microsoft.DotNet.DesktopRuntime.8", ".NET Desktop Runtime 8");
            A("runtimes", "Microsoft.DirectX", "DirectX (runtime)");
            A("runtimes", "EclipseAdoptium.Temurin.21.JRE", "Java 21 (Temurin JRE)");
            return l;
        }

        // ------------------------------------------------------------ Aplicaciones añadidas por el usuario

        static string CustomFile => Path.Combine(Path.GetDirectoryName(AppPaths.SettingsFile) ?? ".", "apps.json");

        public static List<AppEntry> LoadCustom()
        {
            try
            {
                if (!File.Exists(CustomFile)) return new List<AppEntry>();
                using (var fs = File.OpenRead(CustomFile))
                {
                    var list = (List<AppEntry>)new DataContractJsonSerializer(typeof(List<AppEntry>)).ReadObject(fs) ?? new List<AppEntry>();
                    foreach (var a in list) { a.IsCustom = true; a.Category = CustomCategory; }
                    return list.Where(a => IsValidId(a.Id) && !string.IsNullOrWhiteSpace(a.Name)).ToList();
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Custom apps could not be read: " + ex.Message);
                return new List<AppEntry>();
            }
        }

        public static void SaveCustom(IEnumerable<AppEntry> apps)
        {
            try
            {
                var tmp = CustomFile + ".tmp";
                using (var fs = File.Create(tmp))
                    new DataContractJsonSerializer(typeof(List<AppEntry>)).WriteObject(fs, apps.Select(a => new AppEntry { Id = a.Id, Name = a.Name, Category = CustomCategory }).ToList());
                if (File.Exists(CustomFile)) File.Delete(CustomFile);
                File.Move(tmp, CustomFile);
            }
            catch (Exception ex) { Log.Warn("Custom apps could not be saved: " + ex.Message); }
        }

        /// <summary>
        /// Identificador de winget (Editor.Programa...: letras, números y . - _ + sin espacios) o de Microsoft Store
        /// (12 letras mayúsculas y números, p. ej. 9NKSQGP7F2NH).
        /// </summary>
        public static bool IsValidId(string id) =>
            !string.IsNullOrWhiteSpace(id) && id.Length <= 128 &&
            ((id.IndexOf('.') > 0 && id.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' || c == '+'))
             || System.Text.RegularExpressions.Regex.IsMatch(id, "^[0-9A-Z]{12}$"));
    }
}
