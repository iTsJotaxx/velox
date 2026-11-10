using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Velox.Core;
using Velox.Core.Backup;
using Velox.Core.Optimizations;
using Velox.Localization;

namespace Velox.UI.Views
{
    /// <summary>
    /// Historial de cambios: qué cambió Velox, cuándo, el valor antes y después, quién lo pidió, y deshacer uno a uno
    /// (solo en la fila más reciente de cada ajuste que todavía tiene copia del original).
    /// </summary>
    public partial class HistoryView : UserControl, IPage
    {
        const int MaxRows = 300;

        public HistoryView()
        {
            InitializeComponent();
            AppState.Current.ChangesApplied += () => Dispatcher.BeginInvoke(new Action(Render));
            Render();
        }

        public void OnShown() => Render();
        public void RefreshTexts() => Render();

        MainWindow Win => (MainWindow)Window.GetWindow(this);
        T Res<T>(string key) => (T)FindResource(key);

        /// <summary>Nombre visible de lo que cambió.</summary>
        internal static string NameOf(ChangeEntry e)
        {
            if (e.Id == Core.Tools.DnsTool.HistoryId) return Loc.T("hist.dns");
            var o = OptimizationEngine.Find(e.Id);
            if (o != null) return o.Name;
            return Loc.Has("hist.name." + e.Id) ? Loc.T("hist.name." + e.Id) : e.Id;
        }

        /// <summary>(antes, después) con las etiquetas de la propia optimización; los ajustes de Windows dicen activado/desactivado.</summary>
        internal static Tuple<string, string> FromTo(ChangeEntry e)
        {
            if (e.From != null || e.To != null) return Tuple.Create(e.From ?? "-", e.To ?? "-");
            string b = Loc.Has("opt." + e.Id + ".base") ? Loc.T("opt." + e.Id + ".base") : Loc.T("hist.off");
            string v = Loc.Has("opt." + e.Id + ".velox") ? Loc.T("opt." + e.Id + ".velox") : Loc.T("hist.on");
            return e.Action == "on" ? Tuple.Create(b, v) : Tuple.Create(v, b);
        }

        void Render()
        {
            Rows.Children.Clear();
            var all = History.All();
            if (all.Count == 0)
            {
                Rows.Children.Add(new TextBlock { Text = Loc.T("hist.empty"), Style = Res<Style>("Muted"), Margin = new Thickness(0, 10, 0, 0) });
                return;
            }
            // «Deshacer» solo en la última fila de cada ajuste, si sigue activado por Velox y queda copia del original
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in all.Take(MaxRows))
            {
                bool latest = seen.Add(e.Id);
                bool canUndo = latest && e.Ok && e.Action == "on" && BackupStore.Has(e.Id);
                AddRow(e, canUndo);
            }
        }

        void AddRow(ChangeEntry e, bool canUndo)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

            var when = new TextBlock { Text = e.When.ToString("g"), Style = Res<Style>("Figure"), FontWeight = FontWeights.Normal, FontSize = 12.5, Foreground = Res<Brush>("TextMuted"), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(when);

            var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            name.Children.Add(new TextBlock { Text = NameOf(e), FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = NameOf(e) });
            if (!e.Ok) name.Children.Add(new TextBlock { Text = Loc.T("hist.failed") + (string.IsNullOrEmpty(e.Error) ? "" : ": " + e.Error), Foreground = Res<Brush>("Danger"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(name, 1); g.Children.Add(name);

            var ft = FromTo(e);
            var change = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            change.Children.Add(new TextBlock { Text = ft.Item1, Style = Res<Style>("Figure"), FontWeight = FontWeights.Normal, FontSize = 13, Foreground = Res<Brush>("TextMuted"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 200 });
            change.Children.Add(new TextBlock { Text = "", FontFamily = Res<FontFamily>("Icons"), FontSize = 10, Foreground = Res<Brush>("TextDim"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) });
            change.Children.Add(new TextBlock { Text = ft.Item2, Style = Res<Style>("Figure"), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 200, Opacity = e.Ok ? 1 : 0.5 });
            Grid.SetColumn(change, 2); g.Children.Add(change);

            var src = new Border { Style = Res<Style>("Badge"), Background = Res<Brush>("CardInner"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            src.Child = new TextBlock { Text = Loc.Has("hist.src." + e.Source) ? Loc.T("hist.src." + e.Source) : e.Source, Style = Res<Style>("BadgeText"), Foreground = Res<Brush>("TextMuted") };
            Grid.SetColumn(src, 3); g.Children.Add(src);

            if (canUndo)
            {
                var btn = new Button { Content = Loc.T("hist.undo"), Style = Res<Style>("BtnBase"), Padding = new Thickness(12, 6, 12, 6), HorizontalAlignment = HorizontalAlignment.Right, Tag = e.Id };
                System.Windows.Automation.AutomationProperties.SetName(btn, Loc.T("hist.undo") + " " + NameOf(e));
                btn.Click += Undo_Click;
                Grid.SetColumn(btn, 4); g.Children.Add(btn);
            }
            Rows.Children.Add(new Border { BorderBrush = Res<Brush>("Rule"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 10, 0, 10), Child = g });
        }

        async void Undo_Click(object sender, RoutedEventArgs e)
        {
            string id = (sender as Button)?.Tag as string;
            if (id == null) return;
            if (id == Core.Tools.DnsTool.HistoryId)
            {
                await Win.RestoreDnsAsync();
                Render();
                return;
            }
            var o = OptimizationEngine.Find(id);
            if (o == null) return;
            await AppState.Current.ApplyChangesAsync(new List<OptChange> { new OptChange(o, false) }, "undo-one",
                Loc.F("hist.undoTitle", o.Name), Loc.T("hist.undoText"));
            Render();
        }

        void Back_Click(object sender, RoutedEventArgs e) => Win.Navigate("summary");
    }
}
