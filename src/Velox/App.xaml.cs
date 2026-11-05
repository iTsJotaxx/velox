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
