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
            RenderProfiles();
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

        // ------------------------------------------------------------ Perfiles y exportar / importar

        void RenderProfiles()
        {
            ProfileList.Children.Clear();
            System.Collections.Generic.List<Core.Profiles.VeloxProfile> list;
            try { list = Core.Profiles.ProfileStore.List(); }
            catch (Exception ex) { Log.Warn("Profiles: " + ex.Message); list = new System.Collections.Generic.List<Core.Profiles.VeloxProfile>(); }
            foreach (var p in list)
            {
                var g = new Grid { Margin = new Thickness(0, 4, 0, 0) };
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                sp.Children.Add(new TextBlock { Text = "  ·  " + p.Created.ToString("d") + "  ·  " + Loc.F("prof.count", p.SettingsCount), Style = (Style)FindResource("Muted"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
                g.Children.Add(sp);
                var bp = new StackPanel { Orientation = Orientation.Horizontal };
                var apply = new Button { Content = Loc.T("prof.apply"), Style = (Style)FindResource("BtnBase"), Padding = new Thickness(12, 6, 12, 6), Tag = p, IsEnabled = AppState.Current.IsIdle };
                apply.Click += async (s, e) => await AppState.Current.ImportProfileAsync((Core.Profiles.VeloxProfile)((Button)s).Tag);
                var del = new Button { Content = Loc.T("prof.delete"), Style = (Style)FindResource("BtnGhost"), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(6, 0, 0, 0), Tag = p };
                del.Click += async (s, e) =>
                {
                    var prof = (Core.Profiles.VeloxProfile)((Button)s).Tag;
                    if (!await Win.ConfirmAsync(Loc.F("prof.deleteTitle", prof.Name), Loc.T("prof.deleteText"), null, Loc.T("prof.delete"), Loc.T("ui.btn.cancel"), true)) return;
                    try { Core.Profiles.ProfileStore.Delete(prof.Name); } catch (Exception ex) { Win.ShowToast(ex.Message, ToastKind.Error); }
                    RenderProfiles();
                };
                System.Windows.Automation.AutomationProperties.SetName(apply, Loc.T("prof.apply") + " " + p.Name);
                System.Windows.Automation.AutomationProperties.SetName(del, Loc.T("prof.delete") + " " + p.Name);
                bp.Children.Add(apply); bp.Children.Add(del);
                Grid.SetColumn(bp, 1); g.Children.Add(bp);
                ProfileList.Children.Add(g);
            }
            var st = Settings.Current;
            string Yn(bool? v) => v == null ? Loc.T("wiz.unanswered") : v.Value ? Loc.T("wiz.yes") : Loc.T("wiz.no");
            TxtAnswers.Text = Loc.F("wiz.summary", st.UserLaptop == null ? Loc.T("wiz.detected") : Yn(st.UserLaptop), Yn(st.UserCompetitive), Yn(st.UserObs));
        }

        void ProfileName_Changed(object sender, TextChangedEventArgs e)
        {
            ProfileNameHint.Visibility = string.IsNullOrEmpty(TxtProfileName.Text) ? Visibility.Visible : Visibility.Collapsed;
            BtnSaveProfile.IsEnabled = Core.Profiles.ProfileStore.SafeName(TxtProfileName.Text).Length > 0;
        }

        void ProfileName_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter && BtnSaveProfile.IsEnabled) { SaveProfile_Click(sender, e); e.Handled = true; }
        }

        /// <summary>Guarda los ajustes actuales con un nombre (sin apps: un perfil es «cómo quiero el equipo»).</summary>
        async void SaveProfile_Click(object sender, RoutedEventArgs e)
        {
            string name = Core.Profiles.ProfileStore.SafeName(TxtProfileName.Text);
            if (name.Length == 0 || AppState.Current.Sys == null) return;
            string path = Core.Profiles.ProfileStore.PathFor(name);
            if (System.IO.File.Exists(path) && !await Win.ConfirmAsync(Loc.F("prof.overwriteTitle", name), Loc.T("prof.overwriteText"), null, Loc.T("prof.save"), Loc.T("ui.btn.cancel"))) return;
            try
            {
                var sys = AppState.Current.Sys;
                var p = await Task.Run(() => Core.Profiles.ProfileStore.Capture(sys, name));
                Core.Profiles.ProfileStore.Save(p, path);
                TxtProfileName.Text = "";
                Win.ShowToast(Loc.F("prof.saved", name), ToastKind.Success);
            }
            catch (Exception ex) { Log.Error("Save profile", ex); Win.ShowToast(ex.Message, ToastKind.Error); }
            RenderProfiles();
        }

        /// <summary>Exporta todo (ajustes, apps instaladas del catálogo, Memoria, respuestas) a un archivo para otro equipo.</summary>
        async void Export_Click(object sender, RoutedEventArgs e)
        {
            if (AppState.Current.Sys == null) return;
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "Velox-" + DateTime.Now.ToString("yyyy-MM-dd") + Core.Profiles.ProfileStore.Extension,
                Filter = Loc.T("prof.filter") + "|*" + Core.Profiles.ProfileStore.Extension,
                AddExtension = false,
            };
            if (dlg.ShowDialog(Win) != true) return;
            string path = dlg.FileName.EndsWith(Core.Profiles.ProfileStore.Extension, StringComparison.OrdinalIgnoreCase) ? dlg.FileName : dlg.FileName + Core.Profiles.ProfileStore.Extension;
            int apps = 0, settings = 0;
            await AppState.Current.RunBusy(Loc.T("prof.busyExport"), async () =>
            {
                var sys = AppState.Current.Sys;
                var p = await Task.Run(() =>
                {
                    var installed = Core.Apps.Winget.IsAvailable ? Core.Apps.Winget.GetInstalled() : new System.Collections.Generic.Dictionary<string, string>();
                    return Core.Profiles.ProfileStore.Capture(sys, Environment.MachineName, installed);
                });
                Core.Profiles.ProfileStore.Save(p, path);
                apps = p.Apps?.Count ?? 0; settings = p.SettingsCount;
            });
            Win.ShowToast(Loc.F("prof.exported", settings, apps), ToastKind.Success);
        }

        async void Import_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = Loc.T("prof.filter") + "|*.json", CheckFileExists = true };
            if (dlg.ShowDialog(Win) != true) return;
            Core.Profiles.VeloxProfile p;
            try { p = Core.Profiles.ProfileStore.Load(dlg.FileName); }
            catch (Exception ex) { await Win.ConfirmAsync(Loc.T("prof.import"), ex.Message, null, Loc.T("ui.btn.ok"), null); return; }
            await AppState.Current.ImportProfileAsync(p);
            RenderProfiles();
        }

        async void Wizard_Click(object sender, RoutedEventArgs e)
        {
            await Wizard.RunAsync(Win);
            RenderProfiles();
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
        void ViewHistory_Click(object sender, RoutedEventArgs e) => Win.Navigate("history");
        void OpenData_Click(object sender, RoutedEventArgs e) => Process.Start("explorer.exe", "\"" + AppPaths.DataDir + "\"");
        void GitHub_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo(AppInfo.RepositoryUrl) { UseShellExecute = true });
    }
}
