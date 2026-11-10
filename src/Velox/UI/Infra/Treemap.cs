using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Velox.Core.Disk;

namespace Velox.UI.Infra
{
    /// <summary>
    /// Mapa de bloques (treemap «squarified») de una carpeta: cada rectángulo es proporcional a lo que ocupa.
    /// Las subcarpetas grandes muestran dentro su propio contenido (segundo nivel). Colores por tipo de archivo.
    /// </summary>
    public sealed class Treemap : FrameworkElement
    {
        sealed class Block
        {
            public Rect Rect;
            public DiskNode Node;
            public int Level;
            public bool Framed;
        }

        static readonly Color[] KindColors =
        {
            Color.FromRgb(0xF0, 0x52, 0x5C), // Video
            Color.FromRgb(0xF5, 0xA5, 0x24), // Image
            Color.FromRgb(0xE9, 0x5C, 0xB0), // Audio
            Color.FromRgb(0xA8, 0x6B, 0xF7), // Archive
            Color.FromRgb(0x4C, 0x86, 0xF7), // Program
            Color.FromRgb(0x34, 0xC7, 0x6F), // Document
            Color.FromRgb(0x1F, 0xB8, 0xA9), // Code
            Color.FromRgb(0x6B, 0x78, 0x90), // System
            Color.FromRgb(0xF7, 0x7F, 0x3B), // DiskImage
            Color.FromRgb(0x8E, 0x96, 0xA8), // Other
            Color.FromRgb(0x46, 0x4E, 0x62), // Folder
        };

        public static Color ColorOf(FileKind k) => KindColors[(int)k];

        readonly List<Block> _blocks = new List<Block>();
        readonly Dictionary<string, Brush> _brushCache = new Dictionary<string, Brush>();
        DiskNode _root;
        bool _dirty = true;
        Size _laidOut;
        Block _hover;

        /// <summary>Nombre a mostrar de un elemento (lo pone la vista: traduce el grupo de archivos pequeños).</summary>
        public Func<DiskNode, string> NameOf { get; set; } = n => n.Name;
        public Func<long, string> SizeText { get; set; } = Format.Bytes;

        public event Action<DiskNode> HoverChanged;
        public event Action<DiskNode, MouseButton> NodeClicked;

        public Treemap()
        {
            ClipToBounds = true;
            Cursor = Cursors.Hand;
            SnapsToDevicePixels = true;
        }

        public DiskNode Root
        {
            get => _root;
            set { _root = value; _hover = null; _dirty = true; InvalidateVisual(); }
        }

        public DiskNode Hovered => _hover?.Node;

        /// <summary>Vuelve a calcular (tras borrar algo del árbol).</summary>
        public void Refresh() { _dirty = true; _hover = null; InvalidateVisual(); }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            _dirty = true;
        }

        // ------------------------------------------------------------ Distribución

        void Layout()
        {
            _blocks.Clear();
            _laidOut = new Size(ActualWidth, ActualHeight);
            _dirty = false;
            if (_root?.Children == null || ActualWidth < 4 || ActualHeight < 4) return;

            LayoutLevel(_root.Children, new Rect(0, 0, ActualWidth, ActualHeight), 1);
        }

        const int MaxLevel = 4;

        /// <summary>Distribuye un nivel; las carpetas con sitio suficiente muestran dentro su contenido (hasta MaxLevel).</summary>
        void LayoutLevel(List<DiskNode> nodes, Rect area, int level)
        {
            foreach (var b in Squarify(nodes, area, level == 1 ? 400 : 160))
            {
                var r = b.Item1; var n = b.Item2;
                bool framed = level < MaxLevel && n.IsDir && n.Children != null && n.Children.Count > 0
                              && r.Width >= 90 && r.Height >= 64 && n.Children[0].Size > 0;
                _blocks.Add(new Block { Rect = r, Node = n, Level = level, Framed = framed });
                if (!framed) continue;
                var inner = new Rect(r.X + 3, r.Y + 20, Math.Max(0, r.Width - 6), Math.Max(0, r.Height - 23));
                if (inner.Width < 6 || inner.Height < 6) continue;
                LayoutLevel(n.Children, inner, level + 1);
            }
        }

