using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Velox.Core;
using Velox.Core.Backup;
using Velox.Localization;

namespace Velox
{
    public partial class App : Application
    {
        public static string StartPage { get; set; } = "summary";
        /// <summary>--disk=ruta: abre «Espacio en disco» analizando esa carpeta.</summary>
        public static string DiskPath { get; set; }
#if UITEST
        /// <summary>Solo pruebas: guarda una imagen de la ventana en esta ruta y cierra la app.</summary>
        public static string ShotPath { get; set; }
        public static int ShotDelay { get; set; } = 6;
        /// <summary>Solo pruebas: en «Espacio en disco», entra N veces en la carpeta más grande.</summary>
        public static int DiskEnter { get; set; }
        /// <summary>Solo pruebas: acción extra antes de la captura (p. ej. "clean" en Memoria).</summary>
        public static string TestAction { get; set; }
        /// <summary>Solo pruebas: tamaño de la ventana, p. ej. "1060x680" (el mínimo).</summary>
        public static string TestSize { get; set; }
#endif

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnDispatcherException;
            AppDomain.CurrentDomain.UnhandledException += (s, a) => Log.Error("Unhandled: " + a.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (s, a) => { Log.Error("Task", a.Exception); a.SetObserved(); };

            Settings.Load();
            BackupStore.Load();
            Loc.SetLanguage(Settings.Current.Language ?? Loc.SystemLanguage());

            var w = new UI.MainWindow();
            MainWindow = w;
            w.Show();
        }

        void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error("UI error", e.Exception);
            e.Handled = true;
            try
            {
                if (MainWindow is UI.MainWindow mw) mw.ShowToast(Loc.T("ui.msg.unexpectedError") + " " + e.Exception.Message, UI.ToastKind.Error);
            }
            catch { }
        }
    }
}
