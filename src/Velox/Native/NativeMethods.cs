// Llamadas nativas de Windows (portadas del prototipo en PowerShell)
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Velox.Native
{
    // ---------------------------------------------------------------- SystemParametersInfo
    public static class Spi
    {
        // Solo SPIF_UPDATEINIFILE: el cambio es inmediato. El aviso WM_SETTINGCHANGE se envía aparte en otro
        // hilo, porque con SPIF_SENDCHANGE Windows espera a que respondan todas las ventanas (1-2 s con muchas apps).
        const uint FLAGS = 0x01;

        static void Notify(uint action)
        {
            Thread t = new Thread(delegate ()
            {
                UIntPtr r;
                SendMessageTimeoutPtr(new IntPtr(0xFFFF), 0x001A, new UIntPtr(action), IntPtr.Zero, 0x0002, 3000, out r);
            });
            t.IsBackground = true;
            t.Start();
        }

        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
        static extern IntPtr SendMessageTimeoutPtr(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);

        [StructLayout(LayoutKind.Sequential)]
        struct ANIMATIONINFO { public uint cbSize; public int iMinAnimate; }
        [StructLayout(LayoutKind.Sequential)]
        struct FLAGSONLY { public uint cbSize; public uint dwFlags; } // STICKYKEYS y TOGGLEKEYS
        [StructLayout(LayoutKind.Sequential)]
        struct FILTERKEYS { public uint cbSize; public uint dwFlags; public uint iWaitMSec; public uint iDelayMSec; public uint iRepeatMSec; public uint iBounceMSec; }

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        static extern bool SpiPtr(uint action, uint uiParam, IntPtr pvParam, uint winIni);
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        static extern bool SpiInts(uint action, uint uiParam, int[] pvParam, uint winIni);
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        static extern bool SpiRefInt(uint action, uint uiParam, ref int pvParam, uint winIni);
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        static extern bool SpiAnim(uint action, uint uiParam, ref ANIMATIONINFO pvParam, uint winIni);
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        static extern bool SpiFlags(uint action, uint uiParam, ref FLAGSONLY pvParam, uint winIni);
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        static extern bool SpiFilter(uint action, uint uiParam, ref FILTERKEYS pvParam, uint winIni);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);

        // Umbrales y aceleración del ratón (MouseThreshold1, MouseThreshold2, MouseSpeed)
        public static bool SetMouse(int threshold1, int threshold2, int speed)
        {
            bool ok = SpiInts(0x0004, 0, new int[] { threshold1, threshold2, speed }, FLAGS);
            if (ok) Notify(0x0004);
            return ok;
        }

        // delay 0..3 (250 ms..1 s), speed 0..31
        public static bool SetKeyboard(uint delay, uint speed)
        {
            bool a = SpiPtr(0x0017, delay, IntPtr.Zero, FLAGS);
            bool b = SpiPtr(0x000B, speed, IntPtr.Zero, FLAGS);
            if (a || b) Notify(0x000B);
            return a && b;
        }

        public static bool SetMinAnimate(bool on)
        {
            ANIMATIONINFO ai = new ANIMATIONINFO();
            ai.cbSize = (uint)Marshal.SizeOf(typeof(ANIMATIONINFO));
            ai.iMinAnimate = on ? 1 : 0;
            bool ok = SpiAnim(0x0049, ai.cbSize, ref ai, FLAGS);
            if (ok) Notify(0x0049);
            return ok;
        }

        public static bool GetClientAreaAnimation()
        {
            int v = 1;
            SpiRefInt(0x1042, 0, ref v, 0);
            return v != 0;
        }

        public static bool SetClientAreaAnimation(bool on)
        {
            bool ok = SpiPtr(0x1043, 0, new IntPtr(on ? 1 : 0), FLAGS);
            if (ok) Notify(0x1043);
            return ok;
        }

        public static bool SetMenuShowDelay(uint ms)
        {
            bool ok = SpiPtr(0x006B, ms, IntPtr.Zero, FLAGS);
            if (ok) Notify(0x006B);
            return ok;
        }

        // Aplica al instante los flags de Teclas especiales, Teclas de filtro y Teclas de alternancia
        public static bool SetAccessibilityFlags(uint sticky, uint filter, uint toggle)
        {
            bool ok = true;
            FLAGSONLY sk = new FLAGSONLY();
            sk.cbSize = (uint)Marshal.SizeOf(typeof(FLAGSONLY));
            if (SpiFlags(0x003A, sk.cbSize, ref sk, 0)) { sk.dwFlags = sticky; ok &= SpiFlags(0x003B, sk.cbSize, ref sk, FLAGS); } else ok = false;

            FILTERKEYS fk = new FILTERKEYS();
            fk.cbSize = (uint)Marshal.SizeOf(typeof(FILTERKEYS));
            if (SpiFilter(0x0032, fk.cbSize, ref fk, 0)) { fk.dwFlags = filter; ok &= SpiFilter(0x0033, fk.cbSize, ref fk, FLAGS); } else ok = false;

            FLAGSONLY tk = new FLAGSONLY();
            tk.cbSize = (uint)Marshal.SizeOf(typeof(FLAGSONLY));
            if (SpiFlags(0x0034, tk.cbSize, ref tk, 0)) { tk.dwFlags = toggle; ok &= SpiFlags(0x0035, tk.cbSize, ref tk, FLAGS); } else ok = false;
            Notify(0x0033);
            return ok;
        }

        // WM_SETTINGCHANGE a todas las ventanas (p. ej. "ImmersiveColorSet" para la transparencia)
        public static void BroadcastSettingChange(string area)
        {
            Thread t = new Thread(delegate ()
            {
                UIntPtr r;
                SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, UIntPtr.Zero, area, 0x0002, 3000, out r);
            });
            t.IsBackground = true;
            t.Start();
        }
    }

    // ---------------------------------------------------------------- Pantallas y frecuencia
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    public class DisplayInfo
    {
        public string Device { get; set; }
        public string Adapter { get; set; }
        public string Monitor { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Bpp { get; set; }
        public int CurrentHz { get; set; }
        public int MaxHz { get; set; }
        public bool Primary { get; set; }
    }

    public static class Display
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int ChangeDisplaySettingsEx(string deviceName, ref DEVMODE devMode, IntPtr hwnd, uint flags, IntPtr lParam);

        const int ENUM_CURRENT_SETTINGS = -1;
        const int DM_BITSPERPEL = 0x40000;
        const int DM_PELSWIDTH = 0x80000;
        const int DM_PELSHEIGHT = 0x100000;
        const int DM_DISPLAYFREQUENCY = 0x400000;
        const int DM_INTERLACED = 0x2;
        const uint CDS_UPDATEREGISTRY = 0x01;
        const uint CDS_TEST = 0x02;

        static DEVMODE NewMode()
        {
            DEVMODE m = new DEVMODE();
            m.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            return m;
        }

        static DISPLAY_DEVICE NewDevice()
        {
            DISPLAY_DEVICE d = new DISPLAY_DEVICE();
            d.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            return d;
        }

        static bool SameMode(DEVMODE a, DEVMODE b)
        {
            return a.dmPelsWidth == b.dmPelsWidth && a.dmPelsHeight == b.dmPelsHeight && a.dmBitsPerPel == b.dmBitsPerPel;
        }

        public static List<DisplayInfo> GetDisplays()
        {
            List<DisplayInfo> list = new List<DisplayInfo>();
            for (uint i = 0; i < 32; i++)
            {
                DISPLAY_DEVICE d = NewDevice();
                if (!EnumDisplayDevices(null, i, ref d, 0)) break;
                if ((d.StateFlags & 0x1) == 0) continue; // no conectado al escritorio

                DEVMODE cur = NewMode();
                if (!EnumDisplaySettings(d.DeviceName, ENUM_CURRENT_SETTINGS, ref cur)) continue;

                DisplayInfo info = new DisplayInfo();
                info.Device = d.DeviceName;
                info.Adapter = d.DeviceString;
                info.Width = cur.dmPelsWidth;
                info.Height = cur.dmPelsHeight;
                info.Bpp = cur.dmBitsPerPel;
                info.CurrentHz = cur.dmDisplayFrequency;
                info.MaxHz = cur.dmDisplayFrequency;
                info.Primary = (d.StateFlags & 0x4) != 0;

                DISPLAY_DEVICE mon = NewDevice();
                if (EnumDisplayDevices(d.DeviceName, 0, ref mon, 0)) info.Monitor = mon.DeviceString;

                DEVMODE m = NewMode();
                for (int n = 0; EnumDisplaySettings(d.DeviceName, n, ref m); n++)
                {
                    if (SameMode(m, cur) && (m.dmDisplayFlags & DM_INTERLACED) == 0 && m.dmDisplayFrequency > info.MaxHz)
                        info.MaxHz = m.dmDisplayFrequency;
                    m = NewMode();
                }
                list.Add(info);
            }
            return list;
        }

        // Devuelve 0 si se aplicó; <0 errores propios; >0 códigos DISP_CHANGE de Windows
        public static int SetFrequency(string device, int hz)
        {
            DEVMODE cur = NewMode();
            if (!EnumDisplaySettings(device, ENUM_CURRENT_SETTINGS, ref cur)) return -100;
            if (cur.dmDisplayFrequency == hz) return 0;

            DEVMODE m = NewMode();
            DEVMODE target = NewMode();
            bool found = false;
            for (int n = 0; EnumDisplaySettings(device, n, ref m); n++)
            {
                if (SameMode(m, cur) && (m.dmDisplayFlags & DM_INTERLACED) == 0 && m.dmDisplayFrequency == hz)
                {
                    target = m;
                    found = true;
                    break;
                }
                m = NewMode();
            }
            if (!found) return -101;

            target.dmFields = DM_BITSPERPEL | DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
            int r = ChangeDisplaySettingsEx(device, ref target, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
            if (r != 0) return r;
            return ChangeDisplaySettingsEx(device, ref target, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        }
    }

    // ---------------------------------------------------------------- Medidor de Hz del ratón (Raw Input)
    public static class MouseHz
    {
        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTDEVICE { public ushort usUsagePage; public ushort usUsage; public uint dwFlags; public IntPtr hwndTarget; }
        [StructLayout(LayoutKind.Sequential)]
        struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int ptX; public int ptY; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASSEX
        {
            public uint cbSize; public uint style; public IntPtr lpfnWndProc; public int cbClsExtra; public int cbWndExtra;
            public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground;
            public string lpszMenuName; public string lpszClassName; public IntPtr hIconSm;
        }
        delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateWindowEx(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devs, uint num, uint size);
        [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr h, uint min, uint max);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll")] static extern void PostQuitMessage(int code);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

        const string ClassName = "OptPcRawMouseSink";
        static WndProcDelegate _proc;
        static IntPtr _hwnd = IntPtr.Zero;
        static Thread _thread;
        static long _count;
        static volatile bool _running;

        public static long Count { get { return Interlocked.Read(ref _count); } }
        public static bool Running { get { return _running; } }

        public static void Start()
        {
            if (_thread != null && _thread.IsAlive) _thread.Join(1000);
            if (_running) return;
            _running = true;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Start();
        }

        public static void Stop()
        {
            IntPtr h = _hwnd;
            if (h != IntPtr.Zero) PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
        }

        static IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l)
        {
            if (m == 0x00FF) { Interlocked.Increment(ref _count); return DefWindowProc(h, m, w, l); } // WM_INPUT
            if (m == 0x0010) { DestroyWindow(h); return IntPtr.Zero; }                              // WM_CLOSE
            if (m == 0x0002) { PostQuitMessage(0); return IntPtr.Zero; }                            // WM_DESTROY
            return DefWindowProc(h, m, w, l);
        }

        static void Loop()
        {
            try
            {
                if (_proc == null) _proc = new WndProcDelegate(WndProc);
                WNDCLASSEX wc = new WNDCLASSEX();
                wc.cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX));
                wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc);
                wc.hInstance = GetModuleHandle(null);
                wc.lpszClassName = ClassName;
                RegisterClassEx(ref wc); // si ya existe de una medición anterior, falla sin problema

                _hwnd = CreateWindowEx(0, ClassName, "", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, wc.hInstance, IntPtr.Zero); // HWND_MESSAGE
                if (_hwnd == IntPtr.Zero) return;

                RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[1];
                rid[0].usUsagePage = 0x01;
                rid[0].usUsage = 0x02;      // ratón
                rid[0].dwFlags = 0x00000100; // RIDEV_INPUTSINK
                rid[0].hwndTarget = _hwnd;
                uint size = (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE));
                if (!RegisterRawInputDevices(rid, 1, size)) { DestroyWindow(_hwnd); return; }

                MSG msg;
                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }

                rid[0].dwFlags = 0x00000001; // RIDEV_REMOVE
                rid[0].hwndTarget = IntPtr.Zero;
                RegisterRawInputDevices(rid, 1, size);
            }
            finally
            {
                _hwnd = IntPtr.Zero;
                _running = false;
            }
        }
    }

    // ---------------------------------------------------------------- Memoria
    public static class Mem
    {
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct TOKEN_PRIVILEGES { public int Count; public long Luid; public int Attr; }

        [DllImport("ntdll.dll")] static extern int NtSetSystemInformation(int infoClass, ref int info, int length);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool LookupPrivilegeValue(string sys, string name, out long luid);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES newState, int len, IntPtr prev, IntPtr retLen);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        static bool EnablePrivilege(string name)
        {
            IntPtr token;
            if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out token)) return false; // ADJUST_PRIVILEGES | QUERY
            try
            {
                TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES();
                tp.Count = 1;
                tp.Attr = 0x2; // SE_PRIVILEGE_ENABLED
                if (!LookupPrivilegeValue(null, name, out tp.Luid)) return false;
                if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)) return false;
                return Marshal.GetLastWin32Error() == 0;
            }
            finally { CloseHandle(token); }
        }

        // SystemMemoryListInformation (80): 4 = MemoryPurgeStandbyList, 3 = MemoryFlushModifiedList
        static int MemoryCommand(int command)
        {
            EnablePrivilege("SeProfileSingleProcessPrivilege");
            int cmd = command;
            return NtSetSystemInformation(80, ref cmd, 4);
        }

        public static int PurgeStandby() { return MemoryCommand(4); }
        public static int FlushModified() { return MemoryCommand(3); }
        /// <summary>Saca a la lista en espera las páginas de todos los procesos (no los cierra; las recuperan al usarlas).</summary>
        public static int EmptyWorkingSets() { return MemoryCommand(2); }
        /// <summary>Vacía solo la parte de baja prioridad de la lista en espera (prioridad 0).</summary>
        public static int PurgeLowPriorityStandby() { return MemoryCommand(5); }

        // SystemCombinePhysicalMemoryInformation (130): une páginas idénticas (Windows 10+)
        [StructLayout(LayoutKind.Sequential)]
        struct MEMORY_COMBINE_INFORMATION_EX { public IntPtr Handle; public UIntPtr PagesCombined; public uint Flags; }

        [DllImport("ntdll.dll", EntryPoint = "NtSetSystemInformation")]
        static extern int NtSetCombine(int infoClass, ref MEMORY_COMBINE_INFORMATION_EX info, int length);

        public static int CombinePages(out long pagesCombined)
        {
            EnablePrivilege("SeProfileSingleProcessPrivilege");
            var info = new MEMORY_COMBINE_INFORMATION_EX();
            int status = NtSetCombine(130, ref info, Marshal.SizeOf(typeof(MEMORY_COMBINE_INFORMATION_EX)));
            pagesCombined = (long)info.PagesCombined.ToUInt64();
            return status;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetSystemFileCacheSize(UIntPtr minimum, UIntPtr maximum, uint flags);

        /// <summary>Vacía el conjunto de trabajo de la caché de archivos del sistema. Devuelve el código de error (0 = bien).</summary>
        public static int TrimFileCache()
        {
            EnablePrivilege("SeIncreaseQuotaPrivilege");
            var all = new UIntPtr(ulong.MaxValue);
            return SetSystemFileCacheSize(all, all, 0) ? 0 : Marshal.GetLastWin32Error();
        }

        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", EntryPoint = "K32EmptyWorkingSet", SetLastError = true)] static extern bool EmptyWorkingSet(IntPtr process);

        /// <summary>Recorta la memoria de un proceso (sus páginas pasan a la lista en espera). true si se pudo.</summary>
        public static bool TrimProcess(int pid)
        {
            IntPtr h = OpenProcess(0x1000 | 0x0100, false, pid); // QUERY_LIMITED_INFORMATION | SET_QUOTA
            if (h == IntPtr.Zero) return false;
            try { return EmptyWorkingSet(h); }
            finally { CloseHandle(h); }
        }

        /// <summary>Listas de memoria física en bytes. Ceros y código de error si no se pueden leer.</summary>
        public static int GetDetail(out double free, out double standby, out double modified, out double standbyLow)
        {
            free = standby = modified = standbyLow = 0;
            EnablePrivilege("SeProfileSingleProcessPrivilege");
            int size = 22 * IntPtr.Size;
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                int ret;
                int status = NtQuerySystemInformation(80, buf, size, out ret);
                if (status != 0) return status;
                Func<int, double> at = delegate (int i) { return (double)(ulong)Marshal.ReadIntPtr(buf, i * IntPtr.Size).ToInt64(); };
                free = (at(0) + at(1)) * 4096.0;
                modified = (at(2) + at(3)) * 4096.0;
                double sb = 0;
                for (int i = 5; i < 13; i++) sb += at(i);
                standby = sb * 4096.0;
                standbyLow = at(5) * 4096.0;
                return 0;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int infoClass, IntPtr info, int length, out int returnLength);

        // SYSTEM_MEMORY_LIST_INFORMATION: Zero, Free, Modified, ModifiedNoWrite, Bad, PageCountByPriority[8], Repurposed[8], ModifiedPageFile
        public static int GetLists(out double standbyBytes, out double freeBytes)
        {
            standbyBytes = 0; freeBytes = 0;
            EnablePrivilege("SeProfileSingleProcessPrivilege");
            int size = 22 * IntPtr.Size;
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                int ret;
                int status = NtQuerySystemInformation(80, buf, size, out ret);
                if (status != 0) return status;
                Func<int, double> at = delegate (int i) { return (double)(ulong)Marshal.ReadIntPtr(buf, i * IntPtr.Size).ToInt64(); };
                freeBytes = (at(0) + at(1)) * 4096.0;
                double standby = 0;
                for (int i = 5; i < 13; i++) standby += at(i);
                standbyBytes = standby * 4096.0;
                return 0;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }

    // ---------------------------------------------------------------- CPU sin contadores de rendimiento
    public static class CpuMeter
    {
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        static long _idle, _kernel, _user;
        static bool _has;

        // Porcentaje de uso desde la llamada anterior (-1 en la primera)
        public static double Sample()
        {
            long i, k, u;
            if (!GetSystemTimes(out i, out k, out u)) return -1;
            double r = -1;
            if (_has)
            {
                long di = i - _idle, total = (k - _kernel) + (u - _user); // el tiempo de kernel incluye el inactivo
                if (total > 0) r = Math.Max(0, Math.Min(100, (total - di) * 100.0 / total));
            }
            _idle = i; _kernel = k; _user = u; _has = true;
            return r;
        }
    }

    // ---------------------------------------------------------------- GPU NVIDIA (NVML, viene con el driver)
    public static class Nvml
    {
        [StructLayout(LayoutKind.Sequential)] struct Utilization { public uint gpu; public uint memory; }
        [StructLayout(LayoutKind.Sequential)] struct MemoryInfo { public ulong total; public ulong free; public ulong used; }
        [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")] static extern int Init();
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCount_v2")] static extern int GetCount(out uint count);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")] static extern int GetHandle(uint index, out IntPtr device);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")] static extern int GetUtilization(IntPtr device, out Utilization u);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo")] static extern int GetMemory(IntPtr device, out MemoryInfo m);

        static bool _tried, _ok;
        static List<IntPtr> _devices = new List<IntPtr>();

        public static bool Available { get { EnsureInit(); return _ok; } }

        static void EnsureInit()
        {
            if (_tried) return;
            _tried = true;
            try
            {
                if (Init() != 0) return;
                uint n;
                if (GetCount(out n) != 0 || n == 0) return;
                for (uint i = 0; i < n; i++) { IntPtr h; if (GetHandle(i, out h) == 0) _devices.Add(h); }
                _ok = _devices.Count > 0;
            }
            catch { _ok = false; }
        }

        // Uso de la GPU más cargada y memoria de vídeo usada/total de esa misma GPU
        public static bool Read(out double gpu, out double usedBytes, out double totalBytes)
        {
            gpu = 0; usedBytes = 0; totalBytes = 0;
            if (!Available) return false;
            bool any = false;
            foreach (IntPtr d in _devices)
            {
                Utilization u; MemoryInfo m;
                if (GetUtilization(d, out u) != 0) continue;
                if (!any || u.gpu >= gpu)
                {
                    gpu = u.gpu;
                    if (GetMemory(d, out m) == 0) { usedBytes = m.used; totalBytes = m.total; }
                }
                any = true;
            }
            return any;
        }
    }

    // ---------------------------------------------------------------- Contadores PDH con nombres en inglés (para AMD/Intel)
    public sealed class PdhSet
    {
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhOpenQuery(string source, IntPtr user, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
        [DllImport("pdh.dll")] static extern int PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, ref uint count, IntPtr buffer);

        IntPtr _query;
        Dictionary<string, IntPtr> _counters = new Dictionary<string, IntPtr>();
        public bool Ok { get; private set; }

        public PdhSet() { Ok = PdhOpenQuery(null, IntPtr.Zero, out _query) == 0; }

        public bool Add(string key, string englishPath)
        {
            if (!Ok) return false;
            IntPtr c;
            if (PdhAddEnglishCounter(_query, englishPath, IntPtr.Zero, out c) != 0) return false;
            _counters[key] = c;
            return true;
        }

        public bool Has(string key) { return _counters.ContainsKey(key); }
        public bool Collect() { return Ok && PdhCollectQueryData(_query) == 0; }

        // Suma los valores por adaptador (parte "luid_..." del nombre de instancia) y devuelve el mayor
        public double MaxByAdapter(string key)
        {
            Dictionary<string, double> sums = new Dictionary<string, double>();
            foreach (KeyValuePair<string, double> kv in Read(key))
            {
                string name = kv.Key;
                int a = name.IndexOf("luid_");
                int b = a < 0 ? -1 : name.IndexOf("_phys", a);
                string adapter = (a >= 0 && b > a) ? name.Substring(a, b - a) : "x";
                double s;
                sums.TryGetValue(adapter, out s);
                sums[adapter] = s + kv.Value;
            }
            double max = -1;
            foreach (double v in sums.Values) if (v > max) max = v;
            return max;
        }

        public double Max(string key)
        {
            double max = -1;
            foreach (KeyValuePair<string, double> kv in Read(key)) if (kv.Value > max) max = kv.Value;
            return max;
        }

        List<KeyValuePair<string, double>> Read(string key)
        {
            List<KeyValuePair<string, double>> list = new List<KeyValuePair<string, double>>();
            IntPtr c;
            if (!_counters.TryGetValue(key, out c)) return list;
            const uint FMT = 0x00000200 | 0x00008000; // PDH_FMT_DOUBLE | PDH_FMT_NOCAP100
            uint size = 0, count = 0;
            int r = PdhGetFormattedCounterArray(c, FMT, ref size, ref count, IntPtr.Zero);
            if (r != unchecked((int)0x800007D2) || size == 0) return list; // PDH_MORE_DATA
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArray(c, FMT, ref size, ref count, buf) != 0) return list;
                int itemSize = IntPtr.Size == 8 ? 24 : 16;
                int valueOffset = IntPtr.Size == 8 ? 16 : 8;
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = new IntPtr(buf.ToInt64() + (long)i * itemSize);
                    string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item, 0));
                    int status = Marshal.ReadInt32(item, IntPtr.Size);
                    if (status != 0 && status != 1) continue; // PDH_CSTATUS_VALID_DATA / NEW_DATA
                    double v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, valueOffset));
                    list.Add(new KeyValuePair<string, double>(name ?? "", v));
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return list;
        }
    }

    // ---------------------------------------------------------------- Modo de energía (control deslizante de Windows)
    public static class PowerOverlay
    {
        [DllImport("powrprof.dll", EntryPoint = "PowerSetActiveOverlayScheme")] static extern uint SetOverlay(Guid overlay);
        [DllImport("powrprof.dll", EntryPoint = "PowerGetActualOverlayScheme")] static extern uint GetOverlay(out Guid overlay);

        public static string Get()
        {
            Guid g;
            return GetOverlay(out g) == 0 ? g.ToString() : null;
        }

        public static bool Set(string guid)
        {
            return SetOverlay(new Guid(guid)) == 0;
        }
    }
}
