using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Velox.Core.Scan;
using Velox.Localization;

namespace Velox.UI.Views
{
    public partial class SummaryView : UserControl, IPage
    {
        public SummaryView()
        {
            InitializeComponent();
            AppState.Current.ScanCompleted += Render;
            AppState.Current.Optimizations.Changed += RenderScore;
            AppState.Current.PropertyChanged += OnStateChanged;
            Render();
        }

        void OnStateChanged(object s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppState.IsBusy)) { BtnScan.IsEnabled = AppState.Current.IsIdle; RenderScore(); }
            if (e.PropertyName == nameof(AppState.RestartNotice)) RenderScore();
        }

        public void OnShown() => Render();
        public void RefreshTexts() => Render();

        T Res<T>(string key) => (T)FindResource(key);

        void Render()
        {
            var sys = AppState.Current.Sys;
            TxtScanTime.Text = sys == null ? Loc.T("ui.sum.scanning") : Loc.F("ui.sum.lastScan", sys.ScanTime.ToString("t"));
            BtnScan.IsEnabled = AppState.Current.IsIdle;
            RenderScore();
            RenderTiles(sys);
            RenderSystem(sys);
            RenderFindings();
            RenderTopApps(sys);
        }

        void RenderScore()
        {
            var vm = AppState.Current.Optimizations;
            var st = AppState.Current;
            if (!vm.Scanned)
            {
                TxtPct.Text = "--";
                TxtHeroTitle.Text = Loc.T("ui.sum.heroScanning");
                TxtHeroText.Text = Loc.T("ui.sum.heroScanningText");
                TxtActive.Text = "...";
                BtnApplyRec.Content = Loc.T("ui.sum.applyRec");
                BtnApplyRec.IsEnabled = false;
                return;
            }
            int total = vm.ScoredCount, match = vm.MatchCount, diff = vm.RecommendedDiff;
            double pct = total == 0 ? 100 : Math.Round(100.0 * match / total);
            TxtPct.Text = pct + "%";
            Gauge.BeginAnimation(Infra.ArcGauge.ValueProperty, new DoubleAnimation(pct, TimeSpan.FromMilliseconds(700)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            TxtHeroTitle.Text = diff == 0 ? Loc.T("ui.sum.heroDone") : Loc.F("ui.sum.heroTodo", diff);
            TxtHeroText.Text = diff == 0 ? Loc.T("ui.sum.heroDoneText") : Loc.F("ui.sum.heroTodoText", match, total);
            TxtActive.Text = Loc.F("ui.sum.activeCount", vm.ActiveCount, vm.AvailableCount);
            RestartBadge.Visibility = string.IsNullOrEmpty(st.RestartNotice) ? Visibility.Collapsed : Visibility.Visible;
            TxtRestartBadge.Text = st.RestartNotice;
            BtnApplyRec.Content = diff == 0 ? Loc.T("ui.sum.applyRecDone") : Loc.F("ui.sum.applyRecN", diff);
            BtnApplyRec.IsEnabled = diff > 0 && st.IsIdle;
        }

        // ------------------------------------------------------------ Fichas

        void RenderTiles(SystemInfo s)
        {
            SpecTiles.Children.Clear();
            if (s == null) return;
            var gpu = s.Gpus.FirstOrDefault(g => g.Dedicated) ?? s.Gpus.FirstOrDefault();
            AddTile("", Loc.T("ui.sum.tileCpu"), Shorten(s.Cpu), Loc.F("ui.sum.tileCpuSub", s.Cores, s.Threads));
            AddTile("", Loc.T("ui.sum.tileGpu"), gpu == null ? Loc.T("ui.sum.gpuNone") : Shorten(gpu.Name), gpu != null && gpu.VramGB > 0 ? gpu.VramGB.ToString("0.#") + " GB VRAM" : "");
            AddTile("", Loc.T("ui.sum.tileRam"), s.RamTotalGB.ToString("0.#") + " GB " + s.RamType,
                // En máquinas virtuales Windows no da módulos de RAM ni tipo de disco: no se muestran «0 módulos» ni «Unknown»
                (s.RamSpeed > 0 ? s.RamSpeed + " MT/s" : "") + (s.RamSpeed > 0 && s.RamModules > 0 ? " · " : "")
                + (s.RamModules > 0 ? Loc.F("ui.sum.tileRamModules", s.RamModules) : ""));
            AddTile("", Loc.T("ui.sum.tileDisk"), (s.DiskType == "SSD" || s.DiskType == "HDD" ? s.DiskType : Loc.T("ui.sum.diskGeneric")) + (string.IsNullOrEmpty(s.DiskBus) ? "" : " · " + s.DiskBus),
                Loc.F("ui.sum.tileDiskFree", s.DiskFreeGB.ToString("0"), s.DiskSizeGB.ToString("0")));
        }

        static string Shorten(string s)
        {
            if (string.IsNullOrEmpty(s)) return "-";
            s = s.Replace("(R)", "").Replace("(TM)", "").Replace("  ", " ").Replace("NVIDIA GeForce ", "").Replace("Intel Core ", "Core ");
            return s.Trim();
        }

        void AddTile(string glyph, string label, string value, string sub)
        {
            var tile = new Border { Style = Res<Style>("Card"), Padding = new Thickness(18, 16, 18, 16), Margin = new Thickness(0, 0, 12, 0) };
            var sp = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = new Border { Style = Res<Style>("IconTile"), Width = 32, Height = 32, CornerRadius = new CornerRadius(9) };
            ic.Child = new TextBlock { Text = glyph, FontFamily = Res<FontFamily>("Icons"), FontSize = 15, Foreground = Res<Brush>("AccentText"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            head.Children.Add(ic);
            head.Children.Add(new TextBlock { Text = label, Style = Res<Style>("Caption"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
            sp.Children.Add(head);
            sp.Children.Add(new TextBlock { Text = value, FontSize = 14.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = value });
            sp.Children.Add(new TextBlock { Text = sub, Style = Res<Style>("Caption"), Margin = new Thickness(0, 3, 0, 0) });
            tile.Child = sp;
            SpecTiles.Children.Add(tile);
        }

        // ------------------------------------------------------------ Sistema

        void Row(Panel p, string label, string value)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 9) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(new TextBlock { Text = label, Foreground = Res<Brush>("TextMuted") });
            var v = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            p.Children.Add(g);
        }

        void RenderSystem(SystemInfo s)
        {
            PanelSystem.Children.Clear();
            if (s == null) return;
            Row(PanelSystem, Loc.T("ui.sum.os"), (s.OS ?? "").Replace("Microsoft ", "") + " (" + s.Build + ")");
            foreach (var d in s.Displays)
                Row(PanelSystem, Loc.T("ui.sum.display"), d.Width + "×" + d.Height + " · " + d.CurrentHz + " Hz" + (d.MaxHz > d.CurrentHz ? " (" + Loc.F("ui.sum.maxHz", d.MaxHz) + ")" : ""));
            Row(PanelSystem, Loc.T("ui.sum.powerPlan"), string.IsNullOrEmpty(s.PowerScheme) ? "-" : s.PowerScheme);
            Row(PanelSystem, Loc.T("ui.sum.type"), s.IsLaptop ? Loc.T("ui.sum.laptop") : Loc.T("ui.sum.desktop"));
            Row(PanelSystem, Loc.T("ui.sum.uptime"), Loc.F("ui.sum.uptimeDays", s.UptimeDays.ToString("0.#")));
        }

        // ------------------------------------------------------------ Hallazgos

        void RenderFindings()
        {
            PanelFindings.Children.Clear();
            var list = AppState.Current.Findings;
            if (AppState.Current.Sys == null)
            {
                PanelFindings.Children.Add(new TextBlock { Text = Loc.T("ui.sum.scanning"), Style = Res<Style>("Muted") });
                return;
            }
            foreach (var f in list)
            {
                string glyph; Brush color, bg;
                switch (f.Level)
                {
                    case FindingLevel.Warning: glyph = ""; color = Res<Brush>("Warning"); bg = new SolidColorBrush(Color.FromRgb(0x1F, 0x1B, 0x12)); break;
                    case FindingLevel.Ok: glyph = ""; color = Res<Brush>("Success"); bg = new SolidColorBrush(Color.FromRgb(0x13, 0x1F, 0x1B)); break;
                    default: glyph = ""; color = Res<Brush>("Info"); bg = Res<Brush>("CardInner"); break;
                }
                var dp = new DockPanel();
                var ic = new TextBlock { Text = glyph, FontFamily = Res<FontFamily>("Icons"), FontSize = 14, Foreground = color, Margin = new Thickness(0, 2, 12, 0), VerticalAlignment = VerticalAlignment.Top };
                DockPanel.SetDock(ic, Dock.Left);
                dp.Children.Add(ic);
                dp.Children.Add(new TextBlock { Text = f.Text, TextWrapping = TextWrapping.Wrap, LineHeight = 19 });
                PanelFindings.Children.Add(new Border { Background = bg, CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 11, 14, 11), Margin = new Thickness(0, 0, 0, 8), Child = dp });
            }
        }

        void RenderTopApps(SystemInfo s)
        {
            PanelTopApps.Children.Clear();
            if (s == null) return;
            double max = s.TopApps.Count > 0 ? s.TopApps.Max(a => a.MB) : 1;
            foreach (var a in s.TopApps)
            {
                var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                var g = new Grid();
                g.Children.Add(new TextBlock { Text = a.Name + (a.Count > 1 ? "  ×" + a.Count : ""), TextTrimming = TextTrimming.CharacterEllipsis });
                g.Children.Add(new TextBlock { Text = a.MB.ToString("N0") + " MB", Foreground = Res<Brush>("TextMuted"), HorizontalAlignment = HorizontalAlignment.Right });
                sp.Children.Add(g);
                sp.Children.Add(new ProgressBar { Value = max > 0 ? a.MB / max * 100 : 0, Height = 4, Margin = new Thickness(0, 6, 0, 0), Foreground = Res<Brush>("Violet") });
                PanelTopApps.Children.Add(sp);
            }
        }

        async void Scan_Click(object sender, RoutedEventArgs e) => await AppState.Current.ScanAsync();
        async void ApplyRec_Click(object sender, RoutedEventArgs e) => await AppState.Current.ApplyRecommendedAsync();
        void SeeAll_Click(object sender, RoutedEventArgs e) => ((MainWindow)Window.GetWindow(this)).Navigate("optimizations");
        void Quick_Click(object sender, RoutedEventArgs e) { if (sender is System.Windows.Controls.Button b && b.CommandParameter is string key) ((MainWindow)Window.GetWindow(this)).Navigate(key); }
    }
}
