using System;
using System.Threading;
using System.Windows;
using Velox.Core;

namespace Velox
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            // WPF en .NET Framework 4.8: escalado nítido al mover la ventana entre monitores con distinto DPI
            AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges", false);

#if UITEST
            const string instanceName = @"Global\Velox_UiTest";   // compilación de capturas automáticas
#else
            const string instanceName = @"Global\Velox_SingleInstance";
#endif
            bool afterUpdate = Array.Exists(args, a => a.Equals("--after-update", StringComparison.OrdinalIgnoreCase));
            using (var mutex = new Mutex(true, instanceName, out bool createdNew))
            {
                if (!createdNew && afterUpdate)
                {
                    // Tras actualizar, la versión anterior todavía se está cerrando: se espera a que suelte la instancia
                    try { createdNew = mutex.WaitOne(20000); }
                    catch (AbandonedMutexException) { createdNew = true; }
                }
                if (!createdNew)
                {
                    NativeWindowActivator.ActivateExisting("Velox");
                    return 0;
                }

                AppPaths.Initialize();
                Log.Initialize();
                Log.Info("=== Velox " + AppInfo.Version + " ===");
                Core.Update.Updater.CleanupOld();

                // --page=optimizations abre directamente esa página (útil para atajos y capturas)
                foreach (var a in args)
                    if (a.StartsWith("--page=", StringComparison.OrdinalIgnoreCase)) App.StartPage = a.Substring(7);

                var app = new App();
                app.InitializeComponent();
                int code = app.Run();
                Log.Info("=== Velox closed ===");
                return code;
            }
        }
    }
}
