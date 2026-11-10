using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Velox.Core;
using Velox.Core.Backup;
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
            await RefreshRevertedAsync();
            if (!_rebootChecked) { _rebootChecked = true; RebootVerdicts = await Task.Run(() => CheckAfterReboot()); Raise(nameof(RebootVerdicts)); }
            Log.Info("Scan completed." + (Reverted.Count > 0 ? " Reverted since last time: " + string.Join(", ", Reverted.Select(o => o.Id)) + "." : ""));
            ScanCompleted?.Invoke();
        });

        public Task RefreshOptimizationsAsync() => Optimizations.RefreshAsync(Sys);

        /// <summary>
        /// Tras aplicar o deshacer cambios: relee lo que esos cambios mueven (plan de energía y Hz de las pantallas)
        /// para que el Resumen no enseñe datos del último escaneo.
        /// </summary>
        public async Task RefreshLiveSysAsync()
        {
            var sys = Sys;
            if (sys == null) return;
            try
            {
                var live = await Task.Run(() => new { Plan = Core.Sys.PowerCfg.ActiveSchemeName(), Displays = Native.Display.GetDisplays() });
                if (!string.IsNullOrEmpty(live.Plan)) sys.PowerScheme = live.Plan;
                if (live.Displays != null && live.Displays.Count > 0) sys.Displays = live.Displays;
            }
            catch (Exception ex) { Log.Warn("Live system info: " + ex.Message); }
        }

        /// <summary>Confirma y aplica los cambios pendientes de Optimizaciones (con punto de restauración si está activado).</summary>
        public async Task ApplyPendingAsync(string source = "ui")
        {
            var pending = Optimizations.GetPending();
            if (pending.Count == 0) { Win?.ShowToast(Loc.T("ui.opts.nothingPending")); return; }
            await ApplyChangesAsync(pending.Select(p => new OptChange(p.Opt, p.IsChecked)).ToList(), source,
                Loc.F("ui.opts.confirmTitle", pending.Count), Loc.T("ui.opts.confirmText"));
        }

        /// <summary>
        /// Aplica una lista de cambios (optimizaciones y ajustes de Windows): confirmación con las líneas y los ⚠ de riesgo,
        /// punto de restauración, cada cambio con su copia, historial y relectura del estado. Lo usan Optimizaciones, el
        /// modo Recomendado, los ajustes revertidos y los perfiles. Devuelve true si se llegó a aplicar algo.
        /// </summary>
        public async Task<bool> ApplyChangesAsync(List<OptChange> changes, string source, string title, string text)
        {
            if (changes == null || changes.Count == 0) return false;
            var lines = changes.Select(c => (c.Enable ? "+  " + Loc.T("ui.opts.lineEnable") : "–  " + Loc.T("ui.opts.lineDisable")) + " " + c.Opt.Name
                                            + (c.Enable && c.Opt.IsRisky ? "   ⚠ " + Loc.T("ui.badge.risk") : ""));
            bool ok = await Win.ConfirmAsync(title, text, string.Join("\n", lines), Loc.T("ui.btn.apply"), Loc.T("ui.btn.cancel"),
                                             changes.Any(c => c.Enable && c.Opt.IsRisky));
            if (!ok) return false;
            if (Settings.Current.CreateRestorePoint && !_restorePointDone) await Win.OfferSystemProtectionAsync();

            var failed = new List<string>();
            int done = 0;
            bool cancelled = false;
            bool ran = await RunBusy(Loc.T("ui.busy.applying"), async () =>
            {
                if (Settings.Current.CreateRestorePoint && !_restorePointDone)
                {
                    BusyText = Loc.T("ui.busy.restorePoint");
                    var rp = await RestorePointTool.CreateAsync("Velox");
                    if (rp.Result == RestorePointResult.Failed)
                    {
                        bool go = await Win.ConfirmAsync(Loc.T("ui.rp.failedTitle"), rp.Message + "\n\n" + Loc.T("ui.rp.continue"), null, Loc.T("ui.btn.continue"), Loc.T("ui.btn.cancel"));
                        if (!go) { cancelled = true; Win.ShowToast(Loc.T("ui.msg.cancelled"), ToastKind.Warning); return; }
                    }
                    else _restorePointDone = true;
                }

                var sys = Sys;
                foreach (var c in changes)
                {
                    BusyText = (c.Enable ? Loc.T("ui.busy.enabling") : Loc.T("ui.busy.disabling")) + " " + c.Opt.Name;
                    try
                    {
                        await Task.Run(() => { if (c.Enable) OptimizationEngine.Apply(c.Opt, sys, source); else OptimizationEngine.Revert(c.Opt, sys, source); });
                        done++;
                        if (c.Opt.Restart == RestartNeed.Reboot) NeedsReboot = true;
                        else if (c.Opt.Restart == RestartNeed.Logoff) NeedsLogoff = true;
                    }
                    catch (Exception ex)
                    {
                        failed.Add(c.Opt.Name + ": " + ex.Message);
                        Log.Error("Apply " + c.Opt.Id, ex);
                    }
                }
                BusyText = Loc.T("ui.busy.checking");
                await RefreshLiveSysAsync();
                await Optimizations.RefreshAsync(sys);
                await RefreshRevertedAsync();
            });

            // Cancelado (o había otra tarea en curso): no se aplicó nada, así que no hay resultado que anunciar
            if (!ran || cancelled) return false;
            ChangesApplied?.Invoke();
            if (failed.Count == 0) Win.ShowToast(Loc.F("ui.opts.appliedOk", done), ToastKind.Success);
            else await Win.ConfirmAsync(Loc.F("ui.opts.appliedSome", done, failed.Count), Loc.T("ui.opts.failedText"), string.Join("\n", failed), Loc.T("ui.btn.ok"), null);
            return done > 0;
        }

        /// <summary>Después de aplicar o deshacer (para que las páginas que no son Optimizaciones se refresquen).</summary>
        public event Action ChangesApplied;
        public void RaiseChangesApplied() => ChangesApplied?.Invoke();

        public async Task ApplyRecommendedAsync()
        {
            int n = Optimizations.SelectRecommended();
            Win?.Navigate("optimizations");
            if (n > 0) await ApplyPendingAsync("recommended");
            else Win?.ShowToast(Loc.T("ui.opts.alreadyRecommended"), ToastKind.Success);
        }

        // ------------------------------------------------------------ Perfiles e importar configuración

        /// <summary>
        /// Aplica un perfil o un archivo exportado: solo lo que difiere de este equipo, con la confirmación de siempre
        /// (los ajustes de riesgo llevan ⚠ y el botón en rojo). Después guarda las opciones de Memoria y las respuestas del
        /// asistente, y deja marcadas en Instalar las apps que falten (no instala nada sin que lo pidas).
        /// </summary>
        public async Task ImportProfileAsync(Core.Profiles.VeloxProfile p)
        {
            if (p == null || Sys == null) return;
            Dictionary<string, string> installed = null;
            if (p.Apps != null && p.Apps.Count > 0 && Core.Apps.Winget.IsAvailable)
            {
                if (!await RunBusy(Loc.T("prof.busyApps"), async () => installed = await Task.Run(() => Core.Apps.Winget.GetInstalled()))) return;
            }
            var plan = await Task.Run(() => Core.Profiles.ProfileStore.Plan(p, Sys, installed));
            Log.Info("Profile " + p.Name + ": " + plan.Changes.Count + " change(s), " + plan.UnchangedCount + " already set, "
                     + plan.Skipped.Count + " skipped, " + plan.AppsToInstall.Count + " app(s) to install.");

            string skipped = plan.Skipped.Count == 0 ? "" : "\n\n" + Loc.F("prof.skipped", plan.Skipped.Count) + " " + string.Join(" · ", plan.Skipped.Take(6)) + (plan.Skipped.Count > 6 ? " …" : "");
            if (plan.Changes.Count > 0)
            {
                bool applied = await ApplyChangesAsync(plan.Changes, "profile", Loc.F("prof.confirmTitle", p.Name, plan.Changes.Count),
                    Loc.F("prof.confirmText", plan.UnchangedCount) + skipped);
                if (!applied) return; // cancelado: tampoco se tocan las preferencias ni las apps
            }
            else Win?.ShowToast(Loc.F("prof.nothing", p.Name), ToastKind.Success);

            var st = Settings.Current;
            if (p.Memory != null)
            {
                st.MemWorkingSets = p.Memory.WorkingSets; st.MemModified = p.Memory.Modified; st.MemFileCache = p.Memory.FileCache;
                st.MemStandby = p.Memory.Standby; st.MemCombine = p.Memory.Combine; st.AutoMemClean = p.Memory.AutoClean;
                st.AutoMemThreshold = Math.Max(5, Math.Min(50, p.Memory.AutoThreshold));
                MemoryAutoCleaner.Apply();
            }
            if (p.Answers != null)
            {
                st.UserLaptop = p.Answers.Laptop; st.UserCompetitive = p.Answers.Competitive; st.UserObs = p.Answers.Obs; st.WizardDone = true;
                Sys.ApplyAnswers(st.UserLaptop, st.UserCompetitive, st.UserObs);
                await RefreshOptimizationsAsync();
            }
            Settings.Save();
            ChangesApplied?.Invoke();

            if (plan.AppsToInstall.Count > 0)
            {
                Views.AppsView.PendingImport = plan.AppsToInstall;
                Win?.Navigate("apps");
                Win?.ShowToast(Loc.F("prof.appsMarked", plan.AppsToInstall.Count));
            }
        }

        // ------------------------------------------------------------ Ajustes revertidos y comprobación tras reiniciar

        /// <summary>Ajustes que Velox dejó aplicados y que ya no lo están (Windows Update, otro programa o la persona).</summary>
        public List<Optimization> Reverted { get; private set; } = new List<Optimization>();
        /// <summary>Resultado de la comprobación tras reiniciar (una vez por arranque de Velox).</summary>
        public List<RebootVerdict> RebootVerdicts { get; private set; } = new List<RebootVerdict>();
        bool _rebootChecked;

        /// <summary>Recalcula los ajustes revertidos en segundo plano y avisa en el hilo de la interfaz.</summary>
        public async Task RefreshRevertedAsync()
        {
            var sys = Sys;
            Reverted = await Task.Run(() =>
            {
                try { return OptimizationEngine.Reverted(sys); }
                catch (Exception ex) { Log.Warn("Reverted check: " + ex.Message); return new List<Optimization>(); }
            });
            Raise(nameof(Reverted));
        }

        /// <summary>Se llama una vez por arranque de Velox, en segundo plano (no toca la interfaz).</summary>
        static List<RebootVerdict> CheckAfterReboot()
        {
            try
            {
                return RebootCheck.CheckNow(
                    id => { var o = OptimizationEngine.Find(id); return o == null ? (bool?)null : OptimizationEngine.IsApplied(o); },
                    id => { var o = OptimizationEngine.Find(id); try { return o?.VerifyAfterReboot == null ? (bool?)null : o.VerifyAfterReboot(); } catch { return null; } });
            }
            catch (Exception ex) { Log.Warn("Reboot check: " + ex.Message); return new List<RebootVerdict>(); }
        }

