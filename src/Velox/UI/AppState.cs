using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Velox.Core;
using Velox.Core.Optimizations;
using Velox.Core.Scan;
using Velox.Core.Tools;
using Velox.Localization;
using Velox.UI.Infra;
using Velox.UI.ViewModels;

namespace Velox.UI
{
    /// <summary>Estado global de la app: equipo escaneado, tareas en curso y cambios pendientes de reinicio.</summary>
    public sealed class AppState : ObservableObject
    {
        public static AppState Current { get; } = new AppState();

        SystemInfo _sys;
        bool _busy;
        string _busyText;
        bool _needsReboot, _needsLogoff;
        bool _restorePointDone;

        public SystemInfo Sys { get => _sys; private set => Set(ref _sys, value); }
        public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) Raise(nameof(IsIdle)); } }
        public bool IsIdle => !_busy;
        public string BusyText { get => _busyText; private set => Set(ref _busyText, value); }
        /// <summary>Actualiza el texto de la tarea en curso (progreso de descargas, pasos...).</summary>
        public void SetBusyText(string text) { if (IsBusy) BusyText = text; }
        public bool NeedsReboot { get => _needsReboot; set { if (Set(ref _needsReboot, value)) Raise(nameof(RestartNotice)); } }
        public bool NeedsLogoff { get => _needsLogoff; set { if (Set(ref _needsLogoff, value)) Raise(nameof(RestartNotice)); } }
        public string RestartNotice => NeedsReboot ? Loc.T("ui.notice.reboot") : NeedsLogoff ? Loc.T("ui.notice.logoff") : null;

        public OptimizationsVM Optimizations { get; } = new OptimizationsVM();
        public List<Finding> Findings { get; private set; } = new List<Finding>();

        public event Action ScanCompleted;

        MainWindow Win => Application.Current.MainWindow as MainWindow;

        /// <summary>Ejecuta una tarea larga mostrando el indicador de actividad. Solo una a la vez.</summary>
        public async Task<bool> RunBusy(string text, Func<Task> work)
        {
            if (IsBusy) { Win?.ShowToast(Loc.T("ui.msg.busy"), ToastKind.Warning); return false; }
            IsBusy = true; BusyText = text;
            try { await work(); return true; }
            catch (Exception ex)
            {
                Log.Error(text, ex);
                Win?.ShowToast(ex.Message, ToastKind.Error);
                return false;
            }
            finally { IsBusy = false; BusyText = null; }
        }

        public Task ScanAsync() => RunBusy(Loc.T("ui.busy.scanning"), async () =>
        {
            var progress = new Progress<string>(t => BusyText = t);
            var sys = await Task.Run(() => SystemScanner.Scan(progress));
            Sys = sys;
            Findings = await Task.Run(() => Core.Scan.Findings.Build(sys));
            await Optimizations.RefreshAsync(sys);
            Log.Info("Scan completed.");
            ScanCompleted?.Invoke();
        });

        public Task RefreshOptimizationsAsync() => Optimizations.RefreshAsync(Sys);

        /// <summary>Confirma y aplica los cambios pendientes (con punto de restauración si está activado).</summary>
        public async Task ApplyPendingAsync()
        {
            var pending = Optimizations.GetPending();
            if (pending.Count == 0) { Win?.ShowToast(Loc.T("ui.opts.nothingPending")); return; }

            var lines = pending.Select(p => (p.IsChecked ? "+  " + Loc.T("ui.opts.lineEnable") : "–  " + Loc.T("ui.opts.lineDisable")) + " " + p.Name);
            bool ok = await Win.ConfirmAsync(Loc.F("ui.opts.confirmTitle", pending.Count), Loc.T("ui.opts.confirmText"),
                string.Join("\n", lines), Loc.T("ui.btn.apply"), Loc.T("ui.btn.cancel"));
            if (!ok) return;

            var failed = new List<string>();
            int done = 0;
            await RunBusy(Loc.T("ui.busy.applying"), async () =>
            {
                if (Settings.Current.CreateRestorePoint && !_restorePointDone)
                {
                    BusyText = Loc.T("ui.busy.restorePoint");
                    var rp = await RestorePointTool.CreateAsync("Velox");
                    if (rp.Result == RestorePointResult.Failed)
                    {
                        bool go = await Win.ConfirmAsync(Loc.T("ui.rp.failedTitle"), rp.Message + "\n\n" + Loc.T("ui.rp.continue"), null, Loc.T("ui.btn.continue"), Loc.T("ui.btn.cancel"));
                        if (!go) { Win.ShowToast(Loc.T("ui.msg.cancelled"), ToastKind.Warning); return; }
                    }
                    else _restorePointDone = true;
                }

                var sys = Sys;
                foreach (var p in pending)
                {
                    BusyText = (p.IsChecked ? Loc.T("ui.busy.enabling") : Loc.T("ui.busy.disabling")) + " " + p.Name;
                    bool enable = p.IsChecked;
                    try
                    {
                        await Task.Run(() => { if (enable) OptimizationEngine.Apply(p.Opt, sys); else OptimizationEngine.Revert(p.Opt, sys); });
                        done++;
                        if (p.Opt.Restart == RestartNeed.Reboot) NeedsReboot = true;
                        else if (p.Opt.Restart == RestartNeed.Logoff) NeedsLogoff = true;
                    }
                    catch (Exception ex)
                    {
                        failed.Add(p.Name + ": " + ex.Message);
                        Log.Error("Apply " + p.Opt.Id, ex);
                    }
                }
                BusyText = Loc.T("ui.busy.checking");
                await Optimizations.RefreshAsync(sys);
            });

            if (failed.Count == 0) Win.ShowToast(Loc.F("ui.opts.appliedOk", done), ToastKind.Success);
            else await Win.ConfirmAsync(Loc.F("ui.opts.appliedSome", done, failed.Count), Loc.T("ui.opts.failedText"), string.Join("\n", failed), Loc.T("ui.btn.ok"), null);
        }

        public async Task ApplyRecommendedAsync()
        {
            int n = Optimizations.SelectRecommended();
            Win?.Navigate("optimizations");
            if (n > 0) await ApplyPendingAsync();
            else Win?.ShowToast(Loc.T("ui.opts.alreadyRecommended"), ToastKind.Success);
        }
    }
}
