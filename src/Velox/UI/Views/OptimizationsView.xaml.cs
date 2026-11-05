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
            AppState.Current.PropertyChanged += OnStateChanged;
            Update();
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

        void Expand_Click(object sender, RoutedEventArgs e)
        {
            bool expand = !Vm.Categories.All(c => c.IsExpanded);
            Vm.SetAllExpanded(expand);
            Update();
        }
    }
}
