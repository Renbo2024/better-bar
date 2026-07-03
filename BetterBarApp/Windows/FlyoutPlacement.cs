using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BetterBarApp.Windows;

/// <summary>
/// Shared bar-anchored placement for the click flyouts (calendar, weather, audio). Positions a
/// <c>SizeToContent</c> flyout centred on its bar item, above the bar if it fits else below, clamped
/// to the anchor's monitor — all in physical pixels (matching <c>PointToScreen</c>/<c>GetWindowRect</c>).
///
/// Mixed-DPI gotcha: a flyout parked off-screen is on the primary monitor, so <c>SizeToContent</c>
/// measures it at the primary's DPI. Moved to a differently-scaled monitor it is then mis-sized and
/// offsets up/left (or overlaps). <see cref="MoveToAnchorMonitor"/> moves it onto the anchor's monitor
/// *before* it is sized, so the measured size already matches the target scale.
/// </summary>
internal static class FlyoutPlacement
{
    /// <summary>Pre-move the (still hidden) flyout onto the anchor's monitor. Call from OnSourceInitialized.</summary>
    public static void MoveToAnchorMonitor(Window window, FrameworkElement anchor)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !AnchorCenter(anchor, out int cx, out int cy)) return;
        if (TryMonitor(cx, cy, out _, out var work))
            SetWindowPos(hwnd, IntPtr.Zero, work.Left, work.Top, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
    }

    /// <summary>
    /// Final placement. Returns true when the flyout was placed above the bar (used to pick the
    /// slide-in direction). <paramref name="gap"/> is the pixel gap to the bar (0 when the flyout's
    /// own transparent margin already provides spacing).
    /// </summary>
    public static bool Place(Window window, FrameworkElement anchor, int gap = 0)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return true;

        GetWindowRect(hwnd, out RECT win);
        int winW = win.Right - win.Left, winH = win.Bottom - win.Top;

        // Anchor centre + the whole bar's top/bottom edges (so the flyout clears the bar, not just the item).
        var topLeft     = anchor.PointToScreen(new Point(0, 0));
        var bottomRight = anchor.PointToScreen(new Point(anchor.ActualWidth, anchor.ActualHeight));
        double centerX  = (topLeft.X + bottomRight.X) / 2;

        int panelTop = (int)Math.Round(topLeft.Y), panelBottom = (int)Math.Round(bottomRight.Y);
        var host = Window.GetWindow(anchor);
        if (host != null)
        {
            var ph = new WindowInteropHelper(host).Handle;
            if (ph != IntPtr.Zero && GetWindowRect(ph, out RECT pr)) { panelTop = pr.Top; panelBottom = pr.Bottom; }
        }

        // The anchor's monitor, so the flyout never spills onto another screen.
        int monLeft = 0, monTop = panelTop, monRight = panelBottom, monBottom = panelBottom;
        if (TryMonitor((int)centerX, (panelTop + panelBottom) / 2, out var mon, out _))
        {
            monLeft = mon.Left; monTop = mon.Top; monRight = mon.Right; monBottom = mon.Bottom;
        }

        // Horizontal: centre on the item, clamped to the monitor.
        int x = (int)Math.Round(centerX - winW / 2.0);
        x = Math.Max(monLeft, Math.Min(x, monRight - winW));

        // Vertical: above the bar if it fits, otherwise below it (top-docked bars); clamp on-screen.
        int above = panelTop - winH - gap;
        bool placedAbove = above >= monTop;
        int y = placedAbove ? above : panelBottom + gap;
        y = Math.Max(monTop, Math.Min(y, monBottom - winH));

        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
        return placedAbove;
    }

    private static bool AnchorCenter(FrameworkElement anchor, out int cx, out int cy)
    {
        cx = cy = 0;
        try
        {
            var tl = anchor.PointToScreen(new Point(0, 0));
            var br = anchor.PointToScreen(new Point(anchor.ActualWidth, anchor.ActualHeight));
            cx = (int)Math.Round((tl.X + br.X) / 2);
            cy = (int)Math.Round((tl.Y + br.Y) / 2);
            return true;
        }
        catch { return false; }
    }

    private static bool TryMonitor(int x, int y, out RECT monitor, out RECT work)
    {
        monitor = default; work = default;
        var mon = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(mon, ref mi)) return false;
        monitor = mi.rcMonitor; work = mi.rcWork;
        return true;
    }

    // ── Win32 (physical pixels) ──────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO mi);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint MONITOR_DEFAULTTONEAREST = 0x2;
}
