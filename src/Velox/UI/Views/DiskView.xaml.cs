using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Velox.Core;
using Velox.Core.Disk;
using Velox.Localization;
using Velox.UI.Infra;

namespace Velox.UI.Views
{
    public sealed class DriveRow
    {
        public DriveEntry Drive { get; set; }
        public string Title { get; set; }
        public string Sub { get; set; }
        public double UsedPct { get; set; }
        public Brush BarBrush { get; set; }
        public string State { get; set; }
    }

    public sealed class DiskRow
    {
        public DiskNode Node { get; set; }
        public string Name { get; set; }
        public string SizeText { get; set; }
        public string PctText { get; set; }
        public double BarWidth { get; set; }
        public Brush Brush { get; set; }
        public string Icon { get; set; }
        public string Tip { get; set; }
    }

    public sealed class KindRow
    {
        public string Name { get; set; }
        public string SizeText { get; set; }
        public string PctText { get; set; }
        public Brush Brush { get; set; }
    }

    /// <summary>Explorador visual de disco: mapa de bloques por tamaño, carpetas que más ocupan y reparto por tipo.</summary>
    public partial class DiskView : UserControl, IPage
    {
        const double BarMax = 190;
        readonly Stack<DiskNode> _back = new Stack<DiskNode>();
        readonly DispatcherTimer _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        DiskNode _root, _current;
        CancellationTokenSource _cts;
        DiskScanCounters _counters;
        long _expectedBytes;
        string _scanPath;
        List<DriveEntry> _drives = new List<DriveEntry>();

