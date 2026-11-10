using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Velox.Core;
using Velox.Core.Apps;
using Velox.Localization;
using Velox.UI.Infra;

namespace Velox.UI.Views
{
    public sealed class AppRowVM : ObservableObject
    {
        static readonly Color[] Palette =
        {
            Color.FromRgb(0x4C, 0x86, 0xF7), Color.FromRgb(0xA8, 0x6B, 0xF7), Color.FromRgb(0x1F, 0xB8, 0xA9), Color.FromRgb(0xF0, 0x52, 0x5C),
            Color.FromRgb(0xF5, 0x9E, 0x24), Color.FromRgb(0x34, 0xC7, 0x6F), Color.FromRgb(0xE9, 0x5C, 0xB0), Color.FromRgb(0xF7, 0x7F, 0x3B),
        };

        bool _checked, _visible = true, _busy;
        string _installedVersion, _badge, _badgeKind;

        public AppEntry App { get; }
        public AppRowVM(AppEntry app)
        {
            App = app;
            int h = 0;
            foreach (char c in app.Id ?? "") h = h * 31 + c;
            var b = new SolidColorBrush(Palette[Math.Abs(h % Palette.Length)]);
            b.Freeze();
            TileBrush = b;
        }

        AppVariant _variant;
        /// <summary>Región elegida (solo apps con Variants, como Riot Client).</summary>
        public AppVariant Variant { get => _variant; set { if (Set(ref _variant, value)) { Raise(nameof(Name)); Raise(nameof(Tip)); } } }
        public string InstallId => _variant?.Id ?? App.Id;

        public string Name => App.Name + (_variant != null ? " · " + _variant.Short : "");
        public string Letter => string.IsNullOrEmpty(App.Name) ? "?" : App.Name.TrimStart('.').Substring(0, 1).ToUpperInvariant();
        public Brush TileBrush { get; }
        public string Tip => InstallId + (IsInstalled ? "  ·  " + Loc.F("apps.tip.installed", _installedVersion) : "");

        public bool IsChecked { get => _checked; set { if (Set(ref _checked, value)) Changed?.Invoke(); } }
        public bool IsVisible { get => _visible; set => Set(ref _visible, value); }
        public bool IsBusy { get => _busy; set { if (Set(ref _busy, value)) Raise(nameof(CanSelect)); } }
        public bool CanSelect => !_busy;

        public string InstalledVersion
        {
            get => _installedVersion;
            set { if (Set(ref _installedVersion, value)) { Raise(nameof(IsInstalled)); Raise(nameof(Tip)); if (_badgeKind == null || _badgeKind == "installed") SetInstalledBadge(); } }
        }
        public bool IsInstalled => _installedVersion != null;

        public string BadgeText => _badge;
        public string BadgeKind => _badgeKind;
        public bool ShowBadge => !string.IsNullOrEmpty(_badge);

        public void SetBadge(string text, string kind)
        {
            _badge = text; _badgeKind = kind;
            Raise(nameof(BadgeText)); Raise(nameof(BadgeKind)); Raise(nameof(ShowBadge));
        }

        public void SetInstalledBadge() => SetBadge(IsInstalled ? Loc.T("apps.badge.installed") : null, IsInstalled ? "installed" : null);

        public event Action Changed;
        public void RefreshTexts() { Raise(nameof(Name)); Raise(nameof(Tip)); if (_badgeKind == "installed" || _badgeKind == null) SetInstalledBadge(); }
    }

    public sealed class AppCategoryVM : ObservableObject
    {
        public AppCategory Category { get; set; }
        public ObservableCollection<AppRowVM> Items { get; } = new ObservableCollection<AppRowVM>();
        public string Icon => Category.Icon;
        public string Name => Category.Name;
        public bool HasVisible => Items.Any(i => i.IsVisible);
        public string CountText => Loc.F("apps.count", Items.Count(i => i.IsChecked), Items.Count);
        public void Refresh() { Raise(nameof(HasVisible)); Raise(nameof(CountText)); Raise(nameof(Name)); }
    }

    /// <summary>Instalar aplicaciones al estilo Ninite: marcas las que quieras y se instalan (o actualizan) todas solas.</summary>
    public partial class AppsView : UserControl, IPage
    {
        readonly List<AppRowVM> _rows = new List<AppRowVM>();
        readonly List<AppCategoryVM> _cats = new List<AppCategoryVM>();
        List<AppEntry> _custom = new List<AppEntry>();
        Dictionary<string, string> _installed;
        CancellationTokenSource _cts;
        bool _loadingInstalled;

