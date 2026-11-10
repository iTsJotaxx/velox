using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Velox.Core;
using Velox.Localization;
using Velox.UI.Infra;
using Velox.UI.Views;

namespace Velox.UI
{
    public enum ToastKind { Info, Success, Warning, Error }

    /// <summary>Las páginas que muestran textos generados en código implementan esto para el cambio de idioma.</summary>
    public interface IPage
    {
        void OnShown();
        void RefreshTexts();
    }

    public partial class MainWindow : Window
    {
        readonly Dictionary<string, FrameworkElement> _pages = new Dictionary<string, FrameworkElement>();
        readonly MonitorService _monitor = new MonitorService();
        TaskCompletionSource<bool> _dialog;
        string _current;

        public MainWindow()
        {
            InitializeComponent();
            TxtVersion.Text = "v" + AppInfo.Version;
#if UITEST
            if (!string.IsNullOrEmpty(App.TestSize))
            {
                var p = App.TestSize.Split('x');
                Width = double.Parse(p[0]); Height = double.Parse(p[1]);
            }
#endif
            SourceInitialized += (s, e) => WindowEffects.Apply(this, Color.FromRgb(0x2C, 0x2F, 0x34), Color.FromRgb(0x10, 0x11, 0x13));
            StateChanged += (s, e) => UpdateMaximized();
            Loaded += OnLoaded;
            Closing += OnClosing;

            AppState.Current.PropertyChanged += OnStateChanged;
            Loc.LanguageChanged += OnLanguageChanged;
            _monitor.Sampled += OnMonitorSample;
            // Atajos Ctrl+1…9 para las primeras secciones (se muestran en el tooltip)
            var nav = ShortcutNav();
            for (int i = 0; i < nav.Length; i++) nav[i].ToolTip = "Ctrl+" + (i + 1);
        }

        RadioButton[] ShortcutNav() => new[] { NavSummary, NavOptimizations, NavProcesses, NavMemory, NavStartup, NavApps, NavUninstall, NavDrivers, NavDisk };

        async void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateMaximized();
            Navigate(App.StartPage);
            if (!string.IsNullOrEmpty(App.DiskPath) && _pages.TryGetValue("disk", out var dp) && dp is DiskView dv)
                dv.StartScan(App.DiskPath, 0);
            _monitor.Start();
            Core.Tools.MemoryAutoCleaner.Apply();
#if UITEST
            if (!string.IsNullOrEmpty(App.ShotPath)) RunTestShot();
#endif
            await AppState.Current.ScanAsync();
        }

        void OnClosing(object sender, CancelEventArgs e)
        {
            if (AppState.Current.IsBusy)
            {
                e.Cancel = true;
                ShowToast(Loc.T("ui.msg.waitBusy"), ToastKind.Warning);
                return;
            }
            _monitor.Stop();
            Core.Tools.MemoryAutoCleaner.Stop();
            try { Native.MouseHz.Stop(); } catch { }
        }

        // ------------------------------------------------------------ Navegación

        FrameworkElement CreatePage(string key)
        {
            switch (key)
            {
                case "summary": return new SummaryView();
                case "optimizations": return new OptimizationsView();
                case "startup": return new StartupView();
                case "processes": return new ProcessesView();
                case "apps": return new AppsView();
                case "uninstall": return new UninstallView();
                case "drivers": return new DriversView();
                case "diagnostics": return new DiagnosticsView();
                case "memory": return new MemoryView();
                case "disk": return new DiskView();
                case "tweaks": return new TweaksView();
                case "tools": return new ToolsView();
                case "log": return new LogView();
                case "settings": return new SettingsView();
                default: return new SummaryView();
            }
        }