        /// <summary>Algoritmo «squarified» (Bruls, Huizing y van Wijk): rectángulos lo más cuadrados posible.</summary>
        static List<Tuple<Rect, DiskNode>> Squarify(List<DiskNode> nodes, Rect rect, int max)
        {
            var result = new List<Tuple<Rect, DiskNode>>();
            long total = 0;
            int count = 0;
            foreach (var n in nodes) { if (n.Size <= 0) break; total += n.Size; if (++count >= max) break; }
            if (total <= 0) return result;

            double scale = rect.Width * rect.Height / total;
            var areas = new List<double>(count);
            var items = new List<DiskNode>(count);
            for (int i = 0; i < count; i++)
            {
                double a = nodes[i].Size * scale;
                if (a < 6) break; // demasiado pequeño para verse
                areas.Add(a); items.Add(nodes[i]);
            }
            // Lo que no se dibuja se reparte: se reescala para que lo visible llene el espacio
            double visible = 0; foreach (var a in areas) visible += a;
            if (visible <= 0) return result;
            double fix = rect.Width * rect.Height / visible;
            for (int i = 0; i < areas.Count; i++) areas[i] *= fix;

            int start = 0;
            var free = rect;
            while (start < areas.Count && free.Width > 0.5 && free.Height > 0.5)
            {
                double side = Math.Min(free.Width, free.Height);
                int end = start;
                double sum = 0, worst = double.MaxValue;
                while (end < areas.Count)
                {
                    double s2 = sum + areas[end];
                    double w = Worst(areas[start], areas[end], s2, side);
                    if (end > start && w > worst) break;
                    worst = w; sum = s2; end++;
                }

                if (free.Width >= free.Height)
                {
                    double colW = Math.Min(free.Width, sum / free.Height);
                    double y = free.Y;
                    for (int k = start; k < end; k++)
                    {
                        double h = areas[k] / colW;
                        if (k == end - 1) h = free.Bottom - y;
                        result.Add(Tuple.Create(new Rect(free.X, y, colW, Math.Max(0, h)), items[k]));
                        y += h;
                    }
                    free = new Rect(free.X + colW, free.Y, Math.Max(0, free.Width - colW), free.Height);
                }
                else
                {
                    double rowH = Math.Min(free.Height, sum / free.Width);
                    double x = free.X;
                    for (int k = start; k < end; k++)
                    {
                        double w = areas[k] / rowH;
                        if (k == end - 1) w = free.Right - x;
                        result.Add(Tuple.Create(new Rect(x, free.Y, Math.Max(0, w), rowH), items[k]));
                        x += w;
                    }
                    free = new Rect(free.X, free.Y + rowH, free.Width, Math.Max(0, free.Height - rowH));
                }
                start = end;
            }
            return result;
        }

        static double Worst(double maxA, double minA, double sum, double side)
        {
            double s2 = side * side, sum2 = sum * sum;
            return Math.Max(s2 * maxA / sum2, sum2 / (s2 * minA));
        }

        // ------------------------------------------------------------ Dibujo

        static Color Mix(Color a, Color b, double t) => Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        static bool IsLight(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 170;

        Brush FillFor(FileKind kind, int level, bool frame)
        {
            string key = (int)kind + "|" + level + "|" + frame;
            if (_brushCache.TryGetValue(key, out var b)) return b;
            var c = KindColors[(int)kind];
            Brush brush;
            if (frame)
            {
                brush = new SolidColorBrush(Mix(Color.FromRgb(0x17, 0x19, 0x1C), c, 0.22));
            }
            else
            {
                // Relieve suave: más claro arriba a la izquierda, más oscuro abajo a la derecha
                var top = Mix(c, Colors.White, level == 1 ? 0.18 : 0.12);
                var bottom = Mix(c, Colors.Black, level == 1 ? 0.28 : 0.35);
                brush = new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(1, 1));
            }
            brush.Freeze();
            _brushCache[key] = brush;
            return brush;
        }

