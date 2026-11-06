using System;
using System.IO;

namespace Velox.Core.Drivers
{
    /// <summary>
    /// Cachés de shaders de NVIDIA y DirectX. Tras cambiar el driver de la gráfica pueden quedar entradas dañadas que
    /// se reutilizan en cada arranque (cierres de GPU siempre en el mismo punto); se reconstruyen solas al jugar.
    /// </summary>
    public static class ShaderCache
    {
        static string[] Dirs()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return new[] { Path.Combine(local, "NVIDIA", "DXCache"), Path.Combine(local, "NVIDIA", "GLCache"), Path.Combine(local, "D3DSCache") };
        }

        /// <summary>Borra los archivos de caché (los que estén en uso se saltan). Devuelve los bytes liberados. No lanza.</summary>
        public static long Clear()
        {
            long freed = 0;
            foreach (var dir in Dirs())
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    var di = new DirectoryInfo(dir);
                    if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue; // nunca seguir enlaces
                    freed += ClearDir(di);
                }
                catch (Exception ex) { Log.Warn("Shader cache " + dir + ": " + ex.Message); }
            }
            Log.Info("Shader caches cleared: " + (freed / (1024.0 * 1024.0)).ToString("N0") + " MB.");
            return freed;
        }

        static long ClearDir(DirectoryInfo di)
        {
            long freed = 0;
            foreach (var f in di.GetFiles())
            {
                if ((f.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                try { long len = f.Length; f.Attributes = FileAttributes.Normal; f.Delete(); freed += len; } catch { }
            }
            foreach (var d in di.GetDirectories())
            {
                if ((d.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                freed += ClearDir(d);
                try { d.Delete(); } catch { }
            }
            return freed;
        }
    }
}
