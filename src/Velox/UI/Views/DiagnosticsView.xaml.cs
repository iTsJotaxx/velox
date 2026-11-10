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
        }

        public void OnShown() => Build();
        public void RefreshTexts() => Build();

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
