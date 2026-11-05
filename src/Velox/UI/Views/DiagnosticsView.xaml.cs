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
                Groups.Children.Add(new TextBlock { Text = Loc.T("diaggroup." + g), Style = Res<Style>("H2"), Margin = new Thickness(0, 22, 0, 12) });
                var wrap = new WrapPanel();
                foreach (var a in actions) wrap.Children.Add(Card(a));
                Groups.Children.Add(wrap);
            }
        }

        FrameworkElement Card(DiagAction a)
        {
            var card = new Border { Style = Res<Style>("Card"), Width = 344, Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(20, 18, 20, 18) };
            var dp = new DockPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var tile = new Border { Style = Res<Style>("IconTile") };
            tile.Child = new TextBlock { Text = a.Icon, FontFamily = Res<FontFamily>("Icons"), FontSize = 17, Foreground = Res<Brush>("AccentText"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            head.Children.Add(tile);
            var titles = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = a.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = 250 });
            string kindKey = a.Kind == DiagKind.InApp ? "ui.diag.kindInApp" : a.Kind == DiagKind.Console ? "ui.diag.kindConsole" : "ui.diag.kindExternal";
            titles.Children.Add(new TextBlock { Text = Loc.T(kindKey), Style = Res<Style>("Caption"), Margin = new Thickness(0, 2, 0, 0) });
            head.Children.Add(titles);
            DockPanel.SetDock(head, Dock.Top);
            dp.Children.Add(head);

            var btn = new Button
            {
                Style = Res<Style>(a.Kind == DiagKind.InApp ? "BtnPrimary" : "BtnBase"),
                Content = Loc.T(a.Kind == DiagKind.InApp ? "ui.diag.run" : "ui.diag.open"),
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0), Tag = a,
            };
            btn.Click += Run_Click;
            btn.SetBinding(IsEnabledProperty, new System.Windows.Data.Binding(nameof(AppState.IsIdle)) { Source = AppState.Current });
            DockPanel.SetDock(btn, Dock.Bottom);
            dp.Children.Add(btn);
            dp.Children.Add(new TextBlock { Text = a.Description, Style = Res<Style>("Muted"), FontSize = 12.5, Margin = new Thickness(0, 12, 0, 0) });
            card.Child = dp;
            return card;
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