        public DiskView()
        {
            InitializeComponent();
            Map.NameOf = DisplayName;
            Map.HoverChanged += OnHover;
            Map.NodeClicked += OnMapClick;
            _progressTimer.Tick += (s, e) => UpdateProgress();
            PreviewMouseUp += (s, e) => { if (e.ChangedButton == MouseButton.XButton1) { GoBack(); e.Handled = true; } };
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Back && !(e.OriginalSource is TextBox)) { GoBack(); e.Handled = true; } };
        }

        MainWindow Win => (MainWindow)Window.GetWindow(this);

        public void OnShown()
        {
            LoadDrives();
            Focus();
        }

        public void RefreshTexts()
        {
            LoadDrives();
            if (_current != null) Show(_current, false);
        }

        // ------------------------------------------------------------ Unidades

        void LoadDrives()
        {
            Task.Run(() => DiskScanner.GetDrives()).ContinueWith(t =>
            {
                if (t.IsFaulted) return;
                _drives = t.Result;
                RenderDrives();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void RenderDrives()
        {
            string rootPath = _root?.FullPath;
            DriveList.ItemsSource = _drives.Select(d => new DriveRow
            {
                Drive = d,
                Title = d.Root.TrimEnd('\\') + "  " + (string.IsNullOrWhiteSpace(d.Label) ? Loc.T("disk.localDisk") : d.Label),
                Sub = Loc.F("disk.driveFree", Format.Bytes(d.Free), Format.Bytes(d.Total)),
                UsedPct = d.UsedPct,
                BarBrush = d.UsedPct >= 90 ? (Brush)FindResource("Danger") : d.UsedPct >= 75 ? (Brush)FindResource("Warning") : (Brush)FindResource("AccentGradient"),
                State = rootPath != null && string.Equals(rootPath.TrimEnd('\\'), d.Root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ? "active" : "",
            }).ToList();
        }

        void Drive_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is DriveRow r) StartScan(r.Drive.Root, r.Drive.Used);
        }

        void Pick_Click(object sender, RoutedEventArgs e)
        {
            string p = FolderPicker.Pick(Win, Loc.T("disk.pickTitle"), _current?.FullPath);
            if (!string.IsNullOrEmpty(p)) StartScan(p, 0);
        }

        void Rescan_Click(object sender, RoutedEventArgs e)
        {
            if (_root == null) return;
            string keep = _current?.FullPath;
            var drive = _drives.FirstOrDefault(d => string.Equals(d.Root.TrimEnd('\\'), _root.FullPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            StartScan(_root.FullPath, drive?.Used ?? _root.Size, keep);
        }

        void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        // ------------------------------------------------------------ Análisis

        /// <summary>Visible para las pruebas automáticas (--disk=ruta).</summary>
        public async void StartScan(string path, long expectedBytes, string reopen = null)
        {
            _cts?.Cancel();
            var cts = new CancellationTokenSource();
            _cts = cts;
            _counters = new DiskScanCounters();
            _expectedBytes = expectedBytes;
            _scanPath = path;

            TxtScanTitle.Text = Loc.F("disk.scanning", path);
            ScanPanel.Visibility = Visibility.Visible;
            EmptyPanel.Visibility = Visibility.Collapsed;
            ScanBar.BeginAnimation(ProgressBar.ValueProperty, null);
            ScanBar.Value = 0;
            if (expectedBytes <= 0)
                ScanBar.BeginAnimation(ProgressBar.ValueProperty, new DoubleAnimation(0, 100, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true });
            UpdateProgress();
            _progressTimer.Start();

            DiskNode result = null;
            string error = null;
            var counters = _counters;
            try { result = await Task.Run(() => DiskScanner.Scan(path, counters, cts.Token)); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { error = ex.Message; Log.Error("Disk scan " + path, ex); }

            if (_cts != cts) return; // se empezó otro análisis mientras tanto
            _progressTimer.Stop();
            ScanBar.BeginAnimation(ProgressBar.ValueProperty, null);
            ScanPanel.Visibility = Visibility.Collapsed;
            _cts = null;

            if (result == null)
            {
                if (_root == null) EmptyPanel.Visibility = Visibility.Visible;
                if (error != null) Win?.ShowToast(Loc.F("disk.scanFailed", error), ToastKind.Error);
                else Win?.ShowToast(Loc.T("disk.scanCancelled"), ToastKind.Warning);
                return;
            }

            _root = result;
            _back.Clear();
            BtnRescan.IsEnabled = true;
            var target = reopen != null ? Find(result, reopen) : null;
            Show(target ?? result, true);
            RenderDrives();
            if (counters.Denied > 0) Log.Info("Disk scan: " + counters.Denied + " folder(s) could not be read (no permission).");
        }

        void UpdateProgress()
        {
            var c = _counters;
            if (c == null) return;
            long bytes = Interlocked.Read(ref c.Bytes);
            TxtScanProgress.Text = Loc.F("disk.scanProgress", Interlocked.Read(ref c.Files).ToString("N0"), Interlocked.Read(ref c.Dirs).ToString("N0"), Format.Bytes(bytes));
            if (_expectedBytes > 0) ScanBar.Value = Math.Min(99, bytes * 100.0 / _expectedBytes);
        }

        static DiskNode Find(DiskNode root, string fullPath)
        {
            var n = root;
            string rp = root.FullPath.TrimEnd('\\');
            string target = fullPath.TrimEnd('\\');
            if (!target.StartsWith(rp, StringComparison.OrdinalIgnoreCase)) return null;
            var parts = target.Substring(rp.Length).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                n = n.Children?.FirstOrDefault(c => c.IsDir && string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase));
                if (n == null) return null;
            }
            return n;
        }

#if UITEST
        /// <summary>Solo pruebas: entra en la carpeta más grande y resalta su primer elemento.</summary>
        public void TestEnterBiggest()
        {
            var next = _current?.Children?.FirstOrDefault(c => c.CanEnter);
            if (next != null) Enter(next);
            var first = _current?.Children?.FirstOrDefault();
            if (first != null) { OnHover(first); SelectInList(first); }
        }
#endif

        // ------------------------------------------------------------ Navegación

        void Enter(DiskNode node)
        {
            if (node == null || node == _current || !node.CanEnter) return;
            if (_current != null) _back.Push(_current);
            Show(node, true);
        }

        void GoBack()
        {
            while (_back.Count > 0)
            {
                var n = _back.Pop();
                if (IsAlive(n)) { Show(n, true); return; }
            }
        }

        void Back_Click(object sender, RoutedEventArgs e) => GoBack();

        void Up_Click(object sender, RoutedEventArgs e)
        {
            if (_current?.Parent != null) { _back.Push(_current); Show(_current.Parent, true); }
        }

        // Un nodo borrado ya no cuelga de la raíz
        bool IsAlive(DiskNode n)
        {
            for (var p = n; p != null; p = p.Parent) if (p == _root) return true;
            return false;
        }

        void Show(DiskNode node, bool animate)
        {
            _current = node;
            Map.Root = node;
            BtnBack.IsEnabled = _back.Count > 0;
            BtnUp.IsEnabled = node.Parent != null;
            EmptyPanel.Visibility = Visibility.Collapsed;
            RenderCrumbs();
            RenderLists();
            TxtTotal.Text = Loc.F("disk.total", Format.Bytes(node.Size), node.FileCount.ToString("N0"), node.DirCount.ToString("N0"));
            ResetHover();
            if (animate)
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                Map.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            }
        }

        void RenderCrumbs()
        {
            Crumbs.Children.Clear();
            var chain = new List<DiskNode>();
            for (var p = _current; p != null; p = p.Parent) chain.Insert(0, p);
            for (int i = 0; i < chain.Count; i++)
            {
                var n = chain[i];
                if (i > 0)
                    Crumbs.Children.Add(new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 9, Foreground = (Brush)FindResource("TextDim"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 1, 2, 0) });
                var b = new Button { Style = (Style)FindResource("Crumb"), Content = i == 0 ? n.FullPath : n.Name, Tag = n };
                if (n == _current) { b.Foreground = (Brush)FindResource("Text"); b.FontWeight = FontWeights.SemiBold; }
                b.Click += (s, e) =>
                {
                    var target = (DiskNode)((Button)s).Tag;
                    if (target != _current) { _back.Push(_current); Show(target, true); }
                };
                Crumbs.Children.Add(b);
            }
            CrumbScroll.UpdateLayout();
            CrumbScroll.ScrollToRightEnd();
        }

        void RenderLists()
        {
            var node = _current;
            long total = Math.Max(1, node.Size);
            long max = node.Children?.FirstOrDefault()?.Size ?? 1;
            TopList.ItemsSource = (node.Children ?? new List<DiskNode>()).Take(80).Select(c => new DiskRow
            {
                Node = c,
                Name = DisplayName(c),
                SizeText = Format.Bytes(c.Size),
                PctText = (c.Size * 100.0 / total).ToString("0.0") + " %",
                BarWidth = max > 0 ? Math.Max(2, c.Size * BarMax / max) : 2,
                Brush = BrushFor(c),
                Icon = c.IsDir ? "" : c.IsAggregate ? "" : "",
                Tip = Describe(c),
            }).ToList();

            var kinds = new List<KindRow>();
            KindStrip.Children.Clear();
            if (node.KindSizes != null)
            {
                long sum = Math.Max(1, node.KindSizes.Sum());
                double stripW = ((FrameworkElement)KindStrip.Parent).ActualWidth;
                if (stripW < 50) stripW = 290; // aún sin medir
                foreach (var i in Enumerable.Range(0, DiskNode.KindCount).OrderByDescending(i => node.KindSizes[i]))
                {
                    long v = node.KindSizes[i];
                    if (v <= 0) continue;
                    var brush = new SolidColorBrush(Treemap.ColorOf((FileKind)i)); brush.Freeze();
                    // Los tipos que apenas ocupan no se listan (sí salen en la barra): deja sitio a «Lo que más ocupa»
                    if (v * 100.0 / sum >= 0.5 || kinds.Count < 4) kinds.Add(new KindRow
                    {
                        Name = Loc.T("disk.kind." + ((FileKind)i).ToString().ToLowerInvariant()),
                        SizeText = Format.Bytes(v),
                        PctText = (v * 100.0 / sum).ToString("0") + " %",
                        Brush = brush,
                    });
                    double w = v * stripW / sum;
                    if (w >= 1) KindStrip.Children.Add(new Border { Width = w, Background = brush });
                }
            }
            KindList.ItemsSource = kinds;
        }

        Brush BrushFor(DiskNode n)
        {
            var c = Treemap.ColorOf(n.IsDir ? n.Dominant : n.Kind);
            var b = new SolidColorBrush(c); b.Freeze();
            return b;
        }

        string DisplayName(DiskNode n)
        {
            if (n.IsAggregate) return Loc.F("disk.smallFiles", n.FileCount.ToString("N0"));
            if (n.Parent == null) return n.FullPath;
            return n.Name;
        }

        string Describe(DiskNode n)
        {
            double pct = _current != null && _current.Size > 0 ? n.Size * 100.0 / _current.Size : 0;
            string s = DisplayName(n) + "  ·  " + Format.Bytes(n.Size) + "  ·  " + pct.ToString("0.0") + " %";
            if (n.IsDir)
            {
                s += "  ·  " + Loc.F("disk.contains", n.FileCount.ToString("N0"), n.DirCount.ToString("N0"));
                if (n.IsLink) s += "  ·  " + Loc.T("disk.link");
                else if (n.Denied) s += "  ·  " + Loc.T("disk.denied");
                else s += "  ·  " + Loc.T("disk.kind." + n.Dominant.ToString().ToLowerInvariant());
            }
            else if (!n.IsAggregate)
            {
                s += "  ·  " + Loc.T("disk.kind." + n.Kind.ToString().ToLowerInvariant());
            }
            if (!n.IsAggregate && n.Modified > DateTime.MinValue) s += "  ·  " + n.Modified.ToString("g");
            return s;
        }

        // ------------------------------------------------------------ Ratón

        void ResetHover()
        {
            TxtHover.Text = Loc.T("disk.hoverHint");
            TxtHover.Foreground = (Brush)FindResource("TextMuted");
            HoverSwatch.Fill = (Brush)FindResource("TextDim");
        }

        void OnHover(DiskNode n)
        {
            if (n == null) { ResetHover(); return; }
            TxtHover.Text = Describe(n);
            TxtHover.Foreground = (Brush)FindResource("Text");
            HoverSwatch.Fill = BrushFor(n);
        }

        void OnMapClick(DiskNode n, MouseButton button)
        {
            if (button == MouseButton.Left)
            {
                if (n.CanEnter) Enter(n);
                else SelectInList(n);
            }
            else if (button == MouseButton.Right) ShowMenu(n);
        }

        void SelectInList(DiskNode n)
        {
            var row = (TopList.ItemsSource as IEnumerable<DiskRow>)?.FirstOrDefault(r => r.Node == n);
            if (row == null) return;
            TopList.SelectedItem = row;
            TopList.ScrollIntoView(row);
        }

        void TopList_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (TopList.SelectedItem is DiskRow r)
            {
                if (r.Node.CanEnter) Enter(r.Node);
                else if (!r.Node.IsAggregate) TryRun(() => DiskActions.Open(r.Node));
            }
        }

        void TopList_RightClick(object sender, MouseButtonEventArgs e)
        {
            var item = ItemsControl.ContainerFromElement(TopList, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item?.DataContext is DiskRow r)
            {
                TopList.SelectedItem = r;
                ShowMenu(r.Node);
                e.Handled = true;
            }
        }

        void TopList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TopList.SelectedItem is DiskRow r) OnHover(r.Node);
        }

        // ------------------------------------------------------------ Menú contextual

        void ShowMenu(DiskNode n)
        {
            var menu = new ContextMenu();
            if (n.CanEnter) menu.Items.Add(Item("disk.menu.enter", "", () => Enter(n)));
            if (!n.IsAggregate) menu.Items.Add(Item("disk.menu.open", "", () => TryRun(() => DiskActions.Open(n))));
            menu.Items.Add(Item("disk.menu.explorer", "", () => TryRun(() => DiskActions.ShowInExplorer(n))));
            if (!n.IsAggregate) menu.Items.Add(Item("disk.menu.copyPath", "", () => TryRun(() => { Clipboard.SetText(n.FullPath); Win?.ShowToast(Loc.T("disk.copied"), ToastKind.Success); })));
            string reason = DiskActions.ProtectedReason(n);
            menu.Items.Add(new Separator());
            if (reason == null)
            {
                var del = Item("disk.menu.recycle", "", () => RecycleAsync(n));
                del.Style = (Style)FindResource("MenuDanger");
                menu.Items.Add(del);
            }
            else if (!n.IsAggregate)
            {
                // Se explica por qué no se puede borrar (carpeta clave, de Windows, programa instalado...)
                menu.Items.Add(new MenuItem
                {
                    Header = new TextBlock { Text = reason, TextWrapping = TextWrapping.Wrap, MaxWidth = 300, Foreground = (Brush)FindResource("TextMuted"), FontSize = 12 },
                    Tag = "",
                    IsHitTestVisible = false,
                });
            }
            menu.PlacementTarget = this;
            menu.IsOpen = true;
        }

        MenuItem Item(string key, string glyph, Action action)
        {
            var mi = new MenuItem { Header = Loc.T(key), Tag = glyph };
            mi.Click += (s, e) => action();
            return mi;
        }

        void TryRun(Action a)
        {
            try { a(); }
            catch (Exception ex) { Win?.ShowToast(ex.Message, ToastKind.Error); }
        }

        async void RecycleAsync(DiskNode n)
        {
            string what = n.IsDir ? Loc.F("disk.del.folderDetail", n.FileCount.ToString("N0"), n.DirCount.ToString("N0")) : null;
            if (!await Win.ConfirmAsync(Loc.F("disk.del.title", n.Name), Loc.F("disk.del.text", Format.Bytes(n.Size)),
                    n.FullPath + (what != null ? "\n" + what : ""), Loc.T("disk.menu.recycle"), Loc.T("ui.btn.cancel"), true)) return;
            try
            {
                await Task.Run(() => DiskActions.Recycle(n));
                DiskScanner.Remove(n);
                Win.ShowToast(Loc.F("disk.del.done", n.Name, Format.Bytes(n.Size)), ToastKind.Success);
            }
            catch (Exception ex)
            {
                Log.Warn("Recycle " + n.FullPath + ": " + ex.Message);
                await Win.ConfirmAsync(Loc.T("disk.del.errorTitle"), ex.Message, n.FullPath, Loc.T("ui.btn.ok"), null);
            }
            Map.Refresh();
            if (!IsAlive(_current)) _current = n.Parent ?? _root;
            Show(_current, false);
            LoadDrives();
        }
    }
}
