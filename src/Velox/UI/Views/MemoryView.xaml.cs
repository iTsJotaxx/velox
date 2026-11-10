using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Velox.Core;
using Velox.Core.Tools;
using Velox.Localization;
using Velox.UI.Infra;

namespace Velox.UI.Views
{
    public sealed class ProcMemRow
    {
        public ProcMem Proc { get; set; }
        public string Name { get; set; }
        public string CountText { get; set; }
        public string SizeText { get; set; }
        public double BarWidth { get; set; }
    }

    /// <summary>Memoria RAM: estado por listas, liberar sin cerrar programas, limpieza automática y procesos que más usan.</summary>
    public partial class MemoryView : UserControl, IPage
    {
        readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        bool _loading, _sampling;
        int _ticks;
        MemCleanResult _last;

        public MemoryView()
        {
            InitializeComponent();
            _timer.Tick += (s, e) => Sample();
            IsVisibleChanged += (s, e) => { if (IsVisible) _timer.Start(); else _timer.Stop(); };
            AppState.Current.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(AppState.IsBusy)) BtnClean.IsEnabled = AppState.Current.IsIdle; };
            MemoryAutoCleaner.Cleaned += r => Dispatcher.BeginInvoke(new Action(() => { _last = r; ShowLast(); UpdateAutoState(); }));
            LoadSettings();
        }

        MainWindow Win => (MainWindow)Window.GetWindow(this);

        public void OnShown()
        {
            _ticks = 0;
            Sample();
            LoadTop();
            UpdateAutoState();
            ShowLast();
        }

        public void RefreshTexts()
        {
            UpdateThresholdText();
            UpdateAutoState();
            ShowLast();
            Sample();
            LoadTop();
        }

        void LoadSettings()
        {
            _loading = true;
            var s = Settings.Current;
            ChkWs.IsChecked = s.MemWorkingSets;
            ChkStandby.IsChecked = s.MemStandby;
            ChkModified.IsChecked = s.MemModified;
            ChkCache.IsChecked = s.MemFileCache;
            ChkCombine.IsChecked = s.MemCombine;
            ChkAuto.IsChecked = s.AutoMemClean;
            SldThreshold.Value = Math.Max(5, Math.Min(50, s.AutoMemThreshold));
            UpdateThresholdText();
            _loading = false;
        }

        // ------------------------------------------------------------ Estado

        void Sample()
        {
            if (_sampling) return;
            _sampling = true;
            Task.Run(() => MemoryTool.Snapshot()).ContinueWith(t =>
            {
                _sampling = false;
                if (!t.IsFaulted) Show(t.Result);
                if (++_ticks % 4 == 0) LoadTop();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void Show(MemSnapshot s)
        {
            if (s.Total <= 0) return;
            double pct = s.UsedPct;
            TxtPct.Text = Math.Round(pct) + " %";
            Gauge.BeginAnimation(ArcGauge.ValueProperty, new DoubleAnimation(pct, TimeSpan.FromMilliseconds(450)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            TxtHeadline.Text = Loc.F("mem.headline", MemSnapshot.Gb(s.FreeOrAvailable), MemSnapshot.Gb(s.Total));
            TxtSubline.Text = s.HasLists ? Loc.F("mem.subline", MemSnapshot.Gb(s.Available), MemSnapshot.Gb(s.Standby)) : Loc.T("mem.noLists");

            double use = s.InUse, mod = s.HasLists ? s.Modified : 0, stb = s.HasLists ? s.Standby : 0, free = s.HasLists ? s.Free : s.Available;
            ColUse.Width = new GridLength(Math.Max(0, use), GridUnitType.Star);
            ColMod.Width = new GridLength(Math.Max(0, mod), GridUnitType.Star);
            ColStb.Width = new GridLength(Math.Max(0, stb), GridUnitType.Star);
            ColFree.Width = new GridLength(Math.Max(0, free), GridUnitType.Star);
            TxtUse.Text = MemSnapshot.Gb(use);
            TxtMod.Text = s.HasLists ? MemSnapshot.Gb(mod) : "--";
            TxtStb.Text = s.HasLists ? MemSnapshot.Gb(stb) : "--";
            TxtFree.Text = MemSnapshot.Gb(free);
        }

        void LoadTop()
        {
            Task.Run(() => MemoryTool.TopProcesses(8)).ContinueWith(t =>
            {
                if (t.IsFaulted || t.Result.Count == 0) return;
                double max = Math.Max(1, t.Result.Max(p => p.Bytes));
                TopList.ItemsSource = t.Result.Select(p => new ProcMemRow
                {
                    Proc = p,
                    Name = p.Name,
                    CountText = p.Count > 1 ? "  ×" + p.Count : "",
                    SizeText = (p.Bytes / 1048576.0).ToString("N0") + " MB",
                    BarWidth = Math.Max(2, p.Bytes * 230 / max),
                }).ToList();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void ShowLast()
        {
            var r = _last ?? MemoryAutoCleaner.LastResult;
            if (r == null) { TxtLast.Text = Loc.T("mem.lastNone"); return; }
            TxtLast.Text = Loc.F("mem.last", r.When.ToString("t"), MemSnapshot.Gb(r.Gained), MemSnapshot.Gb(r.After.FreeOrAvailable));
        }

#if UITEST
        public void TestClean() => Clean_Click(this, new RoutedEventArgs());
#endif

        // ------------------------------------------------------------ Liberar

        async void Clean_Click(object sender, RoutedEventArgs e)
        {
            var opts = MemCleanOptions.FromSettings();
            if (!opts.Any) { Win.ShowToast(Loc.T("mem.noneSelected"), ToastKind.Warning); return; }
            MemCleanResult r = null;
            await AppState.Current.RunBusy(Loc.T("ui.tools.memBusy"), async () => r = await Task.Run(() => MemoryTool.Clean(opts)));
            if (r == null) return;
            _last = r;
            Show(r.After);
            ShowLast();
            LoadTop();
            if (r.Failed.Count == 0) Win.ShowToast(Loc.F("mem.done", MemSnapshot.Gb(r.Gained), MemSnapshot.Gb(r.After.FreeOrAvailable)), ToastKind.Success);
            else await Win.ConfirmAsync(Loc.F("mem.donePartial", MemSnapshot.Gb(r.Gained)), Loc.T("mem.partialText"), string.Join("\n", r.Failed), Loc.T("ui.btn.ok"), null);
        }

        async void Trim_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is ProcMemRow row)) return;
            try
            {
                double freed = await Task.Run(() => MemoryTool.TrimProcesses(row.Proc));
                Win.ShowToast(Loc.F("mem.trimmed", row.Name, MemSnapshot.Gb(freed)), ToastKind.Success);
            }
            catch (Exception ex) { Win.ShowToast(ex.Message, ToastKind.Error); }
            LoadTop();
            Sample();
        }

        // ------------------------------------------------------------ Preferencias

        void Opt_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var s = Settings.Current;
            s.MemWorkingSets = ChkWs.IsChecked == true;
            s.MemStandby = ChkStandby.IsChecked == true;
            s.MemModified = ChkModified.IsChecked == true;
            s.MemFileCache = ChkCache.IsChecked == true;
            s.MemCombine = ChkCombine.IsChecked == true;
            Settings.Save();
        }

        void Auto_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            Settings.Current.AutoMemClean = ChkAuto.IsChecked == true;
            Settings.Save();
            MemoryAutoCleaner.Apply();
            UpdateAutoState();
            Log.Info("Automatic RAM clean " + (Settings.Current.AutoMemClean ? "enabled" : "disabled") + " (threshold " + Settings.Current.AutoMemThreshold + " %).");
        }

        void Threshold_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateThresholdText();
            if (_loading || !IsLoaded) return;
            Settings.Current.AutoMemThreshold = (int)Math.Round(SldThreshold.Value);
            Settings.Save();
            UpdateAutoState();
        }

        void UpdateThresholdText()
        {
            if (TxtThreshold != null) TxtThreshold.Text = Math.Round(SldThreshold.Value) + " %";
        }

        void UpdateAutoState()
        {
            if (TxtAutoState == null) return;
            if (!Settings.Current.AutoMemClean) { TxtAutoState.Text = Loc.T("mem.autoOff"); return; }
            var r = MemoryAutoCleaner.LastResult;
            TxtAutoState.Text = r == null
                ? Loc.F("mem.autoOn", Settings.Current.AutoMemThreshold)
                : Loc.F("mem.autoLast", Settings.Current.AutoMemThreshold, r.When.ToString("t"), MemSnapshot.Gb(r.Gained));
        }
    }
}
