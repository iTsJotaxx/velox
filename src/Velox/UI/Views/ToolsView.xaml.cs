using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Velox.Core;
using Velox.Core.Backup;
using Velox.Core.Disk;
using Velox.Core.Tools;
using Velox.Localization;
using Velox.Native;

namespace Velox.UI.Views
{
    public partial class ToolsView : UserControl, IPage
    {
        readonly DispatcherTimer _hzTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        readonly List<double> _hzSamples = new List<double>();
        readonly Stopwatch _hzWatch = new Stopwatch();
        long _hzLast;
        double _hzPeak;

        public ToolsView()
        {
            InitializeComponent();
            _hzTimer.Tick += HzTick;
            AppState.Current.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(AppState.IsBusy)) UpdateButtons(); };
            IsVisibleChanged += (s, e) => { if (!IsVisible) StopHz(); };
            RefreshTexts();
        }

        MainWindow Win => (MainWindow)Window.GetWindow(this);

        public void OnShown() { RefreshMemory(); RefreshBackups(); UpdateButtons(); RenderDnsNow(); }

        public void RefreshTexts()
        {
            BtnHz.Content = MouseHz.Running ? Loc.T("ui.tools.hzStop") : Loc.T("ui.tools.hzStart");
            if (!MouseHz.Running && _hzSamples.Count == 0) TxtHzInfo.Text = Loc.T("ui.tools.hzIdle");
            RefreshBackups();
        }

        void UpdateButtons()
        {
            bool idle = AppState.Current.IsIdle;
            BtnPurge.IsEnabled = BtnRp.IsEnabled = BtnExplorer.IsEnabled = BtnAnalyze.IsEnabled = BtnDnsMeasure.IsEnabled = BtnDnsRestore.IsEnabled = idle;
            if (_clean.Count > 0) UpdateCleanButton();
            BtnUndo.IsEnabled = idle && BackupStore.Ids().Count > 0;
        }

        void RefreshMemory()
        {
            Task.Run(() => MemoryTool.Snapshot()).ContinueWith(t =>
            {
                if (t.IsFaulted) return;
                ShowMemory(t.Result);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void ShowMemory(MemSnapshot s)
        {
            TxtStandby.Text = s.HasLists ? MemSnapshot.Gb(s.Standby) : "--";
            TxtFree.Text = MemSnapshot.Gb(s.FreeOrAvailable);
        }

        void RefreshBackups()
        {
            int n = BackupStore.Ids().Count;
            TxtBackups.Text = Loc.F("ui.tools.backupCount", n);
        }

        // ------------------------------------------------------------ Hz del ratón

        void Hz_Click(object sender, RoutedEventArgs e)
        {
            if (MouseHz.Running) { StopHz(); return; }
            MouseHz.Start();
            _hzLast = MouseHz.Count;
            _hzWatch.Restart();
            _hzSamples.Clear();
            _hzPeak = 0;
            TxtHz.Text = "--";
            TxtHzPeak.Text = "";
            TxtHzInfo.Text = Loc.T("ui.tools.hzMove");
            HzGauge.Value = 0;
            BtnHz.Content = Loc.T("ui.tools.hzStop");
            _hzTimer.Start();
        }

        void StopHz()
        {
            _hzTimer.Stop();
            try { MouseHz.Stop(); } catch { }
            BtnHz.Content = Loc.T("ui.tools.hzStart");
        }

        static int Nominal(double hz)
        {
            foreach (var n in new[] { 125, 250, 500, 1000, 2000, 4000, 8000 })
                if (Math.Abs(hz - n) <= n * 0.15) return n;
            return 0;
        }

        void HzTick(object sender, EventArgs e)
        {
            long count = MouseHz.Count;
            double dt = _hzWatch.Elapsed.TotalSeconds;
            _hzWatch.Restart();
            long delta = count - _hzLast;
            _hzLast = count;
            if (dt <= 0) return;
            double rate = delta / dt;
            if (rate < 40) return; // ratón quieto o muy lento

            _hzSamples.Add(rate);
            if (_hzSamples.Count > 12) _hzSamples.RemoveAt(0);
            _hzPeak = Math.Max(_hzPeak, rate);
            // La frecuencia real se parece más a las mejores muestras (al mover lento el ratón envía menos informes)
            double stable = _hzSamples.OrderByDescending(x => x).Take(3).Average();
            int nominal = Nominal(stable);

            TxtHz.Text = stable.ToString("N0");
            TxtHzPeak.Text = Loc.F("ui.tools.hzPeak", _hzPeak.ToString("N0"));
            HzGauge.Value = Math.Min(100, stable / 1000.0 * 100);
            if (nominal == 0) TxtHzInfo.Text = Loc.T("ui.tools.hzUnstable");
            else if (nominal <= 125) TxtHzInfo.Text = Loc.T("ui.tools.hz125");
            else if (nominal < 1000) TxtHzInfo.Text = Loc.F("ui.tools.hzLow", nominal);
            else TxtHzInfo.Text = Loc.F("ui.tools.hzGood", nominal);
        }

        // ------------------------------------------------------------ Acciones

        async void Purge_Click(object sender, RoutedEventArgs e)
        {
            MemCleanResult r = null;
            var opts = MemCleanOptions.FromSettings();
            if (!opts.Any) { Win.Navigate("memory"); return; }
            if (await AppState.Current.RunBusy(Loc.T("ui.tools.memBusy"), async () => r = await Task.Run(() => MemoryTool.Clean(opts))) && r != null)
            {
                ShowMemory(r.After);
                Win.ShowToast(Loc.F("ui.tools.memDone", MemSnapshot.Gb(r.Gained)), ToastKind.Success);
            }
        }

        void MemMore_Click(object sender, RoutedEventArgs e) => Win.Navigate("memory");

        // ------------------------------------------------------------ DNS

        void RenderDnsNow()
        {
            Task.Run(() =>
            {
                var adapters = DnsTool.ActiveAdapters();
                var p = DnsTool.CurrentProvider(adapters);
                string now = adapters.Count == 0 ? Loc.T("dns.err.noAdapter")
                    : (p != null ? p.Name : adapters.All(a => DnsTool.SplitServers(a.StaticServers).Count == 0) ? Loc.T("dns.auto") : Loc.T("dns.custom"))
                      + "  ·  " + string.Join(", ", adapters.SelectMany(a => a.Current).Distinct().Take(3));
                return now;
            }).ContinueWith(t =>
            {
                TxtDnsNow.Text = Loc.F("dns.now", t.IsFaulted ? "-" : t.Result);
                BtnDnsRestore.Visibility = DnsTool.HasBackup ? Visibility.Visible : Visibility.Collapsed;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>Mide el DNS actual y los tres proveedores con consultas reales (mediana de varias) desde esta conexión.</summary>
        async void DnsMeasure_Click(object sender, RoutedEventArgs e)
        {
            List<Tuple<string, string, double?>> rows = null; // (id del proveedor o null = el actual, nombre, ms)
            if (!await AppState.Current.RunBusy(Loc.T("dns.busyMeasure"), async () => rows = await Task.Run(() =>
            {
                var res = new List<Tuple<string, string, double?>>();
                var current = DnsTool.ActiveAdapters().SelectMany(a => a.Current).FirstOrDefault();
                var prov = DnsTool.CurrentProvider();
                if (current != null && prov == null) res.Add(Tuple.Create((string)null, Loc.F("dns.yours", current), DnsTool.Measure(current)));
                foreach (var p in DnsTool.Providers) res.Add(Tuple.Create(p.Id, p.Name + "  " + p.Primary, DnsTool.Measure(p.Primary)));
                return res;
            }))) return;

            DnsList.Children.Clear();
            var cur = DnsTool.CurrentProvider();
            double? best = rows.Where(r => r.Item3.HasValue).Select(r => r.Item3).DefaultIfEmpty(null).Min();
            foreach (var r in rows)
            {
                var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
                g.Children.Add(new TextBlock { Text = r.Item2, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                bool fastest = r.Item3.HasValue && best.HasValue && r.Item3.Value == best.Value;
                var ms = new TextBlock { Text = r.Item3.HasValue ? r.Item3.Value.ToString("0") + " ms" : Loc.T("dns.noReply"), Style = (Style)FindResource("Figure"),
                    FontWeight = fastest ? FontWeights.SemiBold : FontWeights.Normal, Foreground = (System.Windows.Media.Brush)FindResource(fastest ? "Text" : "TextMuted"),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(ms, 1); g.Children.Add(ms);
                if (r.Item1 != null)
                {
                    bool inUse = cur != null && cur.Id == r.Item1;
                    var b = new Button { Content = inUse ? Loc.T("dns.inUse") : Loc.T("dns.use"), Style = (Style)FindResource("BtnBase"), Padding = new Thickness(10, 5, 10, 5),
                        HorizontalAlignment = HorizontalAlignment.Right, IsEnabled = !inUse && r.Item3.HasValue, Tag = r.Item1 };
                    System.Windows.Automation.AutomationProperties.SetName(b, Loc.T("dns.use") + " " + r.Item2);
                    b.Click += DnsUse_Click;
                    Grid.SetColumn(b, 2); g.Children.Add(b);
                }
                DnsList.Children.Add(g);
            }
            DnsList.Visibility = Visibility.Visible;
            RenderDnsNow();
        }

        async void DnsUse_Click(object sender, RoutedEventArgs e)
        {
            var p = DnsTool.Providers.FirstOrDefault(x => x.Id == (string)((Button)sender).Tag);
            if (p == null) return;
            if (!await Win.ConfirmAsync(Loc.F("dns.confirmTitle", p.Name), Loc.F("dns.confirmText", p.Primary, p.Secondary), null, Loc.T("dns.use"), Loc.T("ui.btn.cancel"))) return;
            string error = null;
            await AppState.Current.RunBusy(Loc.T("dns.busySet"), async () =>
            {
                try { await Task.Run(() => DnsTool.Set(p)); }
                catch (Exception ex) { error = ex.Message; }
            });
            AppState.Current.RaiseChangesApplied();
            if (error != null) await Win.ConfirmAsync(Loc.T("dns.err.title"), error, null, Loc.T("ui.btn.ok"), null);
            else Win.ShowToast(Loc.F("dns.set", p.Name), ToastKind.Success);
            DnsList.Visibility = Visibility.Collapsed;
            RenderDnsNow();
        }

        async void DnsRestore_Click(object sender, RoutedEventArgs e)
        {
            await Win.RestoreDnsAsync();
            DnsList.Visibility = Visibility.Collapsed;
            RenderDnsNow();
        }

        // ------------------------------------------------------------ Limpieza profunda

        readonly System.Collections.Generic.List<Tuple<CheckBox, CleanMeasure>> _clean = new System.Collections.Generic.List<Tuple<CheckBox, CleanMeasure>>();

        /// <summary>Mide cuánto ocupa cada tipo (no borra nada) y enseña la lista para elegir.</summary>
        async void Analyze_Click(object sender, RoutedEventArgs e)
        {
            System.Collections.Generic.List<CleanMeasure> list = null;
            if (!await AppState.Current.RunBusy(Loc.T("clean.busyAnalyze"), async () =>
                    list = await Task.Run(() => DeepCleaner.Categories().Select(DeepCleaner.Measure).ToList()))) return;
            RenderClean(list);
        }

        void RenderClean(System.Collections.Generic.List<CleanMeasure> list)
        {
            _clean.Clear();
            CleanList.Children.Clear();
            foreach (var m in list)
            {
                var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                bool can = m.Blocked == null && m.Bytes > 0;
                var cb = new CheckBox { Style = (Style)FindResource("Check"), IsChecked = can && m.Category.SelectedByDefault, IsEnabled = can, VerticalAlignment = VerticalAlignment.Top };
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = m.Category.Name, FontWeight = FontWeights.SemiBold });
                string note = m.Blocked ?? (m.SkippedApps.Count > 0 ? Loc.F("clean.skippedApps", string.Join(", ", m.SkippedApps)) : m.Category.Description);
                sp.Children.Add(new TextBlock { Text = note, Style = (Style)FindResource("Caption"), TextWrapping = TextWrapping.Wrap, Foreground = (System.Windows.Media.Brush)FindResource(m.Blocked != null ? "Warning" : "TextDim") });
                cb.Content = sp;
                cb.Checked += (s, a) => UpdateCleanButton();
                cb.Unchecked += (s, a) => UpdateCleanButton();
                System.Windows.Automation.AutomationProperties.SetName(cb, m.Category.Name + " " + Format.Bytes(m.Bytes));
                g.Children.Add(cb);
                var size = new TextBlock { Text = Format.Bytes(m.Bytes), Style = (Style)FindResource("Figure"), FontWeight = FontWeights.Normal, Foreground = (System.Windows.Media.Brush)FindResource(can ? "Text" : "TextDim"), Margin = new Thickness(12, 2, 0, 0), VerticalAlignment = VerticalAlignment.Top };
                Grid.SetColumn(size, 1); g.Children.Add(size);
                CleanList.Children.Add(g);
                _clean.Add(Tuple.Create(cb, m));
            }
            CleanList.Visibility = Visibility.Visible;
            TxtTemp.Visibility = Visibility.Collapsed;
            BtnTemp.Visibility = Visibility.Visible;
            BtnAnalyze.Content = Loc.T("clean.reanalyze");
            UpdateCleanButton();
        }

        void UpdateCleanButton()
        {
            long bytes = _clean.Where(t => t.Item1.IsChecked == true).Sum(t => t.Item2.Bytes);
            BtnTemp.Content = Loc.F("clean.cleanN", Format.Bytes(bytes));
            BtnTemp.IsEnabled = bytes > 0 && AppState.Current.IsIdle;
        }

        async void Temp_Click(object sender, RoutedEventArgs e)
        {
            var sel = _clean.Where(t => t.Item1.IsChecked == true).Select(t => t.Item2.Category).ToList();
            if (sel.Count == 0) return;
            long freed = 0;
            if (!await AppState.Current.RunBusy(Loc.T("ui.tools.tempBusy"), async () =>
            {
                foreach (var c in sel)
                {
                    AppState.Current.SetBusyText(Loc.F("clean.busyOne", c.Name));
                    freed += await Task.Run(() => DeepCleaner.Clean(c));
                }
            })) return;
            string done = Loc.F("clean.done", Format.Bytes(freed));
            Win.ShowToast(done, ToastKind.Success);
            // Se vuelve a medir para enseñar lo que ha quedado (archivos en uso)
            var list = await Task.Run(() => DeepCleaner.Categories().Select(DeepCleaner.Measure).ToList());
            RenderClean(list);
            TxtTemp.Text = done;
            TxtTemp.Visibility = Visibility.Visible;
        }

        async void Rp_Click(object sender, RoutedEventArgs e)
        {
            RestorePointOutcome r = null;
            if (AppState.Current.IsIdle) await Win.OfferSystemProtectionAsync();
            await AppState.Current.RunBusy(Loc.T("ui.busy.restorePoint"), async () => r = await RestorePointTool.CreateAsync("Velox"));
            if (r == null) return;
            TxtRp.Text = r.Message;
            TxtRp.Visibility = Visibility.Visible;
            Win.ShowToast(r.Message, r.Result == RestorePointResult.Failed ? ToastKind.Error : r.Result == RestorePointResult.Created ? ToastKind.Success : ToastKind.Info);
        }

        async void Explorer_Click(object sender, RoutedEventArgs e)
        {
            if (!await Win.ConfirmAsync(Loc.T("ui.tools.explorerTitle"), Loc.T("ui.tools.explorerConfirm"), null, Loc.T("ui.btn.continue"), Loc.T("ui.btn.cancel"))) return;
            bool ok = false;
            await AppState.Current.RunBusy(Loc.T("ui.tools.explorerBusy"), async () => ok = await Task.Run(() => ExplorerTool.Restart()));
            if (ok)
            {
                AppState.Current.NeedsLogoff = false;
                Win.ShowToast(Loc.T("ui.tools.explorerDone"), ToastKind.Success);
            }
            else await Win.ConfirmAsync(Loc.T("ui.tools.explorerTitle"), Loc.T("ui.tools.explorerManual"), null, Loc.T("ui.btn.ok"), null);
        }

        async void Undo_Click(object sender, RoutedEventArgs e)
        {
            await Win.UndoAllAsync();
            RefreshBackups(); UpdateButtons();
        }

        void OpenData_Click(object sender, RoutedEventArgs e) => Process.Start("explorer.exe", "\"" + AppPaths.DataDir + "\"");
    }
}
