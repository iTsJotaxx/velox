using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Velox.Core;
using Velox.Core.Apps;
using Velox.Core.Disk;
using Velox.Localization;
using Velox.UI.Infra;

namespace Velox.UI.Views
{
    public sealed class ProgramRowVM : ObservableObject
    {
        static readonly Color[] Palette =
        {
            Color.FromRgb(0x4C, 0x86, 0xF7), Color.FromRgb(0xA8, 0x6B, 0xF7), Color.FromRgb(0x1F, 0xB8, 0xA9), Color.FromRgb(0xF0, 0x52, 0x5C),
            Color.FromRgb(0xF5, 0x9E, 0x24), Color.FromRgb(0x34, 0xC7, 0x6F), Color.FromRgb(0xE9, 0x5C, 0xB0), Color.FromRgb(0xF7, 0x7F, 0x3B),
        };

        bool _checked, _visible = true, _busy;
        ImageSource _icon;
        string _status, _statusKind;

        public InstalledProgram Program { get; }
        public ProgramRowVM(InstalledProgram p)
        {
            Program = p;
            int h = 0;
            foreach (char c in p.Name ?? "") h = h * 31 + c;
            var b = new SolidColorBrush(Palette[Math.Abs(h % Palette.Length)]);
            b.Freeze();
            TileBrush = b;
        }

