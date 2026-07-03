using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace BetterBarApp.Controls;

/// <summary>
/// A single line of text drawn as glyph geometry so it can carry a crisp, high-contrast
/// outline (halo) around the letters — used by the monitor widgets so titles stay legible
/// over the graph. The outline is stroked first and the fill painted on top of it, so the
/// halo sits above the graph/grid/lines but never over the text itself. With
/// <see cref="StrokeThickness"/> = 0 it renders as plain fill text.
/// </summary>
public sealed class OutlinedTextBlock : FrameworkElement
{
    private string        _text          = "";
    private FontFamily    _fontFamily    = new("Segoe UI");
    private double        _fontSize      = 12;
    private Brush         _fill          = Brushes.White;
    private Brush         _stroke        = Brushes.Black;
    private double        _strokeThickness;          // halo half-width (px); 0 = no outline
    private TextAlignment _textAlignment = TextAlignment.Center;

    public string Text                  { get => _text;          set => Set(ref _text, value); }
    public FontFamily FontFamily        { get => _fontFamily;    set => Set(ref _fontFamily, value); }
    public double FontSize              { get => _fontSize;      set => Set(ref _fontSize, value); }
    public Brush Fill                   { get => _fill;          set => Set(ref _fill, value); }
    public Brush Stroke                 { get => _stroke;        set => Set(ref _stroke, value); }
    public double StrokeThickness       { get => _strokeThickness; set => Set(ref _strokeThickness, value); }
    public TextAlignment TextAlignment  { get => _textAlignment; set => Set(ref _textAlignment, value); }

    private void Set<T>(ref T field, T value)
    {
        if (Equals(field, value)) return;
        field = value;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private FormattedText? Build()
    {
        if (string.IsNullOrEmpty(_text)) return null;
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        return new FormattedText(
            _text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(_fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            Math.Max(1, _fontSize), _fill, pixelsPerDip);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var ft = Build();
        if (ft == null) return new Size(0, 0);
        double w = double.IsInfinity(availableSize.Width) ? ft.Width : Math.Min(ft.Width, availableSize.Width);
        return new Size(w, ft.Height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var ft = Build();
        if (ft == null) return;

        ft.TextAlignment = _textAlignment;
        ft.MaxTextWidth  = Math.Max(0, ActualWidth);

        var geo = ft.BuildGeometry(new Point(0, 0));

        // Outline below, fill on top — the halo can't cover the text it surrounds.
        if (_strokeThickness > 0)
        {
            var pen = new Pen(_stroke, _strokeThickness * 2)   // straddles the path → ~thickness px each side
            {
                LineJoin    = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap   = PenLineCap.Round,
            };
            dc.DrawGeometry(null, pen, geo);
        }
        dc.DrawGeometry(_fill, null, geo);
    }
}
