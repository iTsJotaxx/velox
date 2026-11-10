using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Velox.Core;
using Velox.Localization;
using Velox.UI.ViewModels;

namespace Velox.UI.Views
{
    public partial class OptimizationsView : UserControl, IPage
    {
        OptimizationsVM Vm => AppState.Current.Optimizations;

        public OptimizationsView()
        {
            InitializeComponent();
            CategoryList.ItemsSource = Vm.Categories;
            ChkRestore.IsChecked = Settings.Current.CreateRestorePoint;
            if (Settings.Current.ExpandAllCategories) Vm.SetAllExpanded(true);
            Vm.Changed += Update;
            Vm.RiskyEnabled += OnRiskyEnabled;
            AppState.Current.PropertyChanged += OnStateChanged;
            Update();
        }

        bool _askingRisk;

        /// <summary>Aviso al activar una optimización con riesgo; si no se confirma, el interruptor vuelve atrás.</summary>
        async void OnRiskyEnabled(OptItemVM item)
        {
            if (_askingRisk) return;
            _askingRisk = true;
            try
            {
                var w = (MainWindow)Window.GetWindow(this);
                if (w == null) return;
                bool ok = await w.ConfirmAsync(Loc.F("ui.risk.title", item.Name), item.RiskText, Loc.T("ui.risk.undo"),
                                               Loc.T("ui.risk.accept"), Loc.T("ui.btn.cancel"), true);
                if (!ok) item.IsChecked = false;
                else Log.Info("Risky optimization selected by the user: " + item.Opt.Id + ".");
            }
            finally { _askingRisk = false; }
        }

        void OnStateChanged(object s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppState.IsBusy)) Update();
        }

        void Update()
        {
            bool idle = AppState.Current.IsIdle;
            BtnApply.Content = Vm.ApplyText;
            BtnApply.IsEnabled = idle && Vm.HasPending;
            BtnDiscard.IsEnabled = idle && Vm.HasPending;
            RbRec.IsEnabled = RbCustom.IsEnabled = idle && Vm.Scanned;
            RbRec.IsChecked = Vm.IsRecommendedMode;
            RbCustom.IsChecked = !Vm.IsRecommendedMode;
            CategoryList.IsEnabled = idle;
            TxtHint.Text = Vm.ModeHint;
            BtnExpand.Content = Vm.Categories.All(c => c.IsExpanded) ? Loc.T("ui.opts.collapseAll") : Loc.T("ui.opts.expandAll");
        }

        public void OnShown() => Update();
        public void RefreshTexts() => Update();

        async void Apply_Click(object sender, RoutedEventArgs e) => await AppState.Current.ApplyPendingAsync();

        void Recommended_Click(object sender, RoutedEventArgs e)
        {
            int n = Vm.SelectRecommended();
            var w = (MainWindow)Window.GetWindow(this);
            if (n > 0) w.ShowToast(Loc.F("ui.opts.recSelected", n));
            else w.ShowToast(Loc.T("ui.opts.alreadyRecommended"), ToastKind.Success);
            Update();
        }

        void Custom_Click(object sender, RoutedEventArgs e)
        {
            Vm.SelectCustom();
            Update();
        }

        void Discard_Click(object sender, RoutedEventArgs e) => Vm.Discard();

        void ChkRestore_Click(object sender, RoutedEventArgs e)
        {
            Settings.Current.CreateRestorePoint = ChkRestore.IsChecked == true;
            Settings.Save();
        }

        void Search_Changed(object sender, TextChangedEventArgs e)
        {
            SearchHint.Visibility = string.IsNullOrEmpty(TxtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
            Vm.ApplyFilter(TxtSearch.Text);
            Update();
        }

#if UITEST
        public void TestExpand(string category)
        {
            foreach (var c in Vm.Categories) c.IsExpanded = c.Cat.Id == category;
            Update();
        }

        public void TestSearch(string text) => TxtSearch.Text = text;

        public void TestToggle(string id)
        {
            foreach (var c in Vm.Categories)
                foreach (var it in c.Items)
                    if (it.Opt.Id == id) { c.IsExpanded = true; it.IsChecked = !it.IsChecked; }
            Update();
        }
#endif

        void Expand_Click(object sender, RoutedEventArgs e)
        {
            bool expand = !Vm.Categories.All(c => c.IsExpanded);
            Vm.SetAllExpanded(expand);
            Update();
        }
    }
}