        public AppsView()
        {
            InitializeComponent();
            _custom = AppCatalog.LoadCustom();
            Build();
            AppState.Current.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(AppState.IsBusy)) UpdateButtons(); };
            UpdateButtons();
        }

        MainWindow Win => (MainWindow)Window.GetWindow(this);

        /// <summary>Apps de un perfil importado que faltan: se marcan al abrir la página (no se instala nada solo).</summary>
        public static List<Core.Profiles.ProfileApp> PendingImport { get; set; }

        public void OnShown()
        {
            NoWingetBanner.Visibility = Winget.IsAvailable ? Visibility.Collapsed : Visibility.Visible;
            if (_installed == null) RefreshInstalled();
            if (PendingImport != null) { var list = PendingImport; PendingImport = null; SelectImported(list); }
        }

        /// <summary>
        /// Marca las apps de un perfil. Las que no están en el catálogo se añaden a «Mis aplicaciones» (con su id de winget
        /// ya validado); las de varias regiones (Riot) se marcan con la región que tenía el perfil.
        /// </summary>
        void SelectImported(List<Core.Profiles.ProfileApp> apps)
        {
            bool added = false;
            foreach (var a in apps)
            {
                if (!AppCatalog.IsValidId(a.Id)) continue;
                if (!_rows.Any(r => string.Equals(r.App.Id, a.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    _custom.Add(new AppEntry { Id = a.Id, Name = string.IsNullOrWhiteSpace(a.Name) ? a.Id : a.Name.Trim(), Category = AppCatalog.CustomCategory, Source = a.Source == "msstore" ? "msstore" : null, IsCustom = true });
                    added = true;
                }
            }
            if (added) { AppCatalog.SaveCustom(_custom); Build(); RenderMine(); }
            foreach (var a in apps)
            {
                var row = _rows.FirstOrDefault(r => string.Equals(r.App.Id, a.Id, StringComparison.OrdinalIgnoreCase));
                if (row == null || !row.CanSelect) continue;
                if (row.App.Variants != null)
                {
                    var v = row.App.Variants.FirstOrDefault(x => string.Equals(x.Id, a.Variant, StringComparison.OrdinalIgnoreCase));
                    if (v == null) continue; // sin región válida no se puede instalar
                    row.Variant = v;
                }
                row.IsChecked = true;
            }
            UpdateButtons();
        }

        public void RefreshTexts()
        {
            foreach (var r in _rows) r.RefreshTexts();
            foreach (var c in _cats) c.Refresh();
            UpdateButtons();
            UpdateInstalledState();
            RenderUpgrades();
        }

        // ------------------------------------------------------------ Catálogo

        void Build()
        {
            var keep = new HashSet<string>(_rows.Where(r => r.IsChecked).Select(r => r.App.Id), StringComparer.OrdinalIgnoreCase);
            _rows.Clear();
            _cats.Clear();
            var all = AppCatalog.BuiltIn.Concat(_custom.Where(c => !AppCatalog.BuiltIn.Any(b => string.Equals(b.Id, c.Id, StringComparison.OrdinalIgnoreCase))));
            foreach (var cat in AppCatalog.Categories)
            {
                var vm = new AppCategoryVM { Category = cat };
                foreach (var a in all.Where(x => x.Category == cat.Id))
                {
                    var row = new AppRowVM(a);
                    ApplyInstalled(row);
                    row.IsChecked = keep.Contains(a.Id) && (a.Variants == null || row.Variant != null);
                    row.Changed += () => { vm.Refresh(); UpdateButtons(); AskRegionIfNeeded(row); };
                    vm.Items.Add(row);
                    _rows.Add(row);
                }
                if (vm.Items.Count > 0) _cats.Add(vm);
            }

            // Tres columnas equilibradas: cada categoría va a la que tenga menos aplicaciones
            var cols = new[] { new List<AppCategoryVM>(), new List<AppCategoryVM>(), new List<AppCategoryVM>() };
            var load = new int[3];
            foreach (var c in _cats)
            {
                int i = Array.IndexOf(load, load.Min());
                cols[i].Add(c);
                load[i] += c.Items.Count + 2;
            }
            Col0.ItemsSource = cols[0]; Col1.ItemsSource = cols[1]; Col2.ItemsSource = cols[2];
            ApplyFilter();
            UpdateButtons();
        }

        void Filter_Changed(object sender, TextChangedEventArgs e)
        {
            SearchHint.Visibility = string.IsNullOrEmpty(TxtFilter.Text) ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilter();
        }

        void ApplyFilter()
        {
            string q = (TxtFilter.Text ?? "").Trim();
            foreach (var r in _rows)
                r.IsVisible = q.Length == 0 || r.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 || r.App.Id.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
            foreach (var c in _cats) c.Refresh();
        }

        void SelectInstalled_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in _rows) if (r.IsInstalled && r.CanSelect) r.IsChecked = true;
        }

        void Clear_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in _rows) if (r.CanSelect) r.IsChecked = false;
        }

        void UpdateButtons()
        {
            var sel = _rows.Where(r => r.IsChecked).ToList();
            int upd = sel.Count(r => r.IsInstalled);
            BtnInstall.Content = sel.Count == 0 ? Loc.T("apps.installNone")
                : upd == 0 ? Loc.F("apps.installN", sel.Count)
                : upd == sel.Count ? Loc.F("apps.updateN", sel.Count)
                : Loc.F("apps.installUpdateN", sel.Count);
            bool idle = AppState.Current.IsIdle;
            BtnInstall.IsEnabled = idle && sel.Count > 0 && Winget.IsAvailable;
            BtnAdd.IsEnabled = idle;
            int nUpd = _upgradeChecks.Values.Count(c => c.IsChecked == true);
            BtnUpdate.Content = nUpd == 0 ? Loc.T("upd.choose") : Loc.F("upd.updateN", nUpd);
            BtnUpdate.IsEnabled = idle && Winget.IsAvailable;
            foreach (var c in _upgradeChecks.Values) c.IsEnabled = idle;
        }

        // ------------------------------------------------------------ Lo que ya está instalado

        void RefreshInstalled()
        {
            if (_loadingInstalled || !Winget.IsAvailable) { UpdateInstalledState(); return; }
            _loadingInstalled = true;
            UpdateInstalledState();
            Task.Run(() => Winget.GetInstalled()).ContinueWith(t =>
            {
                _loadingInstalled = false;
                _installed = t.IsFaulted ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : t.Result;
                foreach (var r in _rows) ApplyInstalled(r);
                if (_upgrades == null) RefreshUpgrades(); // después de lo instalado: winget no admite dos consultas a la vez
                UpdateInstalledState();
                UpdateButtons();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ------------------------------------------------------------ Actualizaciones de lo instalado

        List<WingetUpgrade> _upgrades;
        readonly Dictionary<string, CheckBox> _upgradeChecks = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        bool _loadingUpgrades, _upgradesOpen;

        void RefreshUpgrades()
        {
            if (_loadingUpgrades || !Winget.IsAvailable) return;
            _loadingUpgrades = true;
            RenderUpgrades();
            Task.Run(() => Winget.GetUpgrades()).ContinueWith(t =>
            {
                _loadingUpgrades = false;
                _upgrades = t.IsFaulted ? new List<WingetUpgrade>() : t.Result;
                RenderUpgrades();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void RenderUpgrades()
        {
            if (_loadingUpgrades && _upgrades == null)
            {
                UpdatesCard.Visibility = Visibility.Visible;
                TxtUpdatesTitle.Text = Loc.T("upd.checking");
                TxtUpdatesText.Text = "";
                BtnUpdate.Visibility = BtnUpdatesToggle.Visibility = Visibility.Collapsed;
                UpdatesList.Visibility = Visibility.Collapsed;
                return;
            }
            var list = (_upgrades ?? new List<WingetUpgrade>()).Where(u => !u.Excluded).ToList();
            if (list.Count == 0) { UpdatesCard.Visibility = Visibility.Collapsed; return; }
            UpdatesCard.Visibility = Visibility.Visible;
            TxtUpdatesTitle.Text = list.Count == 1 ? Loc.T("upd.title1") : Loc.F("upd.title", list.Count);
            TxtUpdatesText.Text = list.Any(u => u.IsRuntime) ? Loc.T("upd.textRuntime") : Loc.T("upd.text");
            BtnUpdatesToggle.Visibility = Visibility.Visible;
            BtnUpdatesToggle.Content = _upgradesOpen ? Loc.T("upd.hide") : Loc.T("upd.show");

            // Se conserva lo que la persona haya marcado o desmarcado
            var keep = _upgradeChecks.ToDictionary(kv => kv.Key, kv => kv.Value.IsChecked == true, StringComparer.OrdinalIgnoreCase);
            _upgradeChecks.Clear();
            UpdatesList.Children.Clear();
            foreach (var u in list)
            {
                var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var cb = new CheckBox { Style = (Style)FindResource("Check"), IsChecked = keep.TryGetValue(u.Id, out bool k) ? k : u.SelectedByDefault, VerticalAlignment = VerticalAlignment.Center, Tag = u };
                var label = new StackPanel { Orientation = Orientation.Horizontal };
                label.Children.Add(new TextBlock { Text = u.Name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                if (u.IsRuntime)
                    label.Children.Add(new Border { Style = (Style)FindResource("Badge"), Background = (Brush)FindResource("CardInner"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock { Text = Loc.T("upd.runtime"), Style = (Style)FindResource("BadgeText"), Foreground = (Brush)FindResource("TextMuted") } });
                cb.Content = label;
                cb.Checked += (s, e) => UpdateButtons();
                cb.Unchecked += (s, e) => UpdateButtons();
                System.Windows.Automation.AutomationProperties.SetName(cb, u.Name + " " + u.Version + " → " + u.Available);
                g.Children.Add(cb);
                var ver = new TextBlock { Text = u.Version + "  →  " + u.Available, Style = (Style)FindResource("Figure"), FontWeight = FontWeights.Normal, FontSize = 13, Foreground = (Brush)FindResource("TextMuted"), VerticalAlignment = VerticalAlignment.Center, ToolTip = u.Id };
                Grid.SetColumn(ver, 1); g.Children.Add(ver);
                UpdatesList.Children.Add(g);
                _upgradeChecks[u.Id] = cb;
            }
            UpdatesList.Visibility = _upgradesOpen ? Visibility.Visible : Visibility.Collapsed;
            BtnUpdate.Visibility = Visibility.Visible;
            UpdateButtons();
        }

        List<WingetUpgrade> SelectedUpgrades() =>
            _upgradeChecks.Values.Where(c => c.IsChecked == true).Select(c => (WingetUpgrade)c.Tag).ToList();

        void UpdatesToggle_Click(object sender, RoutedEventArgs e)
        {
            _upgradesOpen = !_upgradesOpen;
            RenderUpgrades();
        }

        /// <summary>Actualiza las marcadas una tras otra, en silencio, con el mismo motor que Instalar (winget comprueba el SHA-256).</summary>
        async void Update_Click(object sender, RoutedEventArgs e)
        {
            var sel = SelectedUpgrades();
            if (sel.Count == 0) { _upgradesOpen = true; RenderUpgrades(); return; }
            var lines = sel.Select(u => "↻  " + u.Name + "   " + u.Version + " → " + u.Available);
            if (!await Win.ConfirmAsync(Loc.F("upd.confirmTitle", sel.Count), Loc.T("upd.confirmText"), string.Join("\n", lines), Loc.T("upd.confirmOk"), Loc.T("ui.btn.cancel")))
                return;
            int ok = 0, failed = 0; bool reboot = false;
            var errors = new List<string>();
            _cts = new CancellationTokenSource();
            ProgressCard.Visibility = Visibility.Visible;
            BtnStop.IsEnabled = true;
            ProgressAll.Maximum = sel.Count;
            ProgressAll.Value = 0;
            await AppState.Current.RunBusy(Loc.T("upd.busy"), async () =>
            {
                for (int i = 0; i < sel.Count; i++)
                {
                    var u = sel[i];
                    if (_cts.IsCancellationRequested) break;
                    TxtProgressTitle.Text = Loc.F("upd.progress", i + 1, sel.Count, u.Name);
                    TxtProgressDetail.Text = "";
                    AppState.Current.SetBusyText(TxtProgressTitle.Text);
                    Action<string> status = s => Dispatcher.BeginInvoke(new Action(() => TxtProgressDetail.Text = s));
                    var entry = new AppEntry { Id = u.Id, Name = u.Name, Source = string.Equals(u.Source, "msstore", StringComparison.OrdinalIgnoreCase) ? "msstore" : null };
                    AppInstallResult res;
                    try { res = await Task.Run(() => Winget.Install(entry, true, status, _cts.Token)); }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex) { res = new AppInstallResult { Outcome = AppInstallOutcome.Failed, Error = ex.Message }; }
                    if (res.Outcome == AppInstallOutcome.Failed) { failed++; errors.Add(u.Name + ": " + res.Error); }
                    else { ok++; if (res.Outcome == AppInstallOutcome.RebootNeeded) reboot = true; }
                    ProgressAll.Value = i + 1;
                }
            });
            ProgressCard.Visibility = Visibility.Collapsed;
            _cts.Dispose(); _cts = null;
            if (reboot) AppState.Current.NeedsReboot = true;
            string summary = Loc.F("upd.summary", ok, failed);
            if (failed == 0) Win.ShowToast(summary, ToastKind.Success);
            else await Win.ConfirmAsync(Loc.T("apps.summaryTitleErrors"), summary, string.Join("\n", errors), Loc.T("ui.btn.ok"), null);
            // Vuelve a mirar qué queda por actualizar y las versiones instaladas
            _upgrades = null; _upgradeChecks.Clear();
            RefreshUpgrades();
            _installed = null;
            RefreshInstalled();
        }

#if UITEST
        /// <summary>Solo capturas: despliega la lista de actualizaciones.</summary>
        public void TestOpenUpdates() { _upgradesOpen = true; RenderUpgrades(); }
        public bool TestLoadingUpdates => _upgrades == null;
#endif

        /// <summary>Marca la fila como instalada (en apps por región, si está instalada alguna región, esa queda elegida).</summary>
        void ApplyInstalled(AppRowVM r)
        {
            if (_installed == null) return;
            if (r.App.Variants == null)
            {
                r.InstalledVersion = _installed.TryGetValue(r.App.Id, out var v) ? (v ?? "") : null;
                return;
            }
            var hit = r.App.Variants.FirstOrDefault(x => _installed.ContainsKey(x.Id));
            r.InstalledVersion = hit != null ? (_installed[hit.Id] ?? "") : null;
            if (hit != null && r.Variant == null) r.Variant = hit;
        }

        // ------------------------------------------------------------ Región (Riot Client)

        sealed class RegionChoice : ObservableObject
        {
            bool _sel;
            public AppVariant Variant { get; set; }
            public string Name => Variant.Name;
            public bool IsSelected { get => _sel; set => Set(ref _sel, value); }
            /// <summary>La que corresponde al país de Windows.</summary>
            public bool IsRecommended { get; set; }
        }

        AppRowVM _regionRow;

        /// <summary>Al marcar una app por región sin región elegida, se pide confirmarla antes.</summary>
        void AskRegionIfNeeded(AppRowVM row)
        {
            if (row.App.Variants == null || !row.IsChecked || row.Variant != null || _regionRow != null) return;
            _regionRow = row;
            // Preseleccionada: la del país configurado en Windows (Configuración > Hora e idioma > Región)
            string iso = RegionDetect.CountryIso2();
            string rec = RegionDetect.Suggest(row.App.Id, iso);
            var choices = row.App.Variants.Select(v => new RegionChoice { Variant = v, IsRecommended = v.Code == rec, IsSelected = v.Code == rec }).ToList();
            if (!choices.Any(c => c.IsSelected)) choices[0].IsSelected = true;
            RegionList.ItemsSource = choices;
            TxtRegionTitle.Text = Loc.F("apps.region.title", row.App.Name);
            TxtRegionText.Text = rec != null
                ? Loc.F("apps.region.detected", RegionDetect.CountryName(iso), row.App.Variants.First(v => v.Code == rec).Name)
                : Loc.T("apps.region.unknown");
            Log.Info("Region picker for " + row.App.Name + ": Windows country " + (iso ?? "?") + " -> " + (rec ?? "no suggestion") + ".");
            RegionLayer.Visibility = Visibility.Visible;
        }

        void RegionOk_Click(object sender, RoutedEventArgs e)
        {
            var row = _regionRow;
            var choice = (RegionList.ItemsSource as IEnumerable<RegionChoice>)?.FirstOrDefault(c => c.IsSelected);
            if (row == null || choice == null) return;
            RegionLayer.Visibility = Visibility.Collapsed;
            _regionRow = null;
            row.Variant = choice.Variant;
            Log.Info("Region chosen for " + row.App.Name + ": " + choice.Variant.Code + ".");
        }

        void RegionCancel_Click(object sender, RoutedEventArgs e)
        {
            var row = _regionRow;
            RegionLayer.Visibility = Visibility.Collapsed;
            _regionRow = null;
            if (row != null && row.Variant == null) row.IsChecked = false; // sin región no se puede instalar
        }

        void UpdateInstalledState()
        {
            if (!Winget.IsAvailable) TxtInstalledState.Text = "";
            else if (_loadingInstalled || _installed == null) TxtInstalledState.Text = Loc.T("apps.checkingInstalled");
            else TxtInstalledState.Text = Loc.F("apps.installedCount", _rows.Count(r => r.IsInstalled));
        }

        void GetWinget_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo("ms-windows-store://pdp/?productid=9NBLGGH4NNS1") { UseShellExecute = true }); }
            catch (Exception ex) { Win.ShowToast(ex.Message, ToastKind.Error); }
        }

        // ------------------------------------------------------------ Instalar / actualizar

        async void Install_Click(object sender, RoutedEventArgs e)
        {
            var sel = _rows.Where(r => r.IsChecked).ToList();
            if (sel.Count == 0) return;
            var lines = sel.Select(r => (r.IsInstalled ? "↻  " + Loc.T("apps.line.update") : "+  " + Loc.T("apps.line.install")) + "  " + r.Name);
            if (!await Win.ConfirmAsync(Loc.F("apps.confirmTitle", sel.Count), Loc.T("apps.confirmText"), string.Join("\n", lines), Loc.T("apps.confirmOk"), Loc.T("ui.btn.cancel")))
                return;

            foreach (var r in sel) { r.IsBusy = true; r.SetBadge(Loc.T("apps.badge.queued"), "queued"); }
            _cts = new CancellationTokenSource();
            ProgressCard.Visibility = Visibility.Visible;
            BtnStop.IsEnabled = true;
            ProgressAll.Maximum = sel.Count;
            ProgressAll.Value = 0;

            int ok = 0, upToDate = 0, failed = 0; bool reboot = false;
            var errors = new List<string>();
            await AppState.Current.RunBusy(Loc.T("apps.busy"), async () =>
            {
                for (int i = 0; i < sel.Count; i++)
                {
                    var r = sel[i];
                    if (_cts.IsCancellationRequested)
                    {
                        r.SetBadge(Loc.T("apps.badge.skipped"), "queued");
                        continue;
                    }
                    TxtProgressTitle.Text = Loc.F("apps.progressTitle", i + 1, sel.Count, r.Name);
                    TxtProgressDetail.Text = "";
                    AppState.Current.SetBusyText(Loc.F("apps.progressTitle", i + 1, sel.Count, r.Name));
                    r.SetBadge(Loc.T("apps.status.preparing"), "busy");

                    Action<string> status = s => Dispatcher.BeginInvoke(new Action(() => { r.SetBadge(s, "busy"); TxtProgressDetail.Text = s; }));
                    AppInstallResult res;
                    try
                    {
                        bool was = r.IsInstalled;
                        string variant = r.App.Variants != null ? r.Variant?.Id : null;
                        res = await Task.Run(() => Winget.Install(r.App, was, status, _cts.Token, variant));
                    }
                    catch (OperationCanceledException) { r.SetBadge(Loc.T("apps.badge.skipped"), "queued"); continue; }
                    catch (Exception ex) { res = new AppInstallResult { Outcome = AppInstallOutcome.Failed, Error = ex.Message }; }

                    switch (res.Outcome)
                    {
                        case AppInstallOutcome.Installed: ok++; r.SetBadge(Loc.T("apps.badge.installedNow"), "installed"); break;
                        case AppInstallOutcome.Updated: ok++; r.SetBadge(Loc.T("apps.badge.updated"), "installed"); break;
                        case AppInstallOutcome.UpToDate: upToDate++; r.SetBadge(Loc.T("apps.badge.upToDate"), "installed"); break;
                        case AppInstallOutcome.RebootNeeded: ok++; reboot = true; r.SetBadge(Loc.T("apps.badge.reboot"), "installed"); break;
                        default: failed++; errors.Add(r.Name + ": " + res.Error); r.SetBadge(Loc.T("apps.badge.failed"), "error"); break;
                    }
                    ProgressAll.Value = i + 1;
                }
            });

            ProgressCard.Visibility = Visibility.Collapsed;
            foreach (var r in sel) { r.IsBusy = false; if (r.BadgeKind != "error") r.IsChecked = false; }
            _cts.Dispose(); _cts = null;
            if (reboot) AppState.Current.NeedsReboot = true;

            string summary = Loc.F("apps.summary", ok, upToDate, failed);
            if (failed == 0) Win.ShowToast(summary, ToastKind.Success);
            else await Win.ConfirmAsync(Loc.T("apps.summaryTitleErrors"), summary, string.Join("\n", errors), Loc.T("ui.btn.ok"), null);

            // Vuelve a leer lo instalado (versiones nuevas) sin borrar los avisos de error de esta tanda
            var keepErrors = sel.Where(r => r.BadgeKind == "error").ToList();
            _installed = null;
            RefreshInstalled();
            await Task.Delay(100);
            foreach (var r in keepErrors) r.SetBadge(Loc.T("apps.badge.failed"), "error");
        }

        void Stop_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            BtnStop.IsEnabled = false;
            TxtProgressDetail.Text = Loc.T("apps.stopping");
        }

        // ------------------------------------------------------------ Añadir aplicaciones propias

        void OpenAdd_Click(object sender, RoutedEventArgs e)
        {
            AddLayer.Visibility = Visibility.Visible;
            Hits.ItemsSource = null;
            TxtSearchState.Text = Winget.IsAvailable ? "" : Loc.T("apps.err.noWinget");
            RenderMine();
            TxtSearch.Focus();
        }

        void CloseAdd_Click(object sender, RoutedEventArgs e) => AddLayer.Visibility = Visibility.Collapsed;

        void Search_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { Search_Click(sender, e); e.Handled = true; }
            else if (e.Key == Key.Escape) { AddLayer.Visibility = Visibility.Collapsed; e.Handled = true; }
        }

        async void Search_Click(object sender, RoutedEventArgs e)
        {
            string q = TxtSearch.Text?.Trim() ?? "";
            if (q.Length < 2 || !Winget.IsAvailable) return;
            BtnSearch.IsEnabled = false;
            TxtSearchState.Text = Loc.T("apps.add.searching");
            Hits.ItemsSource = null;
            List<WingetSearchHit> hits;
            try { hits = await Task.Run(() => Winget.Search(q)); }
            catch (Exception ex) { hits = new List<WingetSearchHit>(); Log.Warn("winget search: " + ex.Message); }
            BtnSearch.IsEnabled = true;
            Hits.ItemsSource = hits;
            TxtSearchState.Text = hits.Count == 0 ? Loc.F("apps.add.none", q) : Loc.F("apps.add.found", hits.Count);
        }

        void AddHit_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is WingetSearchHit hit)) return;
            var existing = AppCatalog.BuiltIn.Concat(_custom).FirstOrDefault(a => string.Equals(a.Id, hit.Id, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                Win.ShowToast(Loc.F("apps.add.already", existing.Name), ToastKind.Info);
                SelectRow(existing.Id);
                return;
            }
            _custom.Add(new AppEntry { Id = hit.Id, Name = string.IsNullOrWhiteSpace(hit.Name) ? hit.Id : hit.Name, Category = AppCatalog.CustomCategory, IsCustom = true });
            AppCatalog.SaveCustom(_custom);
            Log.Info("Custom app added: " + hit.Id + ".");
            Build();
            SelectRow(hit.Id);
            RenderMine();
            Win.ShowToast(Loc.F("apps.add.added", hit.Name), ToastKind.Success);
        }

        void SelectRow(string id)
        {
            var row = _rows.FirstOrDefault(r => string.Equals(r.App.Id, id, StringComparison.OrdinalIgnoreCase));
            if (row != null && row.CanSelect) row.IsChecked = true;
        }

        void RemoveMine_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is AppEntry app)) return;
            _custom.RemoveAll(a => string.Equals(a.Id, app.Id, StringComparison.OrdinalIgnoreCase));
            AppCatalog.SaveCustom(_custom);
            Build();
            RenderMine();
        }

        void RenderMine()
        {
            MineList.ItemsSource = null;
            MineList.ItemsSource = _custom.ToList();
            TxtMineEmpty.Visibility = _custom.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

#if UITEST
        public void TestSelect(params string[] ids) { foreach (var id in ids) SelectRow(id); }
        public void TestOpenAdd(string query) { OpenAdd_Click(this, new RoutedEventArgs()); TxtSearch.Text = query; Search_Click(this, new RoutedEventArgs()); }
        public void TestInstall() => Install_Click(this, new RoutedEventArgs());
#endif
    }
}
