using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace BattleCity.Launcher;

/// <summary>
/// A line of text inked round its edge and cast onto the page with a hard shadow, the way a
/// title on a manga cover is drawn. The launcher's name is set in it.
/// </summary>
public sealed class OutlinedText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = Register(nameof(Text), "");
    public static readonly DependencyProperty FontFamilyProperty = Register(nameof(FontFamily), new FontFamily("Segoe UI"));
    public static readonly DependencyProperty FontSizeProperty = Register(nameof(FontSize), 32.0);
    public static readonly DependencyProperty FontWeightProperty = Register(nameof(FontWeight), FontWeights.Black);
    public static readonly DependencyProperty FontStyleProperty = Register(nameof(FontStyle), FontStyles.Normal);
    public static readonly DependencyProperty FillProperty = Register<Brush>(nameof(Fill), Brushes.White);
    public static readonly DependencyProperty StrokeProperty = Register<Brush>(nameof(Stroke), Brushes.Black);
    public static readonly DependencyProperty StrokeThicknessProperty = Register(nameof(StrokeThickness), 4.0);
    public static readonly DependencyProperty ShadowProperty = Register(nameof(Shadow), 0.0);

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }
    public FontStyle FontStyle { get => (FontStyle)GetValue(FontStyleProperty); set => SetValue(FontStyleProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    /// <summary>The outline's full width; half of it falls outside the letters.</summary>
    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }

    /// <summary>How far down and right the shadow falls.</summary>
    public double Shadow { get => (double)GetValue(ShadowProperty); set => SetValue(ShadowProperty, value); }

    Geometry? letters;

    protected override Size MeasureOverride(Size available)
    {
        var text = new FormattedText(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                                     new Typeface(FontFamily, FontStyle, FontWeight, FontStretches.Normal),
                                     FontSize, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var shapes = text.BuildGeometry(default);
        var ink = shapes.Bounds;
        if (ink.IsEmpty)
        {
            letters = null;
            return default;
        }
        // The element is sized to the ink itself, outline included, rather than to the line the
        // font would leave room for.
        double edge = StrokeThickness / 2;
        shapes.Transform = new TranslateTransform(edge - ink.Left, edge - ink.Top);
        shapes.Freeze();
        letters = shapes;
        return new Size(ink.Width + 2 * edge + Shadow, ink.Height + 2 * edge + Shadow);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (letters is null)
            return;
        var outline = new Pen(Stroke, StrokeThickness) { LineJoin = PenLineJoin.Round };
        if (Shadow > 0)
        {
            dc.PushTransform(new TranslateTransform(Shadow, Shadow));
            dc.DrawGeometry(Stroke, outline, letters);
            dc.Pop();
        }
        // The fill goes over the inner half of the outline, so the letters keep their full shape.
        dc.DrawGeometry(null, outline, letters);
        dc.DrawGeometry(Fill, null, letters);
    }

    static DependencyProperty Register<T>(string name, T value) =>
        DependencyProperty.Register(name, typeof(T), typeof(OutlinedText), new FrameworkPropertyMetadata(
            value, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
}
