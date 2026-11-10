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

        public void OnShown() { RefreshMemory(); RefreshBackups(); UpdateButtons(); }

        public void RefreshTexts()
        {
            BtnHz.Content = MouseHz.Running ? Loc.T("ui.tools.hzStop") : Loc.T("ui.tools.hzStart");
            if (!MouseHz.Running && _hzSamples.Count == 0) TxtHzInfo.Text = Loc.T("ui.tools.hzIdle");
            RefreshBackups();
        }

        void UpdateButtons()
        {
            bool idle = AppState.Current.IsIdle;
            BtnPurge.IsEnabled = BtnTemp.IsEnabled = BtnRp.IsEnabled = BtnExplorer.IsEnabled = idle;
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

        async void Temp_Click(object sender, RoutedEventArgs e)
        {
            long freed = 0;
            if (await AppState.Current.RunBusy(Loc.T("ui.tools.tempBusy"), async () => freed = await Task.Run(() => TempCleaner.Clean())))
            {
                string mb = (freed / 1048576.0).ToString("N0");
                TxtTemp.Text = Loc.F("ui.tools.tempDone", mb);
                TxtTemp.Visibility = Visibility.Visible;
                Win.ShowToast(Loc.F("ui.tools.tempDone", mb), ToastKind.Success);
            }
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
