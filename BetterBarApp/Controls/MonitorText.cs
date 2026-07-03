using System.Windows.Media;

namespace BetterBarApp.Controls;

/// <summary>Shared text treatment for monitor widgets (CPU / network).</summary>
internal static class MonitorText
{
    /// <summary>
    /// Sets an <see cref="OutlinedTextBlock"/>'s fill colour and, when <paramref name="shadow"/>
    /// is on, a high-contrast halo around the glyphs of size <paramref name="shadowSize"/> px so
    /// the graph behind can't obscure it. The halo is the opposite luminance of the text — dark
    /// behind light text, light behind dark text. <paramref name="themeDefault"/> supplies the
    /// colour when <paramref name="color"/> is blank (the theme's default text colour).
    /// </summary>
    public static void ApplyLine(OutlinedTextBlock tb, string color, bool shadow, double shadowSize,
                                 Color? themeDefault)
    {
        var fill = ParseColor(color) ?? themeDefault ?? Colors.White;
        tb.Fill = new SolidColorBrush(fill);

        if (shadow && shadowSize > 0)
        {
            double lum = (0.299 * fill.R + 0.587 * fill.G + 0.114 * fill.B) / 255.0;
            tb.Stroke = new SolidColorBrush(lum > 0.5 ? Colors.Black : Colors.White);
            tb.StrokeThickness = shadowSize;
        }
        else
        {
            tb.StrokeThickness = 0;
        }
    }

    private static Color? ParseColor(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        try { return (Color)ColorConverter.ConvertFromString(s)!; }
        catch { return null; }
    }
}
