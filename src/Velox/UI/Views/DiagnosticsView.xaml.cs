using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Velox.Core.Diagnostics;
using Velox.Localization;

namespace Velox.UI.Views
{
    public partial class DiagnosticsView : UserControl, IPage
    {
        static readonly string[] GroupOrder = { "repair", "hardware", "network" };

        public DiagnosticsView()
        {
            InitializeComponent();
            Build();
            AppState.Current.ScanCompleted += () => Dispatcher.BeginInvoke(new System.Action(RenderHealth));
        }

        public void OnShown() { Build(); RenderHealth(); }
        public void RefreshTexts() { Build(); RenderHealth(); }

        // ------------------------------------------------------------ Informe de salud

        bool _healthLoading;

        async void RenderHealth()
        {
            if (_healthLoading) return;
            var st = AppState.Current;
            var sys = st.Sys;
            if (sys == null)
            {
                HealthRows.Children.Clear();
                HealthRows.Children.Add(new TextBlock { Text = Loc.T("ui.sum.scanning"), Style = Res<Style>("Muted"), Margin = new Thickness(0, 12, 0, 12) });
                return;
            }
            _healthLoading = true;
            var inputs = new Core.Scan.HealthInputs
            {
                DiskFreeGB = sys.DiskFreeGB, DiskSizeGB = sys.DiskSizeGB, DiskType = sys.DiskType, RamTotalGB = sys.RamTotalGB,
                RecommendedPending = st.Optimizations.Scanned ? st.Optimizations.RecommendedDiff : 0,
                Reverted = st.Reverted?.Count ?? 0, UptimeDays = sys.UptimeDays, StartupApps = sys.StartupEnabled,
            };
            try
            {
                await System.Threading.Tasks.Task.Run(() =>
                {
                    var disks = Core.Scan.HealthReport.ReadDiskHealth();
                    inputs.DisksUnhealthy = disks.Item1; inputs.DisksTotal = disks.Item2;
                    var mem = Core.Tools.MemoryTool.Snapshot();
                    if (mem.Total > 0) inputs.RamAvailablePct = mem.AvailablePct;
                    inputs.RebootPending = Core.Drivers.DriverSafety.RebootPendingReason() != null;
                    inputs.DefenderRealtime = Core.Scan.HealthReport.ReadDefenderRealtime();
                });
            }
            finally { _healthLoading = false; }

            HealthRows.Children.Clear();
            var items = Core.Scan.HealthReport.Build(inputs);
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                // Verde = bien, ámbar = pide atención (cada color con un solo significado)
                g.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = Res<Brush>(it.Attention ? "Warning" : "Success"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left });
                var name = new TextBlock { Text = it.Name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(name, 1); g.Children.Add(name);
                var text = new TextBlock { Text = it.Text, Style = Res<Style>("Muted"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
                Grid.SetColumn(text, 2); g.Children.Add(text);
                if (it.Page != null)
                {
                    var b = new Button { Content = it.ActionText, Style = Res<Style>("BtnBase"), Padding = new Thickness(12, 6, 12, 6), Tag = it.Page, VerticalAlignment = VerticalAlignment.Center };
                    b.Click += (s, e) => Win.Navigate((string)((Button)s).Tag);
                    System.Windows.Automation.AutomationProperties.SetName(b, it.ActionText + " (" + it.Name + ")");
                    Grid.SetColumn(b, 3); g.Children.Add(b);
                }
                HealthRows.Children.Add(new Border
                {
                    BorderBrush = Res<Brush>("Rule"), BorderThickness = new Thickness(0, i == 0 ? 0 : 1, 0, 0),
                    Padding = new Thickness(0, 12, 0, 12), MinHeight = 52, Child = g,
                });
            }
        }

        T Res<T>(string key) => (T)FindResource(key);
        MainWindow Win => (MainWindow)Window.GetWindow(this);

        void Build()
        {
            Groups.Children.Clear();
            var sys = AppState.Current.Sys;
            foreach (var g in GroupOrder)
            {
                var actions = DiagnosticsCatalog.All.Where(a => a.Group == g && DiagnosticsCatalog.IsAvailable(a, sys)).ToList();
                if (actions.Count == 0) continue;
                Groups.Children.Add(new TextBlock { Text = Loc.T("diaggroup." + g), Style = Res<Style>("H2"), Margin = new Thickness(0, 24, 0, 10) });
                // Un bloque por grupo con filas separadas por reglas (como la hoja de reglajes), no una rejilla de tarjetas
                var list = new StackPanel();
                for (int i = 0; i < actions.Count; i++) list.Children.Add(RowFor(actions[i], i == 0));
                Groups.Children.Add(new Border { Style = Res<Style>("Card"), Padding = new Thickness(20, 4, 20, 4), Child = list });
            }
        }

        FrameworkElement RowFor(DiagAction a, bool first)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var tile = new Border { Style = Res<Style>("IconTile"), Width = 34, Height = 34, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 14, 0) };
            tile.Child = new TextBlock { Text = a.Icon, FontFamily = Res<FontFamily>("Icons"), FontSize = 16, Foreground = Res<Brush>("AccentText"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(tile);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = new WrapPanel();
            title.Children.Add(new TextBlock { Text = a.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            string kindKey = a.Kind == DiagKind.InApp ? "ui.diag.kindInApp" : a.Kind == DiagKind.Console ? "ui.diag.kindConsole" : "ui.diag.kindExternal";
            title.Children.Add(new TextBlock { Text = Loc.T(kindKey), Style = Res<Style>("Label"), FontWeight = FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center });
            text.Children.Add(title);
            text.Children.Add(new TextBlock { Text = a.Description, Style = Res<Style>("Muted"), FontSize = 12.5, Margin = new Thickness(0, 4, 0, 0), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left });
            Grid.SetColumn(text, 1);
            g.Children.Add(text);

            var btn = new Button
            {
                Style = Res<Style>("BtnBase"),
                Content = Loc.T(a.Kind == DiagKind.InApp ? "ui.diag.run" : "ui.diag.open"),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0), MinWidth = 92, Tag = a,
            };
            System.Windows.Automation.AutomationProperties.SetName(btn, (string)btn.Content + ": " + a.Name);
            btn.Click += Run_Click;
            btn.SetBinding(IsEnabledProperty, new System.Windows.Data.Binding(nameof(AppState.IsIdle)) { Source = AppState.Current });
            Grid.SetColumn(btn, 2);
            g.Children.Add(btn);

            return new Border
            {
                BorderBrush = Res<Brush>("Rule"), BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0),
                Padding = new Thickness(0, 14, 0, 14), Child = g,
            };
        }

        async void Run_Click(object sender, RoutedEventArgs e)
        {
            var a = (DiagAction)((Button)sender).Tag;
            if (a.NeedsConfirm && !await Win.ConfirmAsync(a.Name, a.ConfirmText, null, Loc.T("ui.btn.continue"), Loc.T("ui.btn.cancel"))) return;

            DiagResult r = null;
            if (a.Kind == DiagKind.InApp)
                await AppState.Current.RunBusy(Loc.F("ui.diag.running", a.Name), async () => r = await DiagnosticsCatalog.RunAsync(a, AppState.Current.Sys));
            else
                r = await DiagnosticsCatalog.RunAsync(a, AppState.Current.Sys);
            if (r == null) return;

            if (a.Kind == DiagKind.InApp)
                await Win.ConfirmAsync(a.Name, r.Message, r.Details, Loc.T("ui.btn.ok"), null);
            else
                Win.ShowToast(r.Message, r.Success ? ToastKind.Success : ToastKind.Error);
        }
    }
}