        RadioButton NavFor(string key)
        {
            switch (key)
            {
                case "optimizations": return NavOptimizations;
                case "startup": return NavStartup;
                case "processes": return NavProcesses;
                case "apps": return NavApps;
                case "uninstall": return NavUninstall;
                case "drivers": return NavDrivers;
                case "diagnostics": return NavDiagnostics;
                case "memory": return NavMemory;
                case "disk": return NavDisk;
                case "tweaks": return NavTweaks;
                case "tools": return NavTools;
                case "log": return NavSettings; // el registro se abre desde Ajustes
                case "settings": return NavSettings;
                default: return NavSummary;
            }
        }

        public void Navigate(string key)
        {
            NavFor(key).IsChecked = true;
            if (_current == key) return;
            if (_current == "tools" && Native.MouseHz.Running) Native.MouseHz.Stop();
            _current = key;
            if (!_pages.TryGetValue(key, out var page)) { page = CreatePage(key); _pages[key] = page; }
            PageHost.Content = page;
            (page as IPage)?.OnShown();

            // Transición: aparece desde abajo con fundido
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            PageShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        }

        void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.CommandParameter is string key) Navigate(key);
        }

#if UITEST
        // ------------------------------------------------------------ Pruebas automáticas (solo compilación UITEST)

        async void RunTestShot()
        {
            await Task.Delay(TimeSpan.FromSeconds(App.ShotDelay));
            try
            {
                if (_current == "disk" && App.DiskEnter > 0 && PageHost.Content is DiskView dv)
                {
                    for (int i = 0; i < App.DiskEnter; i++) { Log.Info("UITEST: enter " + i); dv.TestEnterBiggest(); Log.Info("UITEST: entered " + i); await Task.Delay(700); }
                }
                if (_current == "memory" && App.TestAction == "clean" && PageHost.Content is MemoryView mv)
                {
                    while (AppState.Current.IsBusy) await Task.Delay(250); // el escaneo inicial
                    mv.TestClean();
                    await Task.Delay(500);
                    for (int i = 0; i < 240 && AppState.Current.IsBusy; i++) await Task.Delay(250);
                    await Task.Delay(1800);
                }
                if (_current == "apps" && App.TestAction != null && PageHost.Content is AppsView av)
                {
                    // --action=select:Id1,Id2 | add:texto | install:Id1,Id2;autoconfirm
                    string act = App.TestAction.Split(';')[0];
                    await Task.Delay(9000); // lectura de lo instalado (winget export)
                    if (act.StartsWith("select:")) av.TestSelect(act.Substring(7).Split(','));
                    else if (act.StartsWith("add:")) { av.TestOpenAdd(act.Substring(4)); await Task.Delay(6000); }
                    else if (act.StartsWith("install:"))
                    {
                        av.TestSelect(act.Substring(8).Split(','));
                        av.TestInstall();
                        await Task.Delay(1500);
                        for (int i = 0; i < 2400 && AppState.Current.IsBusy; i++) await Task.Delay(500); // hasta 20 min
                        await Task.Delay(12000); // relectura de lo instalado
                    }
                }
                if (_current == "uninstall" && App.TestAction != null && PageHost.Content is UninstallView uv)
                {
                    // --action=store | select:Nombre1,Nombre2 | uninstall:Nombre1,Nombre2;autoconfirm
                    string act = App.TestAction.Split(';')[0];
                    await Task.Delay(4000);
                    if (act == "store") { uv.TestStore(); await Task.Delay(8000); }
                    else if (act.StartsWith("storeuninstall:"))
                    {
                        uv.TestStore(); await Task.Delay(9000);
                        uv.TestSelect(act.Substring(15).Split(','));
                        uv.TestUninstall();
                        await Task.Delay(1500);
                        for (int i = 0; i < 1200 && AppState.Current.IsBusy; i++) await Task.Delay(500);
                        await Task.Delay(2000);
                    }
                    else if (act.StartsWith("select:")) uv.TestSelect(act.Substring(7).Split(','));
                    else if (act.StartsWith("uninstall:"))
                    {
                        uv.TestSelect(act.Substring(10).Split(','));
                        uv.TestUninstall();
                        await Task.Delay(1500);
                        for (int i = 0; i < 1200 && AppState.Current.IsBusy; i++) await Task.Delay(500);
                        await Task.Delay(2000);
                    }
                }
                if (_current == "optimizations" && App.TestAction != null && PageHost.Content is OptimizationsView ov)
                {
                    // --action=expand:categoría | risk:Id (despliega su categoría y la activa: sale el aviso)
                    for (int i = 0; i < 120 && AppState.Current.IsBusy; i++) await Task.Delay(250);
                    string act = App.TestAction;
                    if (act.StartsWith("expand:")) ov.TestExpand(act.Substring(7));
                    else if (act.StartsWith("risk:")) ov.TestToggle(act.Substring(5));
                    else if (act.StartsWith("pending:")) ov.TestPending(act.Substring(8));
                    else if (act.StartsWith("search:")) ov.TestSearch(act.Substring(7));
                    await Task.Delay(900);
                }
                if (_current == "drivers" && App.TestAction == "safety" && PageHost.Content is DriversView drv)
                {
                    drv.TestSafety();
                    await Task.Delay(1500);
                }
                if (App.TestAction == "todo")
                {
                    // Solo pruebas: simula que 3 ajustes recomendados están sin aplicar (no toca Windows)
                    for (int i = 0; i < 120 && AppState.Current.IsBusy; i++) await Task.Delay(250);
                    foreach (var it in AppState.Current.Optimizations.AllItems.Where(x => x.IsScored && x.IsRecommended && x.IsApplied).Take(3).ToList())
                    { it.IsApplied = false; it.SetCheckedSilently(false); }
                    (PageHost.Content as IPage)?.OnShown();
                    await Task.Delay(500);
                }
                if (App.TestAction != null && App.TestAction.StartsWith("tab:"))
                {
                    // --action=tab:N mueve el foco N veces con Tab (comprueba el anillo de foco)
                    for (int i = 0; i < 120 && AppState.Current.IsBusy; i++) await Task.Delay(250);
                    int n = int.Parse(App.TestAction.Substring(4));
                    for (int i = 0; i < n; i++)
                        ((System.Windows.Input.Keyboard.FocusedElement as UIElement) ?? PageHost).MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
                    await Task.Delay(500);
                }
                if (App.TestAction == "protection") { _ = OfferSystemProtectionAsync(); await Task.Delay(1200); }
                if (App.TestAction != null && App.TestAction.StartsWith("dialog")) { _ = ConfirmAsync("Prueba de diálogo", "Mensaje de prueba", "Detalle 1\nDetalle 2", "Aceptar", "Cancelar", true); await Task.Delay(600); }
                await Task.Delay(400);
                Log.Info("UITEST: rendering");
                UpdateLayout();
                var dpi = VisualTreeHelper.GetDpi(this);
                var target = (FrameworkElement)Content;
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)Math.Ceiling(target.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(target.ActualHeight * dpi.DpiScaleY),
                    dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                var dv2 = new DrawingVisual();
                using (var dc = dv2.RenderOpen())
                {
                    dc.DrawRectangle((Brush)FindResource("Bg"), null, new Rect(0, 0, target.ActualWidth, target.ActualHeight));
                    dc.DrawRectangle(new VisualBrush(target), null, new Rect(0, 0, target.ActualWidth, target.ActualHeight));
                }
                rtb.Render(dv2);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using (var fs = System.IO.File.Create(App.ShotPath)) enc.Save(fs);
                Log.Info("UITEST shot saved: " + App.ShotPath);
            }
            catch (Exception ex) { Log.Error("UITEST shot", ex); }
            Application.Current.Shutdown();
        }

