using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Velox.Core;
using Velox.Core.Drivers;
using Velox.Localization;

namespace Velox.UI.Views
{
    /// <summary>Fila de la lista de componentes.</summary>
    public sealed class DeviceRow
    {
        public DeviceDriver Device { get; set; }
        public string Icon { get; set; }
        public string CategoryText { get; set; }
    }

    public partial class DriversView : UserControl, IPage
    {
        static readonly string[] MainCategories = { "gpu", "network", "audio", "bluetooth", "storage", "chipset" };
        List<DeviceDriver> _devices = new List<DeviceDriver>();
        List<DriverUpdate> _updates;
        DateTime? _lastSearch;
        bool _loaded;

        public DriversView()
        {
            InitializeComponent();
            AppState.Current.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(AppState.IsBusy)) UpdateButtons(); };
            RenderUpdates();
            UpdateButtons();
        }

        MainWindow Win => (MainWindow)Window.GetWindow(this);
        T Res<T>(string key) => (T)FindResource(key);

        public async void OnShown()
        {
            if (!_loaded) await LoadDevicesAsync();
            RenderVendors();
        }

        public void RefreshTexts()
        {
            RenderUpdates();
            RenderDevices();
            RenderVendors();
            UpdateButtons();
        }

        static string IconFor(string category)
        {
            switch (category)
            {
                case "gpu": return "";
                case "network": return "";
                case "audio": return "";
                case "bluetooth": return "";
                case "storage": return "";
                case "chipset": return "";
                case "input": return "";
                case "usb": return "";
                default: return "";
            }
        }

        // ------------------------------------------------------------ Componentes

        async Task LoadDevicesAsync()
        {
            try { _devices = await Task.Run(() => DriverInventory.GetInstalled()); _loaded = true; }
            catch (Exception ex) { Log.Error("Driver inventory", ex); _devices = new List<DeviceDriver>(); }
            RenderDevices();
        }

        void RenderDevices()
        {
            bool all = ChkAll.IsChecked == true;
            DeviceList.ItemsSource = _devices
                .Where(d => all || (MainCategories.Contains(d.Category) && !IsVirtual(d)))
                .Select(d => new DeviceRow { Device = d, Icon = IconFor(d.Category), CategoryText = Loc.T("drv.cat." + (d.Category ?? "other")) })
                .ToList();
            UpdateButtons();
        }

        // Dispositivos creados por programas (pantallas virtuales de emuladores, escritorio remoto...): sin hardware real
        static bool IsVirtual(DeviceDriver d) =>
            (d.DeviceId ?? "").StartsWith(@"ROOT\", StringComparison.OrdinalIgnoreCase) ||
            (d.DeviceName ?? "").IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0;

        void ShowAll_Click(object sender, RoutedEventArgs e) => RenderDevices();
        void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

        async void Rollback_Click(object sender, RoutedEventArgs e)
        {
            if (!(DeviceList.SelectedItem is DeviceRow row)) return;
            var d = row.Device;
            if (!await Win.ConfirmAsync(Loc.F("ui.drv.rollbackTitle", d.DeviceName), Loc.T("ui.drv.rollbackText"), null, Loc.T("ui.drv.rollback"), Loc.T("ui.btn.cancel"))) return;
            string msg = null; string error = null;
            await AppState.Current.RunBusy(Loc.T("ui.drv.rollbackBusy"), async () =>
            {
                try { msg = await DriverRollback.RollbackAsync(d); }
                catch (Exception ex) { error = ex.Message; }
            });
            if (error != null) await Win.ConfirmAsync(Loc.F("ui.drv.rollbackTitle", d.DeviceName), error, null, Loc.T("ui.btn.ok"), null);
            else if (msg != null)
            {
                Win.ShowToast(msg, ToastKind.Success);
                AppState.Current.NeedsReboot = true;
                await LoadDevicesAsync();
            }
        }

        // ------------------------------------------------------------ Actualizaciones

        async void Search_Click(object sender, RoutedEventArgs e)
        {
            List<DriverUpdate> found = null;
            await AppState.Current.RunBusy(Loc.T("ui.drv.searching"), async () =>
            {
                var progress = new Progress<string>(t => AppState.Current.SetBusyText(t));
                found = await DriverUpdater.SearchAsync(progress);
                if (!_loaded) await LoadDevicesAsync();
            });
            if (found == null) return;
            _updates = found;
            _lastSearch = DateTime.Now;
            RenderUpdates();
            if (found.Count == 0) Win.ShowToast(Loc.T("ui.drv.allUpToDate"), ToastKind.Success);
            else Win.ShowToast(Loc.F("ui.drv.foundN", found.Count));
        }

        void Selection_Click(object sender, RoutedEventArgs e) => UpdateButtons();

        void RenderUpdates()
        {
            TxtLastSearch.Text = _lastSearch.HasValue ? Loc.F("ui.drv.lastSearch", _lastSearch.Value.ToString("t")) : "";
            PanelWarnings.Children.Clear();
            if (_updates == null)
            {
                TxtUpdatesState.Text = Loc.T("ui.drv.notSearched");
                UpdatesList.ItemsSource = null;
                InstallBar.Visibility = Visibility.Collapsed;
                return;
            }
            TxtUpdatesState.Text = _updates.Count == 0 ? Loc.T("ui.drv.none") : Loc.F("ui.drv.available", _updates.Count);
            foreach (var w in DriverUpdater.LastSearchWarnings)
            {
                var dp = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var ic = new TextBlock { Text = "", FontFamily = Res<FontFamily>("Icons"), Foreground = Res<Brush>("Warning"), Margin = new Thickness(0, 2, 10, 0) };
                DockPanel.SetDock(ic, Dock.Left);
                dp.Children.Add(ic);
                dp.Children.Add(new TextBlock { Text = w, TextWrapping = TextWrapping.Wrap, Foreground = Res<Brush>("TextMuted") });
                PanelWarnings.Children.Add(dp);
            }
            UpdatesList.ItemsSource = null;
            UpdatesList.ItemsSource = _updates;
            InstallBar.Visibility = _updates.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateButtons();
        }

        void UpdateButtons()
        {
            bool idle = AppState.Current.IsIdle;
            int n = _updates?.Count(u => u.Selected) ?? 0;
            BtnSearch.IsEnabled = idle;
            BtnInstall.Content = Loc.F("ui.drv.installN", n);
            BtnInstall.IsEnabled = idle && n > 0;
            BtnRollback.IsEnabled = idle && DeviceList.SelectedItem != null;
            UpdatesList.IsEnabled = idle;
        }

        async void Install_Click(object sender, RoutedEventArgs e)
        {
            var selected = _updates?.Where(u => u.Selected).ToList() ?? new List<DriverUpdate>();
            if (selected.Count == 0) return;
            var lines = selected.Select(u => "•  " + u.DeviceName + ":  " + u.CurrentVersion + "  →  " + u.NewVersion + "  (" + u.SourceText + ")");
            string text = Loc.T("ui.drv.confirmText");
            if (selected.Any(u => u.Source == DriverSource.Nvidia)) text += "\n\n" + Loc.T("ui.drv.confirmNvidia");
            if (!await Win.ConfirmAsync(Loc.F("ui.drv.confirmTitle", selected.Count), text, string.Join("\n", lines), Loc.T("ui.drv.install"), Loc.T("ui.btn.cancel"))) return;

            DriverInstallResult result = null;
            await AppState.Current.RunBusy(Loc.T("ui.drv.installing"), async () =>
            {
                var progress = new Progress<string>(t => AppState.Current.SetBusyText(t));
                result = await DriverUpdater.InstallAsync(selected, progress, CancellationToken.None);
            });
            if (result == null) return;
            if (result.RebootRequired) AppState.Current.NeedsReboot = true;

            string title = result.Failed == 0 ? Loc.F("ui.drv.doneOk", result.Installed) : Loc.F("ui.drv.doneSome", result.Installed, result.Failed);
            string body = result.RebootRequired ? Loc.T("ui.drv.rebootNeeded") : Loc.T("ui.drv.noReboot");
            await Win.ConfirmAsync(title, body, string.Join("\n", result.Messages), Loc.T("ui.btn.ok"), null);

            // Si todo fue bien, quita de la lista lo instalado; si hubo fallos, se deja para reintentar
            if (result.Failed == 0) _updates = _updates.Except(selected).ToList();
            await LoadDevicesAsync();
            RenderUpdates();
        }

        // ------------------------------------------------------------ Gráficas AMD / Intel

        List<GpuVendorTool> _vendors;

        async void RenderVendors()
        {
            // La consulta WMI se hace una vez y en segundo plano (antes bloqueaba la interfaz cada vez que se abría la página)
            if (_vendors == null)
            {
                try { _vendors = await Task.Run(() => DriverUpdater.OtherGpuVendors()); } catch { return; }
            }
            PanelVendors.Children.Clear();
            var tools = _vendors;
            foreach (var t in tools)
            {
                var card = new Border { Style = Res<Style>("Card"), Margin = new Thickness(0, 16, 0, 0) };
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = Loc.F("ui.drv.vendorTitle", t.GpuName), Style = Res<Style>("H2") });
                sp.Children.Add(new TextBlock { Text = Loc.F("ui.drv.vendorText", t.Vendor, t.DriverVersion), Style = Res<Style>("Muted"), FontSize = 12.5, Margin = new Thickness(0, 6, 16, 0) });
                g.Children.Add(sp);
                var btn = new Button { Style = Res<Style>("BtnBase"), Content = Loc.F("ui.drv.vendorButton", t.Vendor), VerticalAlignment = VerticalAlignment.Center, Tag = t.ToolUrl };
                btn.Click += (s, e) => Process.Start(new ProcessStartInfo((string)((Button)s).Tag) { UseShellExecute = true });
                Grid.SetColumn(btn, 1);
                g.Children.Add(btn);
                card.Child = g;
                PanelVendors.Children.Add(card);
            }
        }
    }
}
