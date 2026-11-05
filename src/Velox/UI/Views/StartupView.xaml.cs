using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Velox.Core;
using Velox.Core.Tools;
using Velox.Localization;

namespace Velox.UI.Views
{
    public partial class StartupView : UserControl, IPage
    {
        List<StartupEntry> _items = new List<StartupEntry>();

        public StartupView()
        {
            InitializeComponent();
        }

        public async void OnShown() => await LoadAsync();
        public void RefreshTexts() => UpdateSummary();

        async Task LoadAsync()
        {
            try { _items = await Task.Run(() => StartupManager.GetAll()); }
            catch (Exception ex) { Log.Error("Startup list", ex); _items = new List<StartupEntry>(); }
            List.ItemsSource = _items;
            UpdateSummary();
        }

        void UpdateSummary()
        {
            int on = _items.Count(i => i.Enabled);
            TxtSummary.Text = Loc.F("ui.startup.summary", on, _items.Count);
        }

        async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        void Toggle_Click(object sender, RoutedEventArgs e)
        {
            var cb = (CheckBox)sender;
            if (!(cb.DataContext is StartupEntry entry)) return;
            bool enable = cb.IsChecked == true;
            var w = (MainWindow)Window.GetWindow(this);
            try
            {
                StartupManager.SetEnabled(entry, enable);
                w.ShowToast(Loc.F(enable ? "ui.startup.enabled" : "ui.startup.disabled", entry.Name), ToastKind.Success);
            }
            catch (Exception ex)
            {
                cb.IsChecked = entry.Enabled;
                w.ShowToast(Loc.F("ui.startup.error", entry.Name, ex.Message), ToastKind.Error);
                Log.Error("Startup " + entry.Name, ex);
            }
            UpdateSummary();
        }
    }
}
