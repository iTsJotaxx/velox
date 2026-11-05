using System;
using System.Runtime.InteropServices;

namespace Velox.Core.Sys
{
    /// <summary>Tareas programadas vía la API COM del Programador de tareas (independiente del idioma).</summary>
    public static class ScheduledTasks
    {
        /// <summary>Ruta completa, p. ej. \Microsoft\Windows\Autochk\Proxy</summary>
        static dynamic Find(string fullPath)
        {
            int i = fullPath.LastIndexOf('\\');
            string folder = i <= 0 ? "\\" : fullPath.Substring(0, i);
            string name = fullPath.Substring(i + 1);
            try
            {
                Type t = Type.GetTypeFromProgID("Schedule.Service");
                dynamic svc = Activator.CreateInstance(t);
                svc.Connect();
                dynamic f = svc.GetFolder(folder);
                return f.GetTask(name);
            }
            catch (COMException) { return null; }
            catch (Exception) { return null; }
        }

        public static bool Exists(string fullPath) => Find(fullPath) != null;

        /// <summary>null si la tarea no existe.</summary>
        public static bool? IsEnabled(string fullPath)
        {
            dynamic t = Find(fullPath);
            if (t == null) return null;
            try { return (bool)t.Enabled; } catch { return null; }
        }

        public static void SetEnabled(string fullPath, bool enabled)
        {
            dynamic t = Find(fullPath);
            if (t == null) throw new InvalidOperationException("Task not found: " + fullPath);
            t.Enabled = enabled;
        }
    }
}
