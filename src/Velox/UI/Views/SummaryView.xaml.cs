using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Velox.Core.Backup;
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
            if (e.PropertyName == nameof(AppState.IsBusy)) { BtnScan.IsEnabled = AppState.Current.IsIdle; RenderScore(); RenderNotices(); }
            if (e.PropertyName == nameof(AppState.RestartNotice)) RenderScore();
            if (e.PropertyName == nameof(AppState.Reverted) || e.PropertyName == nameof(AppState.RebootVerdicts)) RenderNotices();
        }

        // ------------------------------------------------------------ Avisos

        /// <summary>
        /// Ajustes que han vuelto a su valor base (sin culpar a nadie: pudo ser Windows Update, otro programa o la propia
        /// persona) y el resultado de la comprobación tras reiniciar.
        /// </summary>
        void RenderNotices()
        {
            PanelNotices.Children.Clear();
            var st = AppState.Current;
            var rev = st.Reverted ?? new System.Collections.Generic.List<Core.Optimizations.Optimization>();
            if (rev.Count > 0)
            {
                string title = rev.Count == 1 ? Loc.T("ui.rev.title1") : Loc.F("ui.rev.title", rev.Count);
                string names = string.Join(" · ", rev.Take(4).Select(o => o.Name)) + (rev.Count > 4 ? " · " + Loc.F("ui.rev.more", rev.Count - 4) : "");
                var reapply = MakeButton(Loc.T("ui.rev.reapply"), "BtnBase", async () => await st.ReapplyRevertedAsync());
                var keep = MakeButton(Loc.T("ui.rev.keep"), "BtnGhost", async () => await st.AcceptRevertedAsync());
                PanelNotices.Children.Add(Notice(true, title, Loc.T("ui.rev.text") + "\n" + names, reapply, keep));
            }
            var verdicts = st.RebootVerdicts ?? new System.Collections.Generic.List<Core.Backup.RebootVerdict>();
            if (verdicts.Count > 0)
            {
                var bad = verdicts.Where(v => !v.Ok).ToList();
                string NameOf(string id) => Core.Optimizations.OptimizationEngine.Find(id)?.Name ?? id;
                string title = bad.Count == 0
                    ? (verdicts.Count == 1 ? Loc.T("ui.boot.ok1") : Loc.F("ui.boot.ok", verdicts.Count))
                    : (bad.Count == 1 ? Loc.T("ui.boot.bad1") : Loc.F("ui.boot.bad", bad.Count));
                string text = bad.Count == 0
                    ? string.Join(" · ", verdicts.Select(v => NameOf(v.Id) + (v.Effective.HasValue ? " ✓✓" : " ✓")))
                      + (verdicts.Any(v => v.Effective.HasValue) ? "\n" + Loc.T("ui.boot.checkedLegend") : "")
                    : string.Join("\n", bad.Select(v => NameOf(v.Id) + ": " + (v.Kept ? Loc.T("ui.boot.notEffective") : Loc.T("ui.boot.notKept"))));
                var ok = MakeButton(Loc.T("ui.boot.dismiss"), "BtnGhost", () => { st.DismissRebootVerdicts(); return System.Threading.Tasks.Task.CompletedTask; });
                PanelNotices.Children.Add(Notice(bad.Count > 0, title, text, ok));
            }
            PanelNotices.Visibility = PanelNotices.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        Button MakeButton(string text, string style, Func<System.Threading.Tasks.Task> action)
        {
            var b = new Button { Content = text, Style = Res<Style>(style), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 8, 14, 8) };
            b.IsEnabled = AppState.Current.IsIdle;
            b.Click += async (s, e) => await action();
            return b;
        }

        /// <summary>Aviso en una franja: ámbar si pide atención, verde si confirma (cada color con un solo significado).</summary>
        Border Notice(bool attention, string title, string text, params Button[] buttons)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new TextBlock { Text = attention ? "" : "", FontFamily = Res<FontFamily>("Icons"), FontSize = 15, Foreground = Res<Brush>(attention ? "Warning" : "Success"), Margin = new Thickness(0, 1, 14, 0), VerticalAlignment = VerticalAlignment.Top };
            g.Children.Add(icon);
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap });
            sp.Children.Add(new TextBlock { Text = text, Style = Res<Style>("Muted"), FontSize = 12.5, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(sp, 1); g.Children.Add(sp);
            var bp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            foreach (var b in buttons) bp.Children.Add(b);
            Grid.SetColumn(bp, 2); g.Children.Add(bp);
            return new Border
            {
                Background = Res<Brush>(attention ? "PendingRow" : "CardBg"),
                BorderBrush = Res<Brush>(attention ? "Warning" : "Border"),
                BorderThickness = new Thickness(attention ? 1 : 1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(20, 14, 18, 14),
                Margin = new Thickness(0, 0, 0, 10),
                Child = g,
            };
        }

        void History_Click(object sender, RoutedEventArgs e) => ((MainWindow)Window.GetWindow(this)).Navigate("history");

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
            RenderNotices();
        }

        void RenderScore()
        {
            var vm = AppState.Current.Optimizations;
            var st = AppState.Current;
            RenderSaved();
            if (!vm.Scanned)
            {
                TxtHeroTitle.Text = Loc.T("ui.sum.heroScanning");
                TxtHeroText.Text = Loc.T("ui.sum.heroScanningText");
                BtnApplyRec.Content = Loc.T("ui.sum.applyRec");
                BtnApplyRec.IsEnabled = false;
                RenderSheet();
                return;
            }
            int total = vm.ScoredCount, match = vm.MatchCount, diff = vm.RecommendedDiff;
            TxtHeroTitle.Text = diff == 0 ? Loc.T("ui.sum.heroDone") : Loc.F("ui.sum.heroTodo", diff);
            TxtHeroText.Text = diff == 0 ? Loc.T("ui.sum.heroDoneText") : Loc.F("ui.sum.heroTodoText", match, total);
            RestartBadge.Visibility = string.IsNullOrEmpty(st.RestartNotice) ? Visibility.Collapsed : Visibility.Visible;
            TxtRestartBadge.Text = st.RestartNotice;
            BtnApplyRec.Content = diff == 0 ? Loc.T("ui.sum.applyRecDone") : Loc.F("ui.sum.applyRecN", diff);
            BtnApplyRec.IsEnabled = diff > 0 && st.IsIdle;
            RenderSheet();
        }

        // ------------------------------------------------------------ Hoja de reglajes

        const int SheetMax = 7;

        /// <summary>
        /// Con cambios recomendados: AJUSTE | AHORA → RECOMENDADO. Sin ellos: el reglaje en vigor, AJUSTE | BASE → VELOX.
        /// Las etiquetas salen de cada optimización (opt.Id.base / opt.Id.velox), nunca se inventan valores.
        /// </summary>
        void RenderSheet()
        {
            var vm = AppState.Current.Optimizations;
            SheetRows.Children.Clear();
            BtnMore.Visibility = Visibility.Collapsed;
            if (!vm.Scanned) { SheetHead.Visibility = Visibility.Collapsed; return; }

            bool todo = vm.RecommendedDiff > 0;
            // BASE → VELOX solo enseña lo que Velox ha cambiado (tiene copia del valor original). Lo que ya venía así de
            // Windows (p. ej. el Modo de juego activado de fábrica) no es un cambio de Velox y no se le atribuye.
            var saved = new System.Collections.Generic.HashSet<string>(BackupStore.Ids(), StringComparer.OrdinalIgnoreCase);
            var list = (todo
                ? vm.AllItems.Where(i => i.IsScored && i.IsApplied != i.IsRecommended)
                : vm.AllItems.Where(i => i.IsApplied && saved.Contains(i.Opt.Id))).ToList();

            ColSetting.Text = Loc.T("ui.sum.colSetting");
            ColFrom.Text = todo ? Loc.T("ui.sum.colNow") : Loc.T("ui.sum.colBase");
            ColTo.Text = todo ? Loc.T("ui.sum.colRec") : "VELOX";
            SheetHead.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (list.Count == 0)
            {
                SheetRows.Children.Add(new TextBlock { Text = Loc.T("ui.sum.noneApplied"), Style = Res<Style>("Muted"), Margin = new Thickness(0, 4, 0, 0) });
                return;
            }
            foreach (var it in list.Take(SheetMax))
            {
                string from = todo ? (it.IsApplied ? it.VeloxText : it.BaseText) : it.BaseText;
                string to = todo ? (it.IsRecommended ? it.VeloxText : it.BaseText) : it.VeloxText;
                AddSheetRow(it.Name, from, to);
            }
            if (list.Count > SheetMax)
            {
                BtnMore.Content = Loc.F("ui.sum.more", list.Count);
                BtnMore.Visibility = Visibility.Visible;
            }
        }

        void AddSheetRow(string name, string from, string to)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star) });
            g.Children.Add(new TextBlock { Text = name, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = name, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center });
            var f = new TextBlock { Text = from, Style = Res<Style>("Figure"), FontSize = 13.5, FontWeight = FontWeights.Normal, Foreground = Res<Brush>("TextMuted"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(f, 1); g.Children.Add(f);
            var arrow = new TextBlock { Text = "\uE72A", FontFamily = Res<FontFamily>("Icons"), FontSize = 10, Foreground = Res<Brush>("TextDim"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(arrow, 2); g.Children.Add(arrow);
            var t = new TextBlock { Text = to, Style = Res<Style>("Figure"), FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(t, 3); g.Children.Add(t);
            SheetRows.Children.Add(new Border { BorderBrush = Res<Brush>("Rule"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 10, 0, 10), Child = g });
        }

        void RenderSaved()
        {
            int n = BackupStore.Ids().Count;
            TxtSaved.Text = n == 0 ? Loc.T("ui.sum.savedNone") : n == 1 ? Loc.T("ui.sum.saved1") : Loc.F("ui.sum.saved", n);
            BtnBackToBase.IsEnabled = n > 0 && AppState.Current.IsIdle;
        }

        async void BackToBase_Click(object sender, RoutedEventArgs e)
        {
            await ((MainWindow)Window.GetWindow(this)).UndoAllAsync();
            RenderScore();
        }

        // ------------------------------------------------------------ Cabecera: el equipo en cifras

        void RenderTiles(SystemInfo s)
        {
            SpecTiles.Children.Clear();
            if (s == null) return;
            var gpu = s.Gpus.FirstOrDefault(g => g.Dedicated) ?? s.Gpus.FirstOrDefault();
            AddTile(Loc.T("ui.sum.tileCpu"), Shorten(s.Cpu), Loc.F("ui.sum.tileCpuSub", s.Cores, s.Threads));
            AddTile(Loc.T("ui.sum.tileGpu"), gpu == null ? Loc.T("ui.sum.gpuNone") : Shorten(gpu.Name), gpu != null && gpu.VramGB > 0 ? gpu.VramGB.ToString("0.#") + " GB VRAM" : "");
            AddTile(Loc.T("ui.sum.tileRam"), s.RamTotalGB.ToString("0.#") + " GB " + s.RamType,
                // En máquinas virtuales Windows no da módulos de RAM ni tipo de disco: no se muestran «0 módulos» ni «Unknown»
                (s.RamSpeed > 0 ? s.RamSpeed + " MT/s" : "") + (s.RamSpeed > 0 && s.RamModules > 0 ? " · " : "")
                + (s.RamModules > 0 ? Loc.F("ui.sum.tileRamModules", s.RamModules) : ""));
            AddTile(Loc.T("ui.sum.tileDisk"), (s.DiskType == "SSD" || s.DiskType == "HDD" ? s.DiskType : Loc.T("ui.sum.diskGeneric")) + (string.IsNullOrEmpty(s.DiskBus) ? "" : " · " + s.DiskBus),
                Loc.F("ui.sum.tileDiskFree", s.DiskFreeGB.ToString("0"), s.DiskSizeGB.ToString("0")));
        }

        static string Shorten(string s)
        {
            if (string.IsNullOrEmpty(s)) return "-";
            s = s.Replace("(R)", "").Replace("(TM)", "").Replace("  ", " ").Replace("NVIDIA GeForce ", "").Replace("Intel Core ", "Core ");
            return s.Trim();
        }

        void AddTile(string label, string value, string sub)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };
            sp.Children.Add(new TextBlock { Text = label, Style = Res<Style>("Label") });
            sp.Children.Add(new TextBlock { Text = value, Style = Res<Style>("Figure"), FontSize = 21, Margin = new Thickness(0, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = value });
            sp.Children.Add(new TextBlock { Text = sub, Style = Res<Style>("Caption"), Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            SpecTiles.Children.Add(sp);
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
                    case FindingLevel.Warning: glyph = ""; color = Res<Brush>("Warning"); bg = Res<Brush>("WarningSoft"); break;
                    case FindingLevel.Ok: glyph = ""; color = Res<Brush>("Success"); bg = Res<Brush>("SuccessSoft"); break;
                    default: glyph = ""; color = Res<Brush>("Info"); bg = Res<Brush>("CardInner"); break;
                }
                var dp = new DockPanel();
                var ic = new TextBlock { Text = glyph, FontFamily = Res<FontFamily>("Icons"), FontSize = 14, Foreground = color, Margin = new Thickness(0, 2, 12, 0), VerticalAlignment = VerticalAlignment.Top };
                DockPanel.SetDock(ic, Dock.Left);
                dp.Children.Add(ic);
                dp.Children.Add(new TextBlock { Text = f.Text, TextWrapping = TextWrapping.Wrap, LineHeight = 19 });
                PanelFindings.Children.Add(new Border { Background = bg, CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 11, 14, 11), Margin = new Thickness(0, 0, 0, 8), Child = dp });
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
                g.Children.Add(new TextBlock { Text = a.MB.ToString("N0") + " MB", Style = Res<Style>("Figure"), FontWeight = FontWeights.Normal, Foreground = Res<Brush>("TextMuted"), HorizontalAlignment = HorizontalAlignment.Right });
                sp.Children.Add(g);
                sp.Children.Add(new ProgressBar { Value = max > 0 ? a.MB / max * 100 : 0, Height = 3, Margin = new Thickness(0, 6, 0, 0) });
                PanelTopApps.Children.Add(sp);
            }
        }

        async void Scan_Click(object sender, RoutedEventArgs e) => await AppState.Current.ScanAsync();
        async void ApplyRec_Click(object sender, RoutedEventArgs e) => await AppState.Current.ApplyRecommendedAsync();
        void SeeAll_Click(object sender, RoutedEventArgs e) => ((MainWindow)Window.GetWindow(this)).Navigate("optimizations");
        void Quick_Click(object sender, RoutedEventArgs e) { if (sender is System.Windows.Controls.Button b && b.CommandParameter is string key) ((MainWindow)Window.GetWindow(this)).Navigate(key); }
    }
}
