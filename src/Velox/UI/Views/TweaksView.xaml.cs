using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Velox.Core;
using Velox.Core.Optimizations;
using Velox.Core.Tools;
using Velox.Core.Tweaks;
using Velox.Localization;
using Velox.UI.Infra;

namespace Velox.UI.Views
{
    public sealed class TweakRowVM : ObservableObject
    {
        bool _on, _available, _busy;
        public Tweak Tweak { get; }
        public TweakRowVM(Tweak t) { Tweak = t; }

        public string Name => Tweak.Name;
        public string Description => Tweak.Description;
        public bool NeedsExplorer => Tweak.NeedsExplorer;
        public bool IsOn { get => _on; set => Set(ref _on, value); }
        public bool IsAvailable { get => _available; set { if (Set(ref _available, value)) { Raise(nameof(CanToggle)); Raise(nameof(ShowUnavailable)); } } }
        public bool IsBusy { get => _busy; set { if (Set(ref _busy, value)) Raise(nameof(CanToggle)); } }
        public bool CanToggle => _available && !_busy;
        public bool ShowUnavailable => !_available;
        public string UnavailableText => Tweak.Win11Only ? Loc.T("tweaks.badge.win11") : Loc.T("tweaks.badge.na");

        public void RefreshTexts() { Raise(nameof(Name)); Raise(nameof(Description)); Raise(nameof(UnavailableText)); }
    }

    public sealed class TweakGroupVM
    {
        public TweakGroup Group { get; set; }
        public string Icon => Group.Icon;
        public string Name => Group.Name;
        public string Description => Group.Description;
        public List<TweakRowVM> Rows { get; set; }
    }

    /// <summary>Ajustes de comodidad de Windows con interruptores que se aplican al momento y se pueden deshacer.</summary>
    public partial class TweaksView : UserControl, IPage
    {
        List<TweakRowVM> _rows = new List<TweakRowVM>();

        public TweaksView()
        {
            InitializeComponent();
            Build();
        }

        MainWindow Win => (MainWindow)Window.GetWindow(this);

        void Build()
        {
            _rows = TweakCatalog.All.Select(t => new TweakRowVM(t)).ToList();
            GroupList.ItemsSource = TweakCatalog.Groups.Select(g => new TweakGroupVM
            {
                Group = g,
                Rows = _rows.Where(r => r.Tweak.Group == g.Id).ToList(),
            }).ToList();
        }

        public void OnShown() => RefreshStates();

        public void RefreshTexts()
        {
            Build();
            RefreshStates();
        }

        void RefreshStates()
        {
            var rows = _rows;
            Task.Run(() => rows.Select(r => new { r, avail = r.Tweak.IsAvailable, on = r.Tweak.IsApplied }).ToList())
                .ContinueWith(t =>
                {
                    if (t.IsFaulted) return;
                    foreach (var x in t.Result) { x.r.IsAvailable = x.avail; x.r.IsOn = x.on; }
                    UpdateCount();
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void UpdateCount()
        {
            TxtCount.Text = Loc.F("tweaks.count", _rows.Count(r => r.IsOn), _rows.Count(r => r.IsAvailable));
        }

        async void Toggle_Click(object sender, RoutedEventArgs e)
        {
            var cb = (CheckBox)sender;
            if (!(cb.DataContext is TweakRowVM row)) return;
            bool enable = cb.IsChecked == true;
            row.IsBusy = true;
            string error = null;
            try
            {
                var opt = row.Tweak.Opt;
                await Task.Run(() =>
                {
                    if (enable) OptimizationEngine.Apply(opt, AppState.Current.Sys);
                    else OptimizationEngine.Revert(opt, AppState.Current.Sys);
                });
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Error("Tweak " + row.Tweak.Opt.Id, ex);
            }
            bool now = await Task.Run(() => row.Tweak.IsApplied);
            row.IsOn = now;
            cb.IsChecked = now; // el binding es de un solo sentido: se deja como está de verdad
            row.IsBusy = false;
            UpdateCount();

            if (error != null) Win.ShowToast(Loc.F("tweaks.failed", row.Name, error), ToastKind.Error);
            else if (now != enable) Win.ShowToast(Loc.F("tweaks.notApplied", row.Name), ToastKind.Warning);
            else
            {
                Win.ShowToast(Loc.F(enable ? "tweaks.enabled" : "tweaks.disabled", row.Name), ToastKind.Success);
                if (row.NeedsExplorer) ExplorerBanner.Visibility = Visibility.Visible;
            }
        }

        async void RestartExplorer_Click(object sender, RoutedEventArgs e)
        {
            bool ok = false;
            BtnExplorer.IsEnabled = false;
            await AppState.Current.RunBusy(Loc.T("ui.tools.explorerBusy"), async () => ok = await Task.Run(() => ExplorerTool.Restart()));
            BtnExplorer.IsEnabled = true;
            if (ok)
            {
                ExplorerBanner.Visibility = Visibility.Collapsed;
                Win.ShowToast(Loc.T("ui.tools.explorerDone"), ToastKind.Success);
            }
            else await Win.ConfirmAsync(Loc.T("ui.tools.explorerTitle"), Loc.T("ui.tools.explorerManual"), null, Loc.T("ui.btn.ok"), null);
        }
    }
}
