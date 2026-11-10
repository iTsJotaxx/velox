using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Velox.Core;
using Velox.Core.Update;
using Velox.Localization;

namespace Velox.UI.Views
{
    public partial class SettingsView : UserControl, IPage
    {
        UpdateInfo _info;
        string _updError;
        bool _checking, _checkedOnce;

        public SettingsView()
        {
            InitializeComponent();
            TxtDataPath.Text = AppPaths.DataDir;
            TxtAbout.Text = "Velox " + AppInfo.Version;
            AppState.Current.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(AppState.IsBusy)) RenderUpdate(); };
            Load();
        }

        MainWindow Win => (MainWindow)Window.GetWindow(this);

        void Load()
        {
            RbEs.IsChecked = Loc.Language == "es";
            RbEn.IsChecked = Loc.Language == "en";
            ChkRestore.IsChecked = Settings.Current.CreateRestorePoint;
            ChkExpand.IsChecked = Settings.Current.ExpandAllCategories;
            RenderUpdate();
        }

        public async void OnShown()
        {
            Load();
            if (!_checkedOnce) await CheckAsync();
        }

        public void RefreshTexts() => Load();

        void Lang_Click(object sender, RoutedEventArgs e)
        {
            string lang = (string)((RadioButton)sender).Tag;
            if (lang == Loc.Language) return;
            Settings.Current.Language = lang;
            Settings.Save();
            Loc.SetLanguage(lang);
        }

        void Prefs_Click(object sender, RoutedEventArgs e)
        {
            Settings.Current.CreateRestorePoint = ChkRestore.IsChecked == true;
            Settings.Current.ExpandAllCategories = ChkExpand.IsChecked == true;
            Settings.Save();
        }

        // ------------------------------------------------------------ Actualización de Velox

        async Task CheckAsync()
        {
            if (_checking) return;
            _checking = true; _checkedOnce = true; _updError = null;
            RenderUpdate();
            try { _info = await Task.Run(() => Updater.Check()); }
            catch (Exception ex) { _info = null; _updError = ex.Message; Log.Warn("Update check failed: " + ex.Message); }
            _checking = false;
            RenderUpdate();
        }

        void RenderUpdate()
        {
            TxtUpdVersion.Text = Loc.F("ui.upd.current", AppInfo.Version);
            bool idle = AppState.Current.IsIdle;
            if (_checking) TxtUpdStatus.Text = Loc.T("ui.upd.checking");
            else if (_updError != null) TxtUpdStatus.Text = Loc.F("ui.upd.checkFailed", _updError);
            else if (_info == null) TxtUpdStatus.Text = Loc.T("ui.upd.notChecked");
            else if (_info.IsNewer) TxtUpdStatus.Text = Loc.F("ui.upd.available", _info.Tag);
            else TxtUpdStatus.Text = Loc.F("ui.upd.upToDate", AppInfo.Version);

            BtnUpdCheck.IsEnabled = idle && !_checking;
            bool canUpdate = _info != null && _info.IsNewer;
            BtnUpdate.Visibility = canUpdate ? Visibility.Visible : Visibility.Collapsed;
            if (canUpdate) BtnUpdate.Content = Loc.F("ui.upd.update", _info.Tag);
            BtnUpdate.IsEnabled = idle && !_checking;
        }

        async void UpdCheck_Click(object sender, RoutedEventArgs e) => await CheckAsync();

        async void Update_Click(object sender, RoutedEventArgs e)
        {
            var info = _info;
            if (info == null || !info.IsNewer) return;
            var w = Win;
            string notes = (info.Notes ?? "").Trim();
            if (notes.Length > 1500) notes = notes.Substring(0, 1500) + "...";
            if (!await w.ConfirmAsync(Loc.F("ui.upd.confirmTitle", info.Tag), Loc.T("ui.upd.confirmText"), notes, Loc.T("ui.upd.confirmOk"), Loc.T("ui.btn.cancel"))) return;

            string staged = null, error = null;
            await AppState.Current.RunBusy(Loc.F("ui.upd.starting", info.Tag), async () =>
            {
                try
                {
                    var progress = new Progress<string>(t => AppState.Current.SetBusyText(t));
                    staged = await Task.Run(() => Updater.DownloadAndVerify(info, progress, CancellationToken.None));
                    AppState.Current.SetBusyText(Loc.T("ui.upd.applying"));
                    await Task.Run(() => Updater.ApplyAndRestart(staged));
                }
                catch (Exception ex) { staged = null; error = ex.Message; Log.Error("Self-update", ex); }
            });

            if (error != null)
            {
                await w.ConfirmAsync(Loc.T("ui.upd.failedTitle"), error, null, Loc.T("ui.btn.ok"), null);
                return;
            }
            if (staged != null)
            {
                Log.Info("Closing Velox to finish the update to " + info.Tag + ".");
                w.Close(); // la versión nueva ya está arrancando y espera a que esta se cierre
            }
        }

        void ViewLog_Click(object sender, RoutedEventArgs e) => Win.Navigate("log");
        void OpenData_Click(object sender, RoutedEventArgs e) => Process.Start("explorer.exe", "\"" + AppPaths.DataDir + "\"");
        void GitHub_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo(AppInfo.RepositoryUrl) { UseShellExecute = true });
    }
}
