namespace BetterBarApp.Services;

/// <summary>
/// Assigns stable, layout-based numbers to the non-primary monitors so panels can
/// be targeted by "Screen 1", "Screen 2", … instead of opaque device names.
///
/// Numbering rule (computed at launch via <see cref="Detect"/>): the primary
/// screen is intentionally excluded; the remaining screens are ordered top-to-
/// bottom, then left-to-right. Screens whose tops are within <see cref="RowTolerance"/>
/// pixels are treated as the same row (so tiny vertical misalignments don't flip
/// the order), and the leftmost wins within a row. Numbered from 1.
/// </summary>
public static class ScreenService
{
    /// <summary>Tops within this many pixels count as the same row/height.</summary>
    private const int RowTolerance = 10;

    public sealed record ScreenInfo(
        int Number, string DeviceName, bool IsPrimary, System.Drawing.Rectangle Bounds)
    {
        /// <summary>Selector / overlay label: "Primary" or "Screen N".</summary>
        public string Label => IsPrimary ? "Primary" : $"Screen {Number}";
    }

    private static List<ScreenInfo> _screens = new();

    /// <summary>Primary first (unnumbered), then the numbered screens in order.</summary>
    public static IReadOnlyList<ScreenInfo> Screens => _screens;

    /// <summary>Re-reads the connected monitors and (re)assigns numbers.</summary>
    public static void Detect()
    {
        var all = System.Windows.Forms.Screen.AllScreens;
        var result = new List<ScreenInfo>();

        foreach (var s in all.Where(s => s.Primary))
            result.Add(new ScreenInfo(0, s.DeviceName, true, s.Bounds));

        // Group non-primary screens into rows: walking top-to-bottom, a screen
        // joins the current row while its top stays within RowTolerance of the
        // row's anchor (the topmost screen in that row); otherwise it starts a
        // new row. Then order by (row, left) and number from 1.
        var rowed = new List<(System.Windows.Forms.Screen Screen, int Row)>();
        int row = 0;
        int? anchorTop = null;
        foreach (var s in all.Where(s => !s.Primary).OrderBy(s => s.Bounds.Top))
        {
            if (anchorTop is null)
                anchorTop = s.Bounds.Top;
            else if (s.Bounds.Top - anchorTop.Value > RowTolerance)
            {
                row++;
                anchorTop = s.Bounds.Top;
            }
            rowed.Add((s, row));
        }

        int n = 1;
        foreach (var (s, _) in rowed.OrderBy(x => x.Row).ThenBy(x => x.Screen.Bounds.Left))
            result.Add(new ScreenInfo(n++, s.DeviceName, false, s.Bounds));

        _screens = result;
    }

    /// <summary>
    /// The screen for a synthetic number (0 = primary; 1..N = layout-ordered), or
    /// null if that number isn't present (e.g. fewer monitors over RDP). Primary
    /// (0) always resolves — there's always exactly one primary.
    /// </summary>
    public static ScreenInfo? ForNumber(int number) =>
        _screens.FirstOrDefault(s => s.Number == number);

    /// <summary>Migration helper: maps an old device name (empty = primary) to a number.</summary>
    public static int NumberForDevice(string? deviceName)
    {
        if (string.IsNullOrEmpty(deviceName)) return 0;
        return _screens.FirstOrDefault(s => s.DeviceName == deviceName)?.Number ?? 0;
    }
}
