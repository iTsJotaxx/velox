using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using Velox.Core.Sys;
using Velox.Core.Tools;
using Velox.Localization;
using Velox.Native;

namespace Velox.Core.Scan
{
    /// <summary>Escanea el equipo con WMI, el registro y llamadas nativas. Se llama desde un hilo de fondo.</summary>
    public static class SystemScanner
    {
        const double GB = 1024.0 * 1024.0 * 1024.0;
        const string VideoClassKey = @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        const string StorageScope = @"root\Microsoft\Windows\Storage";

        static readonly Regex VirtualGpuRx = new Regex("Microsoft Basic|Remote Display|Virtual|Parsec|Meta Virtual|Citrix|DisplayLink", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex DedicatedRx = new Regex(@"NVIDIA|Radeon RX|Radeon\(TM\) RX|Radeon Pro|Arc\(TM\)|Intel\(R\) Arc|\bArc A\d|\bArc B\d", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex ModernRx = new Regex(@"RTX|GTX 1[0-9]|GTX 16|RX [5-9]\d{3}|Arc", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <param name="progress">Recibe el texto ya traducido del paso actual (puede ser null).</param>
        public static SystemInfo Scan(IProgress<string> progress)
        {
            var s = new SystemInfo();

            Step(progress, "scan.step.os");
            Safe("OS", () => ReadOs(s));
            Safe("RAM modules", () => ReadMemoryModules(s));

            Step(progress, "scan.step.cpu");
            Safe("CPU", () => ReadCpu(s));

            Step(progress, "scan.step.gpu");
            Safe("GPU", () => ReadGpus(s));
            Safe("Displays", () => s.Displays = Display.GetDisplays() ?? new List<DisplayInfo>());

            Step(progress, "scan.step.disk");
            Safe("Chassis", () => ReadChassis(s));
            Safe("Disks", () => ReadDisks(s));
            Safe("System drive", () => ReadSystemDriveSpace(s));

            Step(progress, "scan.step.power");
            Safe("Power plan", () => s.PowerScheme = PowerCfg.ActiveSchemeName());
            Safe("Power overlay", () => s.Overlay = PowerOverlay.Get());
            Safe("HVCI", () => s.Hvci = Reg.GetInt(@"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled") == 1);
            // Contadores de rendimiento desactivados por otros optimizadores
            Safe("Perf counters", () => s.PerfCountersOff = Reg.GetInt(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Perflib", "Disable Performance Counters") == 1);
            Safe("Edge", () => s.EdgeInstalled = IsEdgeInstalled());
            Safe("Other account", () => ReadOtherAccount(s));
            Safe("Hibernation", () => s.HibernationEnabled = PowerCfg.HibernationEnabled());
            Safe("Widgets lock", () => s.WidgetsLocked = ProbeWidgetsLocked());

            Step(progress, "scan.step.procs");
            Safe("Processes", () => ReadProcesses(s));
            Safe("Startup", () => s.StartupEnabled = StartupManager.GetAll().Count(e => e.Enabled));

            s.ScanTime = DateTime.Now;
            return s;
        }

        /// <summary>
        /// Comprueba si Windows bloquea el valor TaskbarDa escribiendo su mismo valor actual (no cambia nada).
        /// La protección UCPD de Windows 11 lo impide a cualquier programa que no sea de Windows.
        /// </summary>
        static bool ProbeWidgetsLocked()
        {
            const string path = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
            var st = Reg.Get(path, "TaskbarDa");
            if (!st.Existed || st.Value == null) return false;
            try { Reg.Set(path, "TaskbarDa", st.Value, st.Kind); return false; }
            catch { return true; }
        }

        static void Step(IProgress<string> progress, string key)
        {
            if (progress == null) return;
            try { progress.Report(Loc.T(key)); } catch { }
        }

        // Un fallo en un bloque no puede romper el escaneo entero
        static void Safe(string what, Action a)
        {
            try { a(); }
            catch (Exception ex) { Log.Warn("Scan (" + what + ") failed: " + ex.Message); }
        }

        // ------------------------------------------------------------ WMI

        static void Each(string scope, string wql, Action<ManagementBaseObject> action)
        {
            var opts = new EnumerationOptions { ReturnImmediately = false, Rewindable = false, Timeout = TimeSpan.FromSeconds(30) };
            using (var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql), opts))
            using (var results = searcher.Get())
            {
                foreach (ManagementBaseObject o in results)
                {
                    using (o) action(o);
                }
            }
        }

        static List<T> Select<T>(string scope, string wql, Func<ManagementBaseObject, T> map)
        {
            var list = new List<T>();
            Each(scope, wql, o => list.Add(map(o)));
            return list;
        }

        static object Prop(ManagementBaseObject o, string name)
        {
            try { return o[name]; } catch { return null; }
        }

        static string Str(ManagementBaseObject o, string name) => Convert.ToString(Prop(o, name)) ?? "";

        static double Num(ManagementBaseObject o, string name)
        {
            var v = Prop(o, name);
            if (v == null) return 0;
            try { return Convert.ToDouble(v); } catch { return 0; }
        }

        static int Int(ManagementBaseObject o, string name) => (int)Math.Round(Num(o, name));

        // ------------------------------------------------------------ Bloques

        static void ReadOs(SystemInfo s)
        {
            Each(@"root\cimv2", "SELECT Caption, Version, BuildNumber, LastBootUpTime, TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem", os =>
            {
                s.OS = Str(os, "Caption").Trim();
                s.Version = Str(os, "Version");
                int build; int.TryParse(Str(os, "BuildNumber"), out build); s.Build = build;

                string boot = Str(os, "LastBootUpTime");
                if (boot.Length > 0)
                {
                    var t = ManagementDateTimeConverter.ToDateTime(boot);
                    s.UptimeDays = Math.Round((DateTime.Now - t).TotalDays, 1);
                }

                // WMI da la memoria en KB
                double total = Num(os, "TotalVisibleMemorySize"), free = Num(os, "FreePhysicalMemory");
                s.RamTotalGB = Math.Round(total / (1024.0 * 1024.0), 1);
                s.RamFreeGB = Math.Round(free / (1024.0 * 1024.0), 1);
                if (total > 0) s.RamUsedPct = (int)Math.Round(100 - free / total * 100);
            });
        }

        static void ReadMemoryModules(SystemInfo s)
        {
            var mods = Select(@"root\cimv2", "SELECT ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory",
                o => new { Speed = Int(o, "ConfiguredClockSpeed"), Type = Int(o, "SMBIOSMemoryType") });
            s.RamModules = mods.Count;
            s.RamSpeed = mods.Count > 0 ? mods.Max(m => m.Speed) : 0;
            s.RamType = "";
            if (mods.Count > 0)
            {
                switch (mods[0].Type)
                {
                    case 24: s.RamType = "DDR3"; break;
                    case 26: s.RamType = "DDR4"; break;
                    case 34: s.RamType = "DDR5"; break;
                    case 35: s.RamType = "LPDDR5"; break;
                    case 30: s.RamType = "LPDDR4"; break;
                }
            }
        }

        static void ReadCpu(SystemInfo s)
        {
            Each(@"root\cimv2", "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor", cpu =>
            {
                if (s.Cpu != null) return; // solo el primer procesador
                s.Cpu = Regex.Replace(Str(cpu, "Name").Trim(), @"\s+", " ");
                s.Cores = Int(cpu, "NumberOfCores");
                s.Threads = Int(cpu, "NumberOfLogicalProcessors");
            });
        }

        /// <summary>Convierte el valor del registro (QWORD, DWORD, byte[]...) a bytes.</summary>
        static double ToByteCount(object v)
        {
            if (v == null) return 0;
            var b = v as byte[];
            if (b != null)
            {
                if (b.Length >= 8) return BitConverter.ToUInt64(b, 0);
                if (b.Length >= 4) return BitConverter.ToUInt32(b, 0);
                return 0;
            }
            if (v is int) return unchecked((uint)(int)v);
            if (v is long) return Math.Max(0, (long)v);
            try { return Convert.ToDouble(v); } catch { return 0; }
        }

        static void ReadGpus(SystemInfo s)
        {
            // La VRAM real está en el registro del controlador (Win32_VideoController la limita a 4 GB)
            var vram = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var root = Reg.OpenKey(VideoClassKey, false))
                {
                    if (root != null)
                    {
                        foreach (var sub in root.GetSubKeyNames())
                        {
                            if (!Regex.IsMatch(sub, @"^\d{4}$")) continue;
                            try
                            {
                                using (var k = root.OpenSubKey(sub, false))
                                {
                                    if (k == null) continue;
                                    string desc = Convert.ToString(k.GetValue("DriverDesc"));
                                    object q = k.GetValue("HardwareInformation.qwMemorySize") ?? k.GetValue("HardwareInformation.MemorySize");
                                    double bytes = ToByteCount(q);
                                    double old;
                                    if (!string.IsNullOrEmpty(desc) && bytes > 0 && (!vram.TryGetValue(desc, out old) || old < bytes))
                                        vram[desc] = bytes;
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warn("Scan (VRAM registry) failed: " + ex.Message); }

            var list = new List<GpuInfo>();
            Each(@"root\cimv2", "SELECT Name, AdapterRAM, DriverVersion, DriverDate FROM Win32_VideoController", vc =>
            {
                string name = Str(vc, "Name");
                if (name.Length == 0 || VirtualGpuRx.IsMatch(name)) return;
                double bytes;
                if (!vram.TryGetValue(name, out bytes)) bytes = Math.Abs(Num(vc, "AdapterRAM"));

                DateTime? date = null;
                string d = Str(vc, "DriverDate");
                if (d.Length > 0) { try { date = ManagementDateTimeConverter.ToDateTime(d); } catch { } }

                list.Add(new GpuInfo
                {
                    Name = name,
                    VramGB = Math.Round(bytes / GB, 1),
                    Driver = Str(vc, "DriverVersion"),
                    DriverDate = date,
                    Dedicated = DedicatedRx.IsMatch(name),
                    Modern = ModernRx.IsMatch(name),
                });
            });

            s.Gpus = list;
            s.HasDedicatedGpu = list.Any(g => g.Dedicated);
            s.ModernGpu = list.Any(g => g.Modern);
            s.VramGB = list.Count > 0 ? list.Max(g => g.VramGB) : 0;
        }

        static void ReadChassis(SystemInfo s)
        {
            var laptopTypes = new[] { 8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32 };
            var desktopTypes = new[] { 3, 4, 5, 6, 7, 13, 15, 16, 35, 36 };

            var chassis = new List<int>();
            try
            {
                Each(@"root\cimv2", "SELECT ChassisTypes FROM Win32_SystemEnclosure", e =>
                {
                    var arr = Prop(e, "ChassisTypes") as Array;
                    if (arr != null) foreach (var x in arr) chassis.Add(Convert.ToInt32(x));
                });
            }
            catch (Exception ex) { Log.Warn("Scan (chassis) failed: " + ex.Message); }

            var batteryStatus = new List<int>();
            try { Each(@"root\cimv2", "SELECT BatteryStatus FROM Win32_Battery", b => batteryStatus.Add(Int(b, "BatteryStatus"))); }
            catch (Exception ex) { Log.Warn("Scan (battery) failed: " + ex.Message); }

            bool laptopChassis = chassis.Any(c => laptopTypes.Contains(c));
            bool desktopChassis = chassis.Any(c => desktopTypes.Contains(c));
            // Un SAI conectado por USB aparece como batería: solo cuenta si el chasis no es de sobremesa
            s.IsLaptop = laptopChassis || (batteryStatus.Count > 0 && !desktopChassis);
            s.OnBattery = batteryStatus.Any(b => b == 1);
        }

        static string BusName(int bus)
        {
            switch (bus)
            {
                case 1: return "SCSI";
                case 2: return "ATAPI";
                case 3: return "ATA";
                case 4: return "1394";
                case 6: return "Fibre Channel";
                case 7: return "USB";
                case 8: return "RAID";
                case 9: return "iSCSI";
                case 10: return "SAS";
                case 11: return "SATA";
                case 12: return "SD";
                case 13: return "MMC";
                case 16: return "Storage Spaces";
                case 17: return "NVMe";
                default: return "";
            }
        }

        static string HealthName(int h)
        {
            switch (h)
            {
                case 0: return "Healthy";
                case 1: return "Warning";
                case 2: return "Unhealthy";
                default: return "Unknown";
            }
        }

        static char SystemDriveLetter()
        {
            string sd = Environment.GetEnvironmentVariable("SystemDrive");
            if (string.IsNullOrEmpty(sd)) sd = Path.GetPathRoot(Environment.SystemDirectory);
            return char.ToUpperInvariant(sd[0]);
        }

        static void ReadDisks(SystemInfo s)
        {
            // Número del disco físico donde está la partición del sistema
            int sysDisk = -1;
            try
            {
                char letter = SystemDriveLetter();
                Each(StorageScope, "SELECT DriveLetter, DiskNumber FROM MSFT_Partition", p =>
                {
                    if (sysDisk >= 0) return;
                    var dl = Prop(p, "DriveLetter");
                    char c = '\0';
                    if (dl is char) c = (char)dl;
                    else if (dl is ushort) c = (char)(ushort)dl;
                    if (c != '\0' && char.ToUpperInvariant(c) == letter) sysDisk = Int(p, "DiskNumber");
                });
            }
            catch (Exception ex) { Log.Warn("Scan (partition) failed: " + ex.Message); }

            var disks = new List<DiskInfo>();
            DiskInfo sysInfo = null;
            Each(StorageScope, "SELECT DeviceId, FriendlyName, MediaType, BusType, Size, HealthStatus FROM MSFT_PhysicalDisk", pd =>
            {
                int media = Int(pd, "MediaType"), bus = Int(pd, "BusType");
                string type = "Unknown";
                if (media == 4 || bus == 17) type = "SSD";   // NVMe cuenta como SSD
                else if (media == 3) type = "HDD";

                int id;
                bool isSystem = int.TryParse(Str(pd, "DeviceId"), out id) && id == sysDisk;
                var di = new DiskInfo
                {
                    Name = Str(pd, "FriendlyName").Trim(),
                    MediaType = type,
                    Bus = BusName(bus),
                    SizeGB = Math.Round(Num(pd, "Size") / GB, 1),
                    Health = HealthName(Int(pd, "HealthStatus")),
                    IsSystem = isSystem,
                };
                disks.Add(di);
                if (isSystem && sysInfo == null) sysInfo = di;
            });

            s.Disks = disks;
            if (sysInfo != null)
            {
                s.DiskType = sysInfo.MediaType;
                s.DiskModel = sysInfo.Name;
                s.DiskBus = sysInfo.Bus;
            }
        }

        static void ReadSystemDriveSpace(SystemInfo s)
        {
            string drive = SystemDriveLetter() + ":";
            Each(@"root\cimv2", "SELECT Size, FreeSpace FROM Win32_LogicalDisk WHERE DeviceID='" + drive + "'", ld =>
            {
                double size = Num(ld, "Size"), free = Num(ld, "FreeSpace");
                s.DiskSizeGB = Math.Round(size / GB);
                s.DiskFreeGB = Math.Round(free / GB);
                if (size > 0) s.DiskFreePct = (int)Math.Round(free / size * 100);
            });
        }

        static bool IsEdgeInstalled()
        {
            const string rel = @"Microsoft\Edge\Application\msedge.exe";
            string x86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            string pf = Environment.GetEnvironmentVariable("ProgramFiles");
            return (!string.IsNullOrEmpty(x86) && File.Exists(Path.Combine(x86, rel)))
                || (!string.IsNullOrEmpty(pf) && File.Exists(Path.Combine(pf, rel)));
        }

        /// <summary>¿Se abrió con otra cuenta de administrador? Entonces HKCU no es el del usuario que tiene la sesión.</summary>
        static void ReadOtherAccount(SystemInfo s)
        {
            s.OtherAccount = false; s.ShellUser = "";
            int session = Process.GetCurrentProcess().SessionId;
            string me = Environment.UserName;
            bool done = false;
            Each(@"root\cimv2", "SELECT * FROM Win32_Process WHERE Name='explorer.exe' AND SessionId=" + session, ex =>
            {
                if (done) return;
                done = true;
                var mo = (ManagementObject)ex;
                using (var o = mo.InvokeMethod("GetOwner", null, null))
                {
                    string user = o == null ? null : Convert.ToString(o["User"]);
                    string domain = o == null ? null : Convert.ToString(o["Domain"]);
                    if (!string.IsNullOrEmpty(user) && !string.Equals(user, me, StringComparison.OrdinalIgnoreCase))
                    {
                        s.OtherAccount = true;
                        s.ShellUser = domain + "\\" + user;
                    }
                }
            });
        }

        static void ReadProcesses(SystemInfo s)
        {
            var procs = Process.GetProcesses();
            try
            {
                s.ProcCount = procs.Length;
                var groups = new Dictionary<string, TopApp>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in procs)
                {
                    try
                    {
                        if (p.Id == 0) continue;
                        TopApp g;
                        if (!groups.TryGetValue(p.ProcessName, out g)) groups[p.ProcessName] = g = new TopApp { Name = p.ProcessName };
                        g.Count++;
                        g.MB += p.WorkingSet64 / (1024.0 * 1024.0);
                    }
                    catch { }
                }
                s.TopApps = groups.Values.OrderByDescending(g => g.MB).Take(6)
                    .Select(g => new TopApp { Name = g.Name, Count = g.Count, MB = Math.Round(g.MB) }).ToList();
            }
            finally { foreach (var p in procs) p.Dispose(); }
        }
    }
}