#endif
        // ------------------------------------------------------------ Barra de título

        void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        void Max_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        void Close_Click(object sender, RoutedEventArgs e) => Close();

        void UpdateMaximized()
        {
            bool max = WindowState == WindowState.Maximized;
            // Con WindowChrome la ventana maximizada se sale unos píxeles por cada lado
            RootBorder.Margin = max ? new Thickness(7) : new Thickness(0);
            BtnMax.Content = max ? "" : "";
        }

        // ------------------------------------------------------------ Estado global

        void OnStateChanged(object sender, PropertyChangedEventArgs e)
        {
            var st = AppState.Current;
            if (e.PropertyName == nameof(AppState.IsBusy)) SetBusy(st.IsBusy);
            else if (e.PropertyName == nameof(AppState.BusyText)) TxtBusy.Text = st.BusyText;
            else if (e.PropertyName == nameof(AppState.RestartNotice)) UpdateRestartChip();
        }

        void UpdateRestartChip()
        {
            string t = AppState.Current.RestartNotice;
            RestartChip.Visibility = string.IsNullOrEmpty(t) ? Visibility.Collapsed : Visibility.Visible;
            TxtRestart.Text = t;
        }

        void SetBusy(bool on)
        {
            BusyPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            BusyTrack.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on)
            {
                Cursor = null;
                double w = Math.Max(400, PageHost.ActualWidth);
                var bar = new DoubleAnimation(-220, w, TimeSpan.FromMilliseconds(1300)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                BusyBarX.BeginAnimation(TranslateTransform.XProperty, bar);
                SpinnerRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
            }
            else
            {
                BusyBarX.BeginAnimation(TranslateTransform.XProperty, null);
                SpinnerRot.BeginAnimation(RotateTransform.AngleProperty, null);
            }
        }

        void OnLanguageChanged()
        {
            AppState.Current.Optimizations.RefreshTexts();
            UpdateRestartChip();
            foreach (var p in _pages.Values) (p as IPage)?.RefreshTexts();
        }

        // ------------------------------------------------------------ Monitor

        void OnMonitorSample(MonitorSample s)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (s.Cpu >= 0) { TxtCpu.Text = Math.Round(s.Cpu) + " %"; SetLoad(BarCpu, TxtCpu, s.Cpu); }
                if (s.RamTotal > 0)
                {
                    TxtRam.Text = (s.RamUsed / 1073741824.0).ToString("0.0") + " / " + (s.RamTotal / 1073741824.0).ToString("0") + " GB";
                    SetLoad(BarRam, TxtRam, s.RamUsed / s.RamTotal * 100);
                }
                if (s.Gpu >= 0) { TxtGpu.Text = Math.Round(s.Gpu) + " %"; SetLoad(BarGpu, TxtGpu, s.Gpu); }
                else TxtGpu.Text = "N/D";
                if (s.Vram >= 0)
                {
                    double total = s.VramTotal > 0 ? s.VramTotal : (AppState.Current.Sys?.VramGB ?? 0) * 1073741824.0;
                    TxtVram.Text = total > 0
                        ? (s.Vram / 1073741824.0).ToString("0.0") + " / " + (total / 1073741824.0).ToString("0") + " GB"
                        : (s.Vram / 1073741824.0).ToString("0.0") + " GB";
                    SetLoad(BarVram, TxtVram, total > 0 ? s.Vram / total * 100 : 0);
                }
                else TxtVram.Text = "N/D";
            }), DispatcherPriority.Background);
        }

        /// <summary>Barra del monitor: relleno neutro; ámbar solo cuando la carga pasa del 85 %.</summary>
        void SetLoad(ProgressBar bar, TextBlock txt, double pct)
        {
            bar.Value = pct;
            bool high = pct >= 85;
            bar.Foreground = (Brush)FindResource(high ? "Warning" : "DataFill");
            txt.Foreground = (Brush)FindResource(high ? "Warning" : "Text");
        }

        // ------------------------------------------------------------ Volver a la base

        /// <summary>Restaura todos los valores originales que guardó Velox (Resumen y Herramientas). true si se deshizo algo.</summary>
        public async Task<bool> UndoAllAsync()
        {
            int n = Core.Backup.BackupStore.Ids().Count;
            if (n == 0) { ShowToast(Loc.T("ui.tools.undoNothing")); return false; }
            if (!await ConfirmAsync(Loc.T("ui.tools.undoTitle"), Loc.F("ui.tools.undoConfirm", n), null, Loc.T("ui.tools.undoButton"), Loc.T("ui.btn.cancel"), true)) return false;
            int done = 0;
            bool ran = await AppState.Current.RunBusy(Loc.T("ui.tools.undoBusy"), async () =>
            {
                done = await Task.Run(() =>
                {
                    int r = Core.Backup.BackupStore.RestoreAll();
                    foreach (var o in Core.Optimizations.Catalog.All) { try { o.Sync?.Invoke(); } catch { } }
                    foreach (var t in Core.Tweaks.TweakCatalog.All) { try { t.Opt.Sync?.Invoke(); } catch { } }
                    return r;
                });
                await AppState.Current.RefreshLiveSysAsync();
                await AppState.Current.RefreshOptimizationsAsync();
            });
            if (!ran) return false; // había otra tarea en curso: no se ha deshecho nada
            AppState.Current.NeedsReboot = true;
            ShowToast(Loc.F("ui.tools.undoDone", done), ToastKind.Success);
            return true;
        }

        // ------------------------------------------------------------ Atajos de teclado

        protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            // Solo Ctrl: AltGr cuenta como Ctrl+Alt y en el teclado español escribe @ (AltGr+2), # (AltGr+3) o | (AltGr+1)
            if (DialogLayer.Visibility != Visibility.Visible && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
                var nav = ShortcutNav();
                int n = key >= System.Windows.Input.Key.D1 && key <= System.Windows.Input.Key.D9 ? key - System.Windows.Input.Key.D1
                      : key >= System.Windows.Input.Key.NumPad1 && key <= System.Windows.Input.Key.NumPad9 ? key - System.Windows.Input.Key.NumPad1 : -1;
                if (n >= 0 && n < nav.Length && nav[n].CommandParameter is string target) { Navigate(target); e.Handled = true; return; }
                if (key == System.Windows.Input.Key.F && PageHost.Content is FrameworkElement page)
                {
                    // El buscador visible de la página: «TxtSearch» en Optimizaciones, «TxtFilter» en Instalar y Desinstalar
                    foreach (var name in new[] { "TxtSearch", "TxtFilter" })
                        if (page.FindName(name) is TextBox box && box.IsVisible && box.IsEnabled)
                        {
                            box.Focus(); box.SelectAll(); e.Handled = true; return;
                        }
                }
                if (key == System.Windows.Input.Key.Enter && PageHost.Content is OptimizationsView ov && ov.TryApplyShortcut()) { e.Handled = true; return; }
            }
            base.OnPreviewKeyDown(e);
        }

        // ------------------------------------------------------------ Avisos

        public void ShowToast(string text, ToastKind kind = ToastKind.Info)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => ShowToast(text, kind))); return; }
            string glyph; Brush color;
            switch (kind)
            {
                case ToastKind.Success: glyph = ""; color = (Brush)FindResource("Success"); break;
                case ToastKind.Warning: glyph = ""; color = (Brush)FindResource("Warning"); break;
                case ToastKind.Error: glyph = ""; color = (Brush)FindResource("Danger"); break;
                default: glyph = ""; color = (Brush)FindResource("Info"); break;
            }
            var icon = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 15, Foreground = color, Margin = new Thickness(0, 1, 12, 0), VerticalAlignment = VerticalAlignment.Top };
            var msg = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Text"), MaxWidth = 380, LineHeight = 19 };
            var dp = new DockPanel();
            DockPanel.SetDock(icon, Dock.Left);
            dp.Children.Add(icon); dp.Children.Add(msg);
            var card = new Border
            {
                Background = (Brush)FindResource("CardHover"), BorderBrush = (Brush)FindResource("Border"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 13, 18, 13), Margin = new Thickness(0, 10, 0, 0), Child = dp,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Opacity = 0.45, Color = Colors.Black },
                RenderTransform = new TranslateTransform(30, 0), Opacity = 0,
            };
            ToastHost.Children.Add(card);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
            card.RenderTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(kind == ToastKind.Error ? 7 : 4.5) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
                fade.Completed += (s2, e2) => ToastHost.Children.Remove(card);
                card.BeginAnimation(OpacityProperty, fade);
            };
            timer.Start();
            while (ToastHost.Children.Count > 4) ToastHost.Children.RemoveAt(0);
        }

        // ------------------------------------------------------------ Diálogo de confirmación

        bool _protectionAsked;

        /// <summary>
        /// Antes de crear un punto de restauración: si la Protección del sistema está apagada (no se puede crear ninguno),
        /// ofrece activarla. Se pregunta una vez por sesión.
        /// </summary>
        public async Task OfferSystemProtectionAsync()
        {
            if (_protectionAsked || !await Task.Run(() => Core.Tools.RestorePointTool.ProtectionOff())) return;
            _protectionAsked = true;
            if (!await ConfirmAsync(Loc.T("ui.rp.offTitle"), Loc.T("ui.rp.offText"), null, Loc.T("ui.rp.enable"), Loc.T("ui.rp.notNow"))) return;
            bool ok = await Task.Run(() => Core.Tools.RestorePointTool.EnableProtection());
            ShowToast(ok ? Loc.T("ui.rp.enabled") : Loc.T("ui.rp.enableFailed"), ok ? ToastKind.Success : ToastKind.Error);
        }

        /// <summary>Muestra un diálogo con el estilo de la app. cancelText = null → solo botón de aceptar.</summary>
        public Task<bool> ConfirmAsync(string title, string message, string details, string okText, string cancelText, bool danger = false)
        {
#if UITEST
            // Pruebas automáticas: acepta los diálogos sin mostrar nada
            if (App.TestAction != null && App.TestAction.Contains("autoconfirm")) { Log.Info("UITEST autoconfirm: " + title); return Task.FromResult(true); }
#endif
            _dialog?.TrySetResult(false);
            _dialog = new TaskCompletionSource<bool>();
            DlgTitle.Text = title;
            DlgMessage.Text = message ?? "";
            DlgMessage.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
            DlgDetails.Text = details ?? "";
            DlgDetailsBox.Visibility = string.IsNullOrEmpty(details) ? Visibility.Collapsed : Visibility.Visible;
            DlgOk.Content = okText ?? Loc.T("ui.btn.ok");
            DlgOk.Style = (Style)FindResource(danger ? "BtnDanger" : "BtnPrimary");
            DlgCancel.Content = cancelText;
            DlgCancel.Visibility = cancelText == null ? Visibility.Collapsed : Visibility.Visible;
            DialogLayer.Visibility = Visibility.Visible;
            DialogLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            DialogCard.RenderTransform = new ScaleTransform(0.96, 0.96, 280, 150);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            DialogCard.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            DialogCard.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            DlgOk.Focus();
            return _dialog.Task;
        }

        void CloseDialog(bool result)
        {
            DialogLayer.Visibility = Visibility.Collapsed;
            var d = _dialog; _dialog = null;
            d?.TrySetResult(result);
        }

        void DlgOk_Click(object sender, RoutedEventArgs e) => CloseDialog(true);
        void DlgCancel_Click(object sender, RoutedEventArgs e) => CloseDialog(false);

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape && DialogLayer.Visibility == Visibility.Visible) { CloseDialog(false); e.Handled = true; }
            base.OnKeyDown(e);
        }
    }
}
