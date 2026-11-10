using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Velox.UI.Infra
{
    /// <summary>Icono de un programa (archivo .exe/.dll/.ico con índice) como imagen WPF. Se puede llamar desde cualquier hilo.</summary>
    public static class IconLoader
    {
        static readonly ConcurrentDictionary<string, ImageSource> Cache = new ConcurrentDictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint count);

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon; public int iIcon; public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO info, uint size, uint flags);

        /// <summary>null si no hay icono.</summary>
        public static ImageSource Load(string path, int index)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string key = path + "|" + index;
            if (Cache.TryGetValue(key, out var cached)) return cached;
            ImageSource img = null;
            try
            {
                if (File.Exists(path))
                {
                    if (path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                    {
                        var dec = new IconBitmapDecoder(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                        BitmapFrame best = null;
                        foreach (var f in dec.Frames) if (best == null || Math.Abs(f.PixelWidth - 32) < Math.Abs(best.PixelWidth - 32)) best = f;
                        if (best != null) { best.Freeze(); img = best; }
                    }
                    else
                    {
                        var large = new IntPtr[1];
                        if (ExtractIconEx(path, index, large, null, 1) > 0 && large[0] != IntPtr.Zero) img = FromHIcon(large[0]);
                        if (img == null)
                        {
                            var info = new SHFILEINFO();
                            if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), 0x100 /* ICON */) != IntPtr.Zero && info.hIcon != IntPtr.Zero)
                                img = FromHIcon(info.hIcon);
                        }
                    }
                }
            }
            catch { img = null; }
            Cache[key] = img;
            return img;
        }

        static ImageSource FromHIcon(IntPtr h)
        {
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(h, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally { DestroyIcon(h); }
        }
    }
}
