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

            using (var mutex = new Mutex(true, @"Global\Velox_SingleInstance", out bool createdNew))
            {
                if (!createdNew)
                {
                    NativeWindowActivator.ActivateExisting("Velox");
                    return 0;
                }

                AppPaths.Initialize();
                Log.Initialize();
                Log.Info("=== Velox " + AppInfo.Version + " ===");

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
