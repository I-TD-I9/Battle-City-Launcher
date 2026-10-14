using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace BattleCity.Launcher;

/// <summary>
/// The picture beside the launcher's pages: the plaza clock seen from its foot, rising past the
/// buildings round the square into a summer sky, drawn in the ink and screentone of a manga
/// page. It's all plain shapes, so it stays sharp at any size, and the clock keeps the
/// player's own time.
/// </summary>
public sealed class Plaza : FrameworkElement
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(Plaza),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The picture is clipped to a rectangle rounded this much, to sit inside a frame.</summary>
    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    // Everything is drawn on a board this size, then scaled to cover the element.
    const double BoardWidth = 300, BoardHeight = 560;

    // Drawn once, on first use: everything but the clock's hands stays as it is.
    static Drawing? scene;

    readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(15) };
    int shownMinute = -1;

    public Plaza()
    {
        tick.Tick += (_, _) =>
        {
            if (DateTime.Now.Minute != shownMinute)
                InvalidateVisual();
        };
        Loaded += (_, _) => tick.Start();
        Unloaded += (_, _) => tick.Stop();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        double scale = Math.Max(size.Width / BoardWidth, size.Height / BoardHeight);
        double left = (size.Width - BoardWidth * scale) / 2, top = (size.Height - BoardHeight * scale) / 2;
        dc.PushClip(new RectangleGeometry(new Rect(size), CornerRadius, CornerRadius));
        dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, left, top));
        dc.DrawDrawing(scene ??= Paint());
        var now = DateTime.Now;
        Hands(dc, now);
        shownMinute = now.Minute;
        dc.Pop();
        dc.Pop();
    }

    // --- Colours ----------------------------------------------------------------------------

    static readonly Color Ink = Rgb(0x1C2230);
    static readonly Brush InkBrush = Solid(Ink);
    static readonly Color GoldLight = Rgb(0xFFE58C), Gold = Rgb(0xF0C040), GoldDeep = Rgb(0xB8861A), GoldDark = Rgb(0x8A6410);

    // A rod or a drum of gold, lit from the left: dark at both edges, brightest a third across.
    static readonly Brush GoldRound = Frozen(new LinearGradientBrush(new GradientStopCollection
    {
        new(GoldDark, 0), new(GoldLight, 0.32), new(Gold, 0.6), new(GoldDeep, 0.85), new(GoldDark, 1),
    }, new Point(0, 0.5), new Point(1, 0.5)));

    // --- Perspective --------------------------------------------------------------------------

    // Looking up from the foot of the clock, upright edges lean in toward a point high above the
    // picture. Shapes are laid out upright, as if seen straight on, and leaned as they're drawn.
    const double VanishX = 150, VanishY = -560, Ground = BoardHeight;

    static Point Lean(double x, double y) => new(VanishX + (x - VanishX) * (y - VanishY) / (Ground - VanishY), y);

    // --- The scene ----------------------------------------------------------------------------

    static Drawing Paint()
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            Sky(dc);
            foreach (var cloud in Clouds)
                Cloud(dc, cloud);
            foreach (var building in Buildings)
                Draw(dc, building);
            Paving(dc);
            Tree(dc, 113, 452, 521);
            Tree(dc, 187, 458, 521);
            Clock(dc);
        }
        group.Freeze();
        return group;
    }

    static void Sky(DrawingContext dc)
    {
        var sky = new LinearGradientBrush(new GradientStopCollection
        {
            new(Rgb(0x2470C8), 0), new(Rgb(0x4A9DE4), 0.4), new(Rgb(0x9FD3F4), 0.85), new(Rgb(0xC8E6F8), 1),
        }, new Point(0, 0), new Point(0, 1));
        dc.DrawRectangle(Frozen(sky), null, new Rect(0, 0, BoardWidth, BoardHeight));

        // Screentone over the top of the sky, fading out on its way down.
        var dots = new GeometryGroup();
        dots.Children.Add(new EllipseGeometry(new Point(1.25, 1.25), 0.85, 0.85));
        dots.Children.Add(new EllipseGeometry(new Point(3.75, 3.75), 0.85, 0.85));
        var tone = new DrawingBrush(new GeometryDrawing(Solid(Color.FromArgb(0x50, 0x12, 0x4A, 0x9A)), null, dots))
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 5, 5),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 5, 5),
            ViewboxUnits = BrushMappingMode.Absolute,
        };
        dc.PushOpacityMask(Frozen(new LinearGradientBrush(Colors.Black, Colors.Transparent, 90)));
        dc.DrawRectangle(Frozen(tone), null, new Rect(0, 0, BoardWidth, 300));
        dc.Pop();
    }

    // Each cloud is a bank of puffs: (x, y, width radius, height radius).
    static readonly (double X, double Y, double Rx, double Ry)[][] Clouds =
    [
        [(18, 62, 24, 13), (44, 48, 22, 19), (72, 42, 25, 21), (100, 52, 20, 15), (60, 64, 46, 11)],
        [(214, 34, 18, 12), (238, 22, 21, 17), (266, 28, 22, 16), (290, 40, 18, 11), (252, 42, 40, 9)],
        [(118, 238, 14, 9), (134, 228, 15, 13), (154, 232, 13, 10), (138, 244, 26, 7)],
        [(170, 272, 12, 8), (186, 264, 14, 11), (204, 272, 12, 8), (188, 278, 22, 6)],
    ];

    static void Cloud(DrawingContext dc, (double X, double Y, double Rx, double Ry)[] puffs)
    {
        // Nonzero, so where puffs overlap they merge rather than cancel out.
        var shade = new GeometryGroup { FillRule = FillRule.Nonzero };
        var body = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var (x, y, rx, ry) in puffs)
        {
            shade.Children.Add(new EllipseGeometry(new Point(x + 1.5, y + 3), rx, ry));
            body.Children.Add(new EllipseGeometry(new Point(x, y), rx, ry));
        }
        dc.DrawGeometry(Solid(Rgb(0xCFE3F5)), null, Frozen(shade));
        dc.DrawGeometry(Brushes.White, null, Frozen(body));
    }

    sealed record Building(double Left, double Right, double Top, Color Face, Color Side, double SideWidth,
                           double SideDrop, int Columns, double Pitch, bool Glass = false);

    // Farthest first. A building's side shows on whichever side faces the clock.
    static readonly Building[] Buildings =
    [
        new(180, 262, 300, Rgb(0xE6EDF4), Rgb(0xC3D1DF), 12, 7, 3, 22),
        new(40, 116, 286, Rgb(0xEAF0F6), Rgb(0xC6D3E0), 12, 7, 3, 22),
        new(110, 190, 330, Rgb(0xEFD3B4), Rgb(0xD6B48F), 0, 0, 4, 21),
        new(-74, 62, 150, Rgb(0xF6F8FB), Rgb(0xCCD8E4), 24, 14, 3, 30),
        new(236, 378, 128, Rgb(0xF1F5F9), Rgb(0xC7D5E3), 26, 15, 4, 26, Glass: true),
    ];

    static void Draw(DrawingContext dc, Building b)
    {
        bool sideRight = (b.Left + b.Right) / 2 < VanishX;
        double inner = sideRight ? b.Right : b.Left, outer = sideRight ? b.Right + b.SideWidth : b.Left - b.SideWidth;

        // The side, in shade, with a line at each floor.
        if (b.SideWidth > 0)
        {
            dc.DrawGeometry(Solid(b.Side), Pen(1.6), Shape(Lean(inner, Ground), Lean(inner, b.Top),
                                                           Lean(outer, b.Top + b.SideDrop), Lean(outer, Ground)));
            var floors = new StreamGeometry();
            using (var g = floors.Open())
            {
                for (double y = b.Top + b.Pitch; y < Ground; y += b.Pitch)
                {
                    double drop = b.SideDrop * (Ground - y) / (Ground - b.Top);
                    g.BeginFigure(Lean(inner, y), false, false);
                    g.LineTo(Lean(outer, y + drop), true, false);
                }
            }
            dc.DrawGeometry(null, Pen(0.7, 0x80), Frozen(floors));
        }

        // The front, with a ledge along the roof.
        dc.DrawGeometry(Solid(b.Face), Pen(1.6), Shape(Lean(b.Left, Ground), Lean(b.Left, b.Top),
                                                       Lean(b.Right, b.Top), Lean(b.Right, Ground)));
        dc.DrawGeometry(Solid(b.Side), Pen(1.4), Shape(Lean(b.Left - 2, b.Top - 5), Lean(b.Right + 2, b.Top - 5),
                                                       Lean(b.Right + 2, b.Top), Lean(b.Left - 2, b.Top)));

        // Rows of windows, each with a glint across its top corner.
        double width = b.Right - b.Left, margin = width * 0.12, gap = width * 0.06;
        double pane = (width - 2 * margin - (b.Columns - 1) * gap) / b.Columns;
        double tall = b.Pitch * (b.Glass ? 0.62 : 0.5);
        var panes = new StreamGeometry();
        var glints = new StreamGeometry();
        using (var p = panes.Open())
        using (var s = glints.Open())
        {
            for (double y = b.Top + b.Pitch * 0.45; y + tall < Ground - 6; y += b.Pitch)
            {
                for (int c = 0; c < b.Columns; c++)
                {
                    double x = b.Left + margin + c * (pane + gap);
                    Figure(p, Lean(x, y), Lean(x + pane, y), Lean(x + pane, y + tall), Lean(x, y + tall));
                    Figure(s, Lean(x, y), Lean(x + pane * 0.55, y), Lean(x, y + tall * 0.75));
                }
            }
        }
        dc.DrawGeometry(Solid(b.Glass ? Rgb(0x7DB9EA) : Rgb(0x9CCBEF)), Pen(0.9), Frozen(panes));
        dc.DrawGeometry(Solid(Color.FromArgb(0xB0, 0xE6, 0xF4, 0xFD)), null, Frozen(glints));
    }

    // The paving round the clock, in rings, curving away at the foot of the picture.
    static void Paving(DrawingContext dc)
    {
        var ground = new StreamGeometry();
        using (var g = ground.Open())
        {
            g.BeginFigure(new Point(0, BoardHeight), true, true);
            g.LineTo(new Point(0, 530), true, false);
            g.QuadraticBezierTo(new Point(150, 500), new Point(BoardWidth, 530), true, false);
            g.LineTo(new Point(BoardWidth, BoardHeight), true, false);
        }
        ground.Freeze();
        var paving = new LinearGradientBrush(Rgb(0xEFE6D6), Rgb(0xD8C9AE), 90);
        dc.DrawGeometry(Frozen(paving), Pen(1.6), ground);
        dc.PushClip(ground);
        foreach (double r in new[] { 46.0, 78, 114, 156, 204 })
            dc.DrawEllipse(null, Pen(1, 0x55), new Point(150, 541), r, r * 0.2);
        dc.Pop();
    }

    static void Tree(DrawingContext dc, double x, double top, double foot)
    {
        double h = foot - top;
        var crown = new StreamGeometry();
        using (var g = crown.Open())
        {
            g.BeginFigure(new Point(x, top), true, true);
            g.BezierTo(new Point(x + 10, top + h * 0.3), new Point(x + 15, foot - 14), new Point(x + 4, foot - 4), true, true);
            g.LineTo(new Point(x - 4, foot - 4), true, true);
            g.BezierTo(new Point(x - 15, foot - 14), new Point(x - 10, top + h * 0.3), new Point(x, top), true, true);
        }
        crown.Freeze();
        dc.DrawRectangle(Solid(Rgb(0x6B4A2B)), Pen(1), new Rect(x - 2, foot - 6, 4, 8));
        dc.DrawGeometry(Frozen(new LinearGradientBrush(Rgb(0x5DB860), Rgb(0x2F7F3A), 0)), Pen(1.4), crown);
        var leaves = new StreamGeometry();
        using (var g = leaves.Open())
        {
            for (int i = 1; i <= 3; i++)
            {
                double y = top + h * (0.25 + 0.2 * i);
                g.BeginFigure(new Point(x - 2, y - 6), false, false);
                g.QuadraticBezierTo(new Point(x + 3, y - 1), new Point(x + 6, y - 4), true, true);
            }
        }
        dc.DrawGeometry(null, Pen(1, 0x99), Frozen(leaves));
    }

    // The clock: a drum on the paving, a long rod and the dial at the top.
    const double DialX = 150, DialY = 118, DialRadius = 36;

    static void Clock(DrawingContext dc)
    {
        var drum = new StreamGeometry();
        using (var g = drum.Open())
        {
            g.BeginFigure(new Point(124, 542), true, true);
            g.LineTo(new Point(130, 498), true, true);
            g.ArcTo(new Point(170, 498), new Size(20, 5), 0, false, SweepDirection.Counterclockwise, true, true);
            g.LineTo(new Point(176, 542), true, true);
            g.ArcTo(new Point(124, 542), new Size(26, 6.5), 0, false, SweepDirection.Clockwise, true, true);
        }
        drum.Freeze();
        dc.DrawGeometry(GoldRound, Pen(1.8), drum);
        dc.DrawEllipse(Solid(GoldLight), Pen(1.6), new Point(150, 498), 20, 5);
        dc.DrawGeometry(null, Pen(1.1, 0xA0), Arc(new Point(126, 530), new Point(174, 530), 24.5, 6, SweepDirection.Counterclockwise));

        // The rod, thinning toward the top, with a collar halfway up.
        dc.DrawGeometry(GoldRound, Pen(1.6), Shape(new Point(145, 498), new Point(146.6, DialY + DialRadius + 4),
                                                   new Point(153.4, DialY + DialRadius + 4), new Point(155, 498)));
        dc.DrawRoundedRectangle(GoldRound, Pen(1.4), new Rect(142.5, 330, 15, 9), 3, 3);
        dc.DrawRoundedRectangle(GoldRound, Pen(1.4), new Rect(143.5, DialY + DialRadius - 2, 13, 9), 3, 3);

        // The dial: a thick gold rim, a white face and the hour marks.
        var rim = new LinearGradientBrush(new GradientStopCollection
        {
            new(GoldLight, 0), new(Gold, 0.45), new(GoldDeep, 0.8), new(GoldDark, 1),
        }, new Point(0.2, 0), new Point(0.8, 1));
        var dial = new Point(DialX, DialY);
        dc.DrawEllipse(Frozen(rim), Pen(2.2), dial, DialRadius, DialRadius);
        dc.DrawEllipse(Solid(Rgb(0xFFFDF4)), Pen(1.5), dial, DialRadius - 8, DialRadius - 8);
        dc.DrawGeometry(null, new Pen(Solid(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                        Arc(new Point(DialX - 30, DialY - 14), new Point(DialX - 12, DialY - 31), 33, 33, SweepDirection.Clockwise));
        for (int hour = 0; hour < 12; hour++)
        {
            double a = hour * Math.PI / 6;
            bool quarter = hour % 3 == 0;
            double outer = DialRadius - 11, inner = DialRadius - (quarter ? 17 : 14.5);
            dc.DrawLine(Pen(quarter ? 2.2 : 1.3), Polar(dial, a, inner), Polar(dial, a, outer));
        }
    }

    static void Hands(DrawingContext dc, DateTime now)
    {
        var dial = new Point(DialX, DialY);
        double minutes = now.Minute + now.Second / 60.0;
        double hour = (now.Hour % 12 + minutes / 60) * Math.PI / 6, minute = minutes * Math.PI / 30;
        dc.DrawLine(Pen(3.4), dial, Polar(dial, hour, 13));
        dc.DrawLine(Pen(2.2), dial, Polar(dial, minute, 21));
        dc.DrawEllipse(Solid(Gold), Pen(1.2), dial, 3.2, 3.2);
    }

    // --- Helpers ------------------------------------------------------------------------------

    // A point at angle a, clockwise from twelve o'clock.
    static Point Polar(Point centre, double a, double r) => new(centre.X + r * Math.Sin(a), centre.Y - r * Math.Cos(a));

    static Geometry Shape(params Point[] corners)
    {
        var shape = new StreamGeometry();
        using (var g = shape.Open())
            Figure(g, corners);
        return Frozen(shape);
    }

    static void Figure(StreamGeometryContext g, params Point[] corners)
    {
        g.BeginFigure(corners[0], true, true);
        for (int i = 1; i < corners.Length; i++)
            g.LineTo(corners[i], true, true);
    }

    static Geometry Arc(Point from, Point to, double rx, double ry, SweepDirection sweep)
    {
        var arc = new StreamGeometry();
        using (var g = arc.Open())
        {
            g.BeginFigure(from, false, false);
            g.ArcTo(to, new Size(rx, ry), 0, false, sweep, true, false);
        }
        return Frozen(arc);
    }

    static Pen Pen(double thickness, byte alpha = 0xFF)
    {
        var brush = alpha == 0xFF ? InkBrush : Solid(Color.FromArgb(alpha, Ink.R, Ink.G, Ink.B));
        return Frozen(new Pen(brush, thickness)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        });
    }

    static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    static SolidColorBrush Solid(Color color) => Frozen(new SolidColorBrush(color));

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