        FormattedText Text(string s, double size, Brush brush, FontWeight weight)
        {
            var family = (FontFamily)GetValue(TextElement.FontFamilyProperty);
            double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var ft = new FormattedText(s ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal), size, brush, dip);
            ft.Trimming = TextTrimming.CharacterEllipsis;
            ft.MaxLineCount = 1;
            return ft;
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight)); // recibe el ratón en todo el área
            if (_dirty || _laidOut.Width != ActualWidth || _laidOut.Height != ActualHeight) Layout();

            var white = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)); white.Freeze();
            var whiteDim = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)); whiteDim.Freeze();
            var dark = new SolidColorBrush(Color.FromArgb(0xE6, 0x10, 0x12, 0x18)); dark.Freeze();
            var darkDim = new SolidColorBrush(Color.FromArgb(0xA0, 0x10, 0x12, 0x18)); darkDim.Freeze();
            var framePen = new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), 1); framePen.Freeze();

            foreach (var b in _blocks)
            {
                var r = b.Rect;
                var inset = new Rect(r.X + 1, r.Y + 1, Math.Max(0, r.Width - 2), Math.Max(0, r.Height - 2));
                if (inset.Width <= 0 || inset.Height <= 0) continue;
                var kind = b.Node.IsDir ? b.Node.Dominant : b.Node.Kind;
                double radius = b.Level == 1 ? 6 : 3;
                if (inset.Width < 10 || inset.Height < 10) radius = 1;

                if (b.Framed)
                {
                    dc.DrawRoundedRectangle(FillFor(kind, Math.Min(b.Level, 2), true), framePen, inset, radius, radius);
                    var head = Text(NameOf(b.Node) + "   " + SizeText(b.Node.Size), 11.5, white, FontWeights.SemiBold);
                    head.MaxTextWidth = Math.Max(1, inset.Width - 12);
                    dc.PushClip(new RectangleGeometry(inset));
                    dc.DrawText(head, new Point(inset.X + 7, inset.Y + 2));
                    dc.Pop();
                    continue;
                }

                dc.DrawRoundedRectangle(FillFor(kind, Math.Min(b.Level, 2), false), null, inset, radius, radius);
                bool light = IsLight(KindColors[(int)kind]);
                if (inset.Width >= 44 && inset.Height >= 20)
                {
                    dc.PushClip(new RectangleGeometry(inset));
                    double fs = b.Level == 1 ? 12.5 : 11;
                    var name = Text(NameOf(b.Node), fs, light ? dark : white, FontWeights.SemiBold);
                    name.MaxTextWidth = Math.Max(1, inset.Width - 10);
                    dc.DrawText(name, new Point(inset.X + 6, inset.Y + 4));
                    if (inset.Height >= 38)
                    {
                        var size = Text(SizeText(b.Node.Size), fs - 1.5, light ? darkDim : whiteDim, FontWeights.Normal);
                        size.MaxTextWidth = Math.Max(1, inset.Width - 10);
                        dc.DrawText(size, new Point(inset.X + 6, inset.Y + 5 + name.Height));
                    }
                    dc.Pop();
                }
            }

            if (_hover != null)
            {
                var r = _hover.Rect;
                var pen = new Pen(Brushes.White, 2); pen.Freeze();
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), pen,
                    new Rect(r.X + 1, r.Y + 1, Math.Max(0, r.Width - 2), Math.Max(0, r.Height - 2)), 4, 4);
            }
        }

        // ------------------------------------------------------------ Ratón

        Block HitTest(Point p)
        {
            for (int i = _blocks.Count - 1; i >= 0; i--)
            {
                var b = _blocks[i];
                if (!b.Rect.Contains(p)) continue;
                // En una carpeta con contenido dibujado, la cabecera selecciona la carpeta y el interior sus elementos
                return b;
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var hit = HitTest(e.GetPosition(this));
            if (hit == _hover) return;
            _hover = hit;
            InvalidateVisual();
            HoverChanged?.Invoke(hit?.Node);
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover == null) return;
            _hover = null;
            InvalidateVisual();
            HoverChanged?.Invoke(null);
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            var hit = HitTest(e.GetPosition(this));
            if (hit != null) NodeClicked?.Invoke(hit.Node, e.ChangedButton);
        }
    }
}
