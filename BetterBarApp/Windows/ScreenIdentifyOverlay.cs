using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using BetterBarApp.Services;

namespace BetterBarApp.Windows;

/// <summary>
/// While the settings window is open, draws a labelled square in the upper-right of
/// every monitor ("1", "2", … for numbered screens, "Primary" for the primary) so the
/// "Screen N" entries in the monitor selector are easy to match to physical displays.
///
/// Approach (robust under Per-Monitor-V2 — see app.manifest): each overlay window
/// covers its WHOLE monitor, and the number square is a small child pinned to the
/// window's top-right corner. Because the square is a child of a monitor-sized window
/// it physically CANNOT spill onto an adjacent monitor, and its size is naturally that
/// monitor's scale (no cross-monitor DPI math). The window is placed in physical pixels
/// via SetWindowPos, then its bounds are expressed in DIPs using the monitor's real
/// scale from <c>GetDpiForWindow</c>. The window is transparent and click-through, so
/// only the little square shows and nothing is blocked.
/// </summary>
public sealed class ScreenIdentifyOverlay
{
    private readonly List<Window> _windows = new();

    public void Show(Window owner)
    {
        Hide();

        foreach (var s in ScreenService.Screens)
        {
            var w = Build(s);
            w.Show();                 // creates the HWND
            MakeClickThrough(w);

            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) { _windows.Add(w); continue; }

            // Put it physically over the target monitor so GetDpiForWindow reports THAT
            // monitor's scale, then express the same rect in DIPs for WPF.
            SetWindowPos(hwnd, HWND_TOPMOST,
                         s.Bounds.Left, s.Bounds.Top, s.Bounds.Width, s.Bounds.Height,
                         SWP_NOACTIVATE | SWP_SHOWWINDOW);
            Layout(w, hwnd, s);

            // The cross-monitor move raises WM_DPICHANGED once WPF actually adopts the
            // new scale; re-layout then so the window covers the monitor exactly (and the
            // corner square can't overflow). Same-scale monitors are already correct.
            HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
            {
                if (msg == WM_DPICHANGED) w.Dispatcher.BeginInvoke(() => Layout(w, h, s));
                return IntPtr.Zero;
            });

            _windows.Add(w);
        }
    }

    // Make the window exactly cover its monitor, in DIPs for the monitor's current scale.
    private static void Layout(Window w, IntPtr hwnd, ScreenService.ScreenInfo s)
    {
        double scale = GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0) scale = 1.0;
        w.Left   = s.Bounds.Left   / scale;
        w.Top    = s.Bounds.Top    / scale;
        w.Width  = s.Bounds.Width  / scale;
        w.Height = s.Bounds.Height / scale;
    }

    public void Hide()
    {
        foreach (var w in _windows)
        {
            try { w.Close(); } catch { }
        }
        _windows.Clear();
    }

    private static Window Build(ScreenService.ScreenInfo s)
    {
        var label = new TextBlock
        {
            Text       = s.IsPrimary ? "Primary" : s.Number.ToString(),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
        };

        var square = new Border
        {
            Width               = 84,
            Height              = 84,
            Margin              = new Thickness(0, 28, 28, 0),   // inset from the top-right corner
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment   = VerticalAlignment.Top,
            Background           = new SolidColorBrush(Color.FromArgb(0xF0, 0x00, 0x78, 0xD4)),
            BorderBrush          = Brushes.White,
            BorderThickness      = new Thickness(3),
            CornerRadius         = new CornerRadius(10),
            Child = new Viewbox { Stretch = Stretch.Uniform, Margin = new Thickness(12), Child = label },
        };

        // A transparent canvas the size of the monitor; the square sits in its corner.
        var root = new Grid { Background = Brushes.Transparent, Children = { square } };

        return new Window
        {
            WindowStyle           = WindowStyle.None,
            AllowsTransparency    = true,
            Background            = Brushes.Transparent,
            ShowInTaskbar         = false,
            ShowActivated         = false,   // don't steal focus from the settings window
            ResizeMode            = ResizeMode.NoResize,
            Topmost               = true,
            WindowStartupLocation = WindowStartupLocation.Manual,
            SizeToContent         = SizeToContent.Manual,
            Content               = root,
        };
    }

    // ── Win32 ──────────────────────────────────────────────────────────────────
    private const int GWL_EXSTYLE       = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOOLWINDOW  = 0x80;     // keep out of Alt+Tab
    private const int WS_EX_LAYERED     = 0x80000;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int  WM_DPICHANGED  = 0x02E0;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                            int X, int Y, int cx, int cy, uint uFlags);

    private static void MakeClickThrough(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED);
    }
}