#if UITEST
        /// <summary>Solo capturas: simula dos ajustes revertidos y una comprobación tras reiniciar (no toca Windows).</summary>
        public void TestFakeNotices()
        {
            Reverted = Catalog.All.Where(o => o.Id == "GameDVR" || o.Id == "Telemetry").ToList();
            RebootVerdicts = new List<RebootVerdict>
            {
                new RebootVerdict { Id = "HAGS", On = true, Kept = true },
                new RebootVerdict { Id = "MemoryIntegrity", On = true, Kept = true, Effective = true },
            };
            Raise(nameof(Reverted)); Raise(nameof(RebootVerdicts));
        }
#endif

        public void DismissRebootVerdicts() { RebootVerdicts = new List<RebootVerdict>(); Raise(nameof(RebootVerdicts)); }

        /// <summary>Vuelve a aplicar los ajustes que se han revertido (con la misma confirmación de siempre).</summary>
        public async Task ReapplyRevertedAsync()
        {
            var list = Reverted.ToList();
            if (list.Count == 0) return;
            await ApplyChangesAsync(list.Select(o => new OptChange(o, true)).ToList(), "reapply",
                Loc.F("ui.rev.confirmTitle", list.Count), Loc.T("ui.rev.confirmText"));
        }

        /// <summary>«Dejarlos así»: se acepta el valor base y se descarta la copia (queda en el historial).</summary>
        public async Task AcceptRevertedAsync()
        {
            var list = Reverted.ToList();
            if (list.Count == 0) return;
            await RunBusy(Loc.T("ui.busy.applying"), async () =>
            {
                var sys = Sys;
                await Task.Run(() => { foreach (var o in list) { try { OptimizationEngine.Revert(o, sys, "accept-base"); } catch (Exception ex) { Log.Warn("Accept base " + o.Id + ": " + ex.Message); } } });
                await Optimizations.RefreshAsync(sys);
                await RefreshRevertedAsync();
            });
            ChangesApplied?.Invoke();
        }
    }
}