        public string Name => Program.Name;
        public string Letter => string.IsNullOrEmpty(Program.Name) ? "?" : Program.Name.Substring(0, 1).ToUpperInvariant();
        public Brush TileBrush { get; }
        public string Sub
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(Program.Publisher)) parts.Add(Program.Publisher);
                if (!string.IsNullOrEmpty(Program.Version)) parts.Add(Program.Version);
                return string.Join("  ·  ", parts);
            }
        }
        public string SizeText => Program.Size > 0 ? Format.Bytes(Program.Size) : "";
        public string DateText => Program.InstallDate.HasValue ? Program.InstallDate.Value.ToString("d") : "";
        public string Tip => Program.IsProtected ? Program.ProtectReason : Program.Kind == ProgramKind.Store ? Program.Key : (Program.QuietUninstallString.Length > 0 ? Program.QuietUninstallString : Program.UninstallString);
        public bool IsProtected => Program.IsProtected;
        public string ProtectReason => Program.ProtectReason;
        /// <summary>Etiqueta «Silenciosa»: solo en programas de escritorio (en la Store lo son todas).</summary>
        public bool IsQuiet => Program.IsQuiet && Program.Kind == ProgramKind.Desktop;

        public ImageSource Icon { get => _icon; set { if (Set(ref _icon, value)) Raise(nameof(NoIcon)); } }
        public bool NoIcon => _icon == null;

        public bool IsChecked { get => _checked; set { if (Set(ref _checked, value)) Changed?.Invoke(); } }
        public bool IsVisible { get => _visible; set => Set(ref _visible, value); }
        public bool IsBusy { get => _busy; set { if (Set(ref _busy, value)) Raise(nameof(CanSelect)); } }
        public bool CanSelect => !_busy && !Program.IsProtected;

        public string Status => _status;
        public string StatusKind => _statusKind;
        public bool ShowStatus => !string.IsNullOrEmpty(_status);
        public void SetStatus(string text, string kind) { _status = text; _statusKind = kind; Raise(nameof(Status)); Raise(nameof(StatusKind)); Raise(nameof(ShowStatus)); }

        public event Action Changed;
        public void RefreshTexts() { Raise(nameof(DateText)); Raise(nameof(SizeText)); }
    }

    /// <summary>Desinstalador sencillo: busca, marca y desinstala (escritorio y Microsoft Store), con limpieza de restos.</summary>
    public partial class UninstallView : UserControl, IPage
    {
        List<ProgramRowVM> _desktop, _store;
        bool _loading;
        CancellationTokenSource _cts;

        public UninstallView()
        {
            InitializeComponent();
            AppState.Current.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(AppState.IsBusy)) UpdateButtons(); };
            UpdateButtons();
        }

        MainWindow Win => (MainWindow)Window.GetWindow(this);
        bool StoreMode => RbStore.IsChecked == true;
        List<ProgramRowVM> Current => (StoreMode ? _store : _desktop) ?? new List<ProgramRowVM>();

        public void OnShown()
        {
            if (_desktop == null) Load(false);
        }

        public void RefreshTexts()
        {
            foreach (var r in (_desktop ?? new List<ProgramRowVM>()).Concat(_store ?? new List<ProgramRowVM>())) r.RefreshTexts();
            Render();
        }

        // ------------------------------------------------------------ Cargar

        async void Load(bool store)
        {
            if (_loading) return;
            _loading = true;
            TxtEmpty.Text = Loc.T("unin.loading");
            TxtEmpty.Visibility = Visibility.Visible;
            List.ItemsSource = null;
            try
            {
                var progs = await Task.Run(() => store ? Uninstaller.GetStore() : Uninstaller.GetDesktop());
                var rows = progs.Select(p => new ProgramRowVM(p)).ToList();
                foreach (var r in rows) r.Changed += UpdateButtons;
                if (store) _store = rows; else _desktop = rows;
                Render();
                // Iconos en segundo plano
                if (!store)
                {
                    var pending = rows.Where(r => r.Program.IconPath != null).ToList();
                    await Task.Run(() =>
                    {
                        foreach (var r in pending)
                        {
                            var img = IconLoader.Load(r.Program.IconPath, r.Program.IconIndex);
                            if (img != null) Dispatcher.BeginInvoke(new Action(() => r.Icon = img));
                        }
                    });
                }
            }
            catch (Exception ex) { Log.Error("Installed programs", ex); }
            finally { _loading = false; }
            Render();
        }

        void Kind_Click(object sender, RoutedEventArgs e)
        {
            if (StoreMode && _store == null) Load(true);
            else Render();
        }

        void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (StoreMode) _store = null; else _desktop = null;
            Load(StoreMode);
        }

        void Sort_Click(object sender, RoutedEventArgs e) => Render();

        void Filter_Changed(object sender, TextChangedEventArgs e)
        {
            SearchHint.Visibility = string.IsNullOrEmpty(TxtFilter.Text) ? Visibility.Visible : Visibility.Collapsed;
            Render();
        }

        void Render()
        {
            var rows = Current;
            string q = (TxtFilter.Text ?? "").Trim();
            foreach (var r in rows)
                r.IsVisible = q.Length == 0 || r.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0
                              || (r.Program.Publisher ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0;
            IEnumerable<ProgramRowVM> sorted =
                RbSize.IsChecked == true ? rows.OrderByDescending(r => r.Program.Size).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                : RbDate.IsChecked == true ? rows.OrderByDescending(r => r.Program.InstallDate ?? DateTime.MinValue).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                : rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase);
            var visible = sorted.Where(r => r.IsVisible).ToList();
            List.ItemsSource = visible;

            bool loaded = StoreMode ? _store != null : _desktop != null;
            TxtEmpty.Visibility = loaded && visible.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            TxtEmpty.Text = !loaded ? Loc.T("unin.loading") : Loc.T("unin.none");
            long total = rows.Sum(r => r.Program.Size);
            TxtTotal.Text = loaded ? (total > 0 ? Loc.F("unin.totalSize", rows.Count, Format.Bytes(total)) : Loc.F("unin.total", rows.Count)) : "";
            UpdateButtons();
        }

        void UpdateButtons()
        {
            int n = (_desktop ?? new List<ProgramRowVM>()).Concat(_store ?? new List<ProgramRowVM>()).Count(r => r.IsChecked);
            BtnUninstall.Content = n == 0 ? Loc.T("unin.uninstallNone") : Loc.F("unin.uninstallN", n);
            bool idle = AppState.Current.IsIdle;
            BtnUninstall.IsEnabled = idle && n > 0;
            BtnRefresh.IsEnabled = idle && !_loading;
        }

        // ------------------------------------------------------------ Desinstalar

        async void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            var sel = (_desktop ?? new List<ProgramRowVM>()).Concat(_store ?? new List<ProgramRowVM>()).Where(r => r.IsChecked).ToList();
            if (sel.Count == 0) return;
            var lines = sel.Select(r => "–  " + r.Name + (r.IsQuiet ? "" : "   (" + Loc.T("unin.line.interactive") + ")"));
            string text = Loc.T("unin.confirmText");

            if (!await Win.ConfirmAsync(Loc.F("unin.confirmTitle", sel.Count), text, string.Join("\n", lines), Loc.T("unin.confirmOk"), Loc.T("ui.btn.cancel"), true))
                return;

            foreach (var r in sel) { r.IsBusy = true; r.SetStatus(Loc.T("apps.badge.queued"), "queued"); }
            _cts = new CancellationTokenSource();
            ProgressCard.Visibility = Visibility.Visible;
            BtnStop.IsEnabled = true;
            ProgressAll.Maximum = sel.Count;
            ProgressAll.Value = 0;

            int removed = 0, kept = 0, failed = 0;
            var errors = new List<string>();
            var leftovers = new List<UninstallResult>();
            await AppState.Current.RunBusy(Loc.T("unin.busy"), async () =>
            {
                for (int i = 0; i < sel.Count; i++)
                {
                    var r = sel[i];
                    if (_cts.IsCancellationRequested) { r.SetStatus(Loc.T("apps.badge.skipped"), "queued"); continue; }
                    TxtProgressTitle.Text = Loc.F("apps.progressTitle", i + 1, sel.Count, r.Name);
                    AppState.Current.SetBusyText(Loc.F("apps.progressTitle", i + 1, sel.Count, r.Name));
                    Action<string> status = s => Dispatcher.BeginInvoke(new Action(() => { r.SetStatus(s, "busy"); TxtProgressDetail.Text = s; }));
                    status(Loc.T("unin.status.removing"));
                    var res = await Task.Run(() => Uninstaller.Uninstall(r.Program, status, _cts.Token));
                    switch (res.Outcome)
                    {
                        case UninstallOutcome.Removed:
                            removed++; r.SetStatus(Loc.T("unin.status.removed"), "ok");
                            if (res.Leftover != null) leftovers.Add(res);
                            break;
                        case UninstallOutcome.NotRemoved:
                            kept++; r.SetStatus(Loc.T("unin.status.kept"), "queued");
                            break;
                        default:
                            failed++; errors.Add(r.Name + ": " + res.Error); r.SetStatus(Loc.T("apps.badge.failed"), "error");
                            break;
                    }
                    ProgressAll.Value = i + 1;
                }
            });

            ProgressCard.Visibility = Visibility.Collapsed;
            _cts.Dispose(); _cts = null;
            foreach (var r in sel) { r.IsBusy = false; r.IsChecked = false; }

            string summary = Loc.F("unin.summary", removed, kept, failed);
            if (failed > 0) await Win.ConfirmAsync(Loc.T("unin.summaryErrors"), summary, string.Join("\n", errors), Loc.T("ui.btn.ok"), null);
            else Win.ShowToast(summary, removed > 0 ? ToastKind.Success : ToastKind.Info);

            // Restos: carpetas de instalación que han quedado
            if (leftovers.Count > 0)
            {
                long total = leftovers.Sum(l => l.LeftoverSize);
                var detail = string.Join("\n", leftovers.Select(l => l.Leftover + "  (" + Format.Bytes(l.LeftoverSize) + ")"));
                if (await Win.ConfirmAsync(Loc.T("unin.leftTitle"), Loc.F("unin.leftText", Format.Bytes(total)), detail, Loc.T("disk.menu.recycle"), Loc.T("unin.leftKeep")))
                {
                    int ok = 0;
                    foreach (var l in leftovers)
                    {
                        try { await Task.Run(() => Uninstaller.RecycleLeftover(l.Leftover)); ok++; }
                        catch (Exception ex) { Log.Warn("Leftover " + l.Leftover + ": " + ex.Message); }
                    }
                    Win.ShowToast(Loc.F("unin.leftDone", ok, Format.Bytes(total)), ToastKind.Success);
                }
            }

            // Lista actualizada (lo desinstalado desaparece)
            var removedRows = sel.Where(r => r.StatusKind == "ok").ToList();
            if (_desktop != null) _desktop = _desktop.Except(removedRows).ToList();
            if (_store != null) _store = _store.Except(removedRows).ToList();
            Render();
        }

        void Stop_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            BtnStop.IsEnabled = false;
            TxtProgressDetail.Text = Loc.T("apps.stopping");
        }

#if UITEST
        public void TestSelect(params string[] names)
        {
            foreach (var r in (_desktop ?? new List<ProgramRowVM>()).Concat(_store ?? new List<ProgramRowVM>()))
                if (r.CanSelect && names.Any(n => r.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)) r.IsChecked = true;
        }
        public void TestStore() { RbStore.IsChecked = true; Kind_Click(this, new RoutedEventArgs()); }
        public void TestUninstall() => Uninstall_Click(this, new RoutedEventArgs());
#endif
    }
}
