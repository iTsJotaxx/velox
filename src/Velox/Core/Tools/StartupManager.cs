using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using Velox.Localization;

namespace Velox.Core.Tools
{
    /// <summary>
    /// Programas de inicio. Se activan y desactivan igual que lo hace el Administrador de tareas
    /// (claves StartupApproved), así que el cambio es reversible.
    /// </summary>
    public static class StartupManager
    {
        const string ApprovedBase = @"\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        sealed class RunSource
        {
            public string Run, Approved, LabelKey;
        }

        static readonly RunSource[] RunSources =
        {
            new RunSource { Run = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", Approved = "HKCU" + ApprovedBase + "Run", LabelKey = "startup.source.user" },
            new RunSource { Run = @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run", Approved = "HKLM" + ApprovedBase + "Run", LabelKey = "startup.source.all" },
            new RunSource { Run = @"HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", Approved = "HKLM" + ApprovedBase + "Run32", LabelKey = "startup.source.all32" },
        };

        public static List<StartupEntry> GetAll()
        {
            var items = new List<StartupEntry>();

            foreach (var src in RunSources)
            {
                try
                {
                    using (var key = Reg.OpenKey(src.Run, false))
                    {
                        if (key == null) continue;
                        foreach (var n in key.GetValueNames())
                        {
                            if (string.IsNullOrEmpty(n)) continue;
                            items.Add(NewEntry(n, Convert.ToString(key.GetValue(n)), Loc.T(src.LabelKey), src.Approved, n));
                        }
                    }
                }
                catch (Exception ex) { Log.Warn("Startup (" + src.Run + "): " + ex.Message); }
            }

            var folders = new[]
            {
                new { Path = Environment.GetFolderPath(Environment.SpecialFolder.Startup), Approved = "HKCU" + ApprovedBase + "StartupFolder", LabelKey = "startup.source.folderUser" },
                new { Path = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Approved = "HKLM" + ApprovedBase + "StartupFolder", LabelKey = "startup.source.folderAll" },
            };
            foreach (var f in folders)
            {
                try
                {
                    if (string.IsNullOrEmpty(f.Path) || !Directory.Exists(f.Path)) continue;
                    foreach (var file in new DirectoryInfo(f.Path).GetFiles())
                    {
                        if (string.Equals(file.Name, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                        items.Add(NewEntry(Path.GetFileNameWithoutExtension(file.Name), file.FullName, Loc.T(f.LabelKey), f.Approved, file.Name));
                    }
                }
                catch (Exception ex) { Log.Warn("Startup (" + f.Path + "): " + ex.Message); }
            }

            return items.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>
        /// Nombre legible, como el Administrador de tareas: la descripción del ejecutable («Microsoft Edge» en vez de
        /// «MicrosoftEdgeAutoLaunch_D74866CC...»). Si no se puede leer, el nombre de la entrada.
        /// </summary>
        internal static string FriendlyName(string name, string command)
        {
            try
            {
                string exe = ExePath(command);
                if (exe != null && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(exe))
                {
                    // Lanzadores genéricos: su descripción no dice nada del programa que abren
                    string file = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
                    if (Array.IndexOf(new[] { "cmd", "powershell", "pwsh", "rundll32", "wscript", "cscript", "mshta", "msiexec", "explorer", "regsvr32", "conhost", "schtasks" }, file) >= 0)
                        return name;
                    string d = (System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileDescription ?? "").Trim();
                    if (d.Length > 1) return d;
                }
            }
            catch { }
            return name;
        }

        /// <summary>Ruta del ejecutable de una línea de comandos («"C:\a b\x.exe" -arg» o «C:\a b\x.exe -arg»).</summary>
        internal static string ExePath(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            string c = Environment.ExpandEnvironmentVariables(command.Trim());
            if (c.StartsWith("\""))
            {
                int end = c.IndexOf('"', 1);
                return end > 1 ? c.Substring(1, end - 1) : null;
            }
            int i = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return i > 0 ? c.Substring(0, i + 4) : c;
        }

        static StartupEntry NewEntry(string name, string command, string source, string approved, string valueName)
        {
            return new StartupEntry
            {
                Name = FriendlyName(name, command),
                Command = command,
                Source = source,
                ApprovedPath = approved,
                ValueName = valueName,
                Enabled = IsApproved(approved, valueName),
            };
        }

        // Sin entrada en StartupApproved el programa está activado; si la hay, activado = primer byte par
        static bool IsApproved(string approvedPath, string name)
        {
            var st = Reg.Get(approvedPath, name);
            if (!st.Existed) return true;
            var b = st.Value as byte[];
            if (b != null && b.Length > 0) return (b[0] & 1) == 0;
            return true;
        }

        public static void SetEnabled(StartupEntry entry, bool enabled)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            var bytes = new byte[12];
            if (enabled) bytes[0] = 2;
            else
            {
                bytes[0] = 3;
                Array.Copy(BitConverter.GetBytes(DateTime.Now.ToFileTime()), 0, bytes, 4, 8);
            }
            Reg.Set(entry.ApprovedPath, entry.ValueName, bytes, RegistryValueKind.Binary);
            entry.Enabled = enabled;
            Log.Info("Windows startup: \"" + entry.Name + "\" " + (enabled ? "enabled" : "disabled") + ".");
        }
    }
}
