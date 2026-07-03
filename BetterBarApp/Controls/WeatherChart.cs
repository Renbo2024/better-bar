using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TextBlock = System.Windows.Controls.TextBlock;

namespace BetterBarApp.Controls;

/// <summary>
/// A small themed line chart used in the weather flyout: a titled card with the value range and one
/// or two polylines over a fixed plot area. Built from XAML primitives (so DynamicResource theming
/// works) given a series of values; the y-axis auto-scales to the data.
/// </summary>
public static class WeatherChart
{
    private const double PlotW = 230, PlotH = 46, CardW = 248;

    /// <summary>
    /// Builds a chart card. When <paramref name="axisMin"/>/<paramref name="axisMax"/> are given the
    /// y-axis is fixed to that range (e.g. 0–100 for precipitation chance) rather than auto-scaled to
    /// the data; the range label still shows the data's actual min–max.
    /// </summary>
    public static FrameworkElement Build(string title, string unit,
        IReadOnlyList<double> s1, IReadOnlyList<double>? s2, Brush stroke1, Brush? stroke2 = null, bool fill = false,
        double? axisMin = null, double? axisMax = null)
    {
        var all = (s2 != null ? s1.Concat(s2) : s1).ToList();
        double min = all.Count > 0 ? all.Min() : 0;
        double max = all.Count > 0 ? all.Max() : 1;

        // Plotting bounds: fixed axis when supplied, else the data range with a little padding.
        double lo, hi;
        if (axisMin is { } amin && axisMax is { } amax) { lo = amin; hi = amax; }
        else { double span = max - min, pad = span <= 0 ? 1 : span * 0.12; lo = min - pad; hi = max + pad; }

        var card = new Border
        {
            Width        = CardW,
            CornerRadius = new CornerRadius(6),
            Padding      = new Thickness(9, 6, 9, 8),
            Margin       = new Thickness(0, 0, 0, 8),
        };
        card.SetResourceReference(Border.BackgroundProperty, "StartMenuFieldBg");

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(PlotH) });

        var titleTb = new TextBlock { Text = title, FontSize = 11.5, FontWeight = FontWeights.SemiBold };
        titleTb.SetResourceReference(TextBlock.ForegroundProperty, "TaskBtnFg");
        var rangeTb = new TextBlock
        {
            Text = all.Count > 0 ? $"{Fmt(min)}–{Fmt(max)}{unit}" : "—",
            FontSize = 11, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Right,
        };
        rangeTb.SetResourceReference(TextBlock.ForegroundProperty, "TaskBtnFg");
        var header = new Grid { Margin = new Thickness(0, 0, 0, 3) };
        header.Children.Add(titleTb);
        header.Children.Add(rangeTb);
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        var canvas = new Canvas { Width = PlotW, Height = PlotH, ClipToBounds = true };
        Grid.SetRow(canvas, 1);
        grid.Children.Add(canvas);

        // Two series → fill the band between them (e.g. the daily High/Low range); single series with
        // fill=true → fill the area down to the baseline (e.g. precip chance).
        if (s2 is { Count: > 0 })      canvas.Children.Add(MakeBand(s1, s2, lo, hi, stroke1));
        else if (fill && s1.Count > 0) canvas.Children.Add(MakeArea(s1, lo, hi, stroke1));
        if (s1.Count > 0)              canvas.Children.Add(MakeLine(s1, lo, hi, stroke1));
        if (s2 is { Count: > 0 })      canvas.Children.Add(MakeLine(s2, lo, hi, stroke2 ?? stroke1));

        card.Child = grid;
        return card;
    }

    private static PointCollection Points(IReadOnlyList<double> s, double lo, double hi)
    {
        double range = hi - lo <= 0 ? 1 : hi - lo;
        var pts = new PointCollection();
        for (int i = 0; i < s.Count; i++)
        {
            double x = s.Count <= 1 ? PlotW / 2 : i / (double)(s.Count - 1) * PlotW;
            double y = PlotH - Math.Clamp((s[i] - lo) / range, 0, 1) * PlotH;
            pts.Add(new Point(x, y));
        }
        return pts;
    }

    private static Polyline MakeLine(IReadOnlyList<double> s, double lo, double hi, Brush stroke) => new()
    {
        Points             = Points(s, lo, hi),
        Stroke             = stroke,
        StrokeThickness    = 1.6,
        StrokeLineJoin     = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap   = PenLineCap.Round,
    };

    private static Polygon MakeArea(IReadOnlyList<double> s, double lo, double hi, Brush stroke)
    {
        var line = Points(s, lo, hi);
        var area = new PointCollection(line);
        area.Insert(0, new Point(line[0].X, PlotH));
        area.Add(new Point(line[^1].X, PlotH));
        return new Polygon { Points = area, Fill = Translucent(stroke, 0.18) };
    }

    // Filled band between an upper and lower series (upper along the top, lower back along the bottom).
    private static Polygon MakeBand(IReadOnlyList<double> upper, IReadOnlyList<double> lower, double lo, double hi, Brush stroke)
    {
        var up = Points(upper, lo, hi);
        var dn = Points(lower, lo, hi);
        var band = new PointCollection(up);
        for (int i = dn.Count - 1; i >= 0; i--) band.Add(dn[i]);
        return new Polygon { Points = band, Fill = Translucent(stroke, 0.16) };
    }

    private static Brush Translucent(Brush b, double opacity)
    {
        if (b is SolidColorBrush scb)
        {
            var c = scb.Color; c.A = (byte)(255 * opacity);
            var nb = new SolidColorBrush(c); nb.Freeze(); return nb;
        }
        return b;
    }

    private static string Fmt(double v) => Math.Round(v).ToString(CultureInfo.CurrentCulture);
}
