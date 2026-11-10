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
        /// <summary>Añadida por el usuario (se guarda en %AppData%\Velox\apps.json y se puede quitar).</summary>
        public bool IsCustom { get; set; }
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
            new AppCategory { Id = "notes", Icon = "" },
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
            void A(string cat, string id, string name) => l.Add(new AppEntry { Id = id, Name = name, Category = cat });

            A("web", "Google.Chrome", "Google Chrome");
            A("web", "Mozilla.Firefox", "Firefox");
            A("web", "Brave.Brave", "Brave");
            A("web", "Opera.Opera", "Opera");
            A("web", "Opera.OperaGX", "Opera GX");
            A("web", "Vivaldi.Vivaldi", "Vivaldi");
            A("web", "LibreWolf.LibreWolf", "LibreWolf");

            A("ai", "Anthropic.Claude", "Claude");
            A("ai", "Anthropic.ClaudeCode", "Claude Code");
            A("ai", "OpenAI.ChatGPT", "ChatGPT");
            A("ai", "Perplexity.Comet", "Comet (Perplexity)");
            A("ai", "Ollama.Ollama", "Ollama");
            A("ai", "ElementLabs.LMStudio", "LM Studio");
            A("ai", "Jan.Jan", "Jan");
            A("ai", "nomic.gpt4all", "GPT4All");
            A("ai", "CloudStack.Msty", "Msty");
            A("ai", "Anysphere.Cursor", "Cursor");
            A("ai", "Codeium.Windsurf", "Windsurf");

            A("notes", "Obsidian.Obsidian", "Obsidian");
            A("notes", "Notion.Notion", "Notion");
            A("notes", "Logseq.Logseq", "Logseq");
            A("notes", "Joplin.Joplin", "Joplin");
            A("notes", "Anki.Anki", "Anki");

            A("chat", "Discord.Discord", "Discord");
            A("chat", "Telegram.TelegramDesktop", "Telegram");
            A("chat", "OpenWhisperSystems.Signal", "Signal");
            A("chat", "Zoom.Zoom", "Zoom");
            A("chat", "SlackTechnologies.Slack", "Slack");
            A("chat", "Microsoft.Teams", "Microsoft Teams");

            A("media", "VideoLAN.VLC", "VLC");
            A("media", "Spotify.Spotify", "Spotify");
            A("media", "clsid2.mpc-hc", "MPC-HC");
            A("media", "OBSProject.OBSStudio", "OBS Studio");
            A("media", "Audacity.Audacity", "Audacity");
            A("media", "HandBrake.HandBrake", "HandBrake");
            A("media", "PeterPawlowski.foobar2000", "foobar2000");
            A("media", "Stremio.Stremio", "Stremio");

            A("imaging", "GIMP.GIMP.3", "GIMP");
            A("imaging", "dotPDN.PaintDotNet", "Paint.NET");
            A("imaging", "KDE.Krita", "Krita");
            A("imaging", "Inkscape.Inkscape", "Inkscape");
            A("imaging", "BlenderFoundation.Blender", "Blender");
            A("imaging", "IrfanSkiljan.IrfanView", "IrfanView");
            A("imaging", "ShareX.ShareX", "ShareX");
            A("imaging", "Greenshot.Greenshot", "Greenshot");

            A("docs", "TheDocumentFoundation.LibreOffice", "LibreOffice");
            A("docs", "ONLYOFFICE.DesktopEditors", "ONLYOFFICE");
            A("docs", "Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat Reader");
            A("docs", "SumatraPDF.SumatraPDF", "SumatraPDF");
            A("docs", "Notepad++.Notepad++", "Notepad++");

            A("games", "Valve.Steam", "Steam");
            A("games", "EpicGames.EpicGamesLauncher", "Epic Games Launcher");
            A("games", "ElectronicArts.EADesktop", "EA app");
            A("games", "Ubisoft.Connect", "Ubisoft Connect");
            A("games", "GOG.Galaxy", "GOG Galaxy");
            A("games", "Blizzard.BattleNet", "Battle.net");
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
            A("utils", "AntibodySoftware.WizTree", "WizTree");
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

        /// <summary>Identificador con la forma de winget (Editor.Programa...): letras, números y . - _ + sin espacios.</summary>
        public static bool IsValidId(string id) =>
            !string.IsNullOrWhiteSpace(id) && id.Length <= 128 && id.IndexOf('.') > 0 &&
            id.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' || c == '+');
    }
}
