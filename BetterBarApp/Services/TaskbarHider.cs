using ManagedShell.AppBar;
using ManagedShell.Interop;

namespace BetterBarApp.Services;

/// <summary>
/// Hides Explorer taskbars while BetterBar runs and restores them on shutdown,
/// preserving the user's original AppBar state (auto-hide / always-on-top).
/// </summary>
public static class TaskbarHider
{
    // RetroBar uses ONE ExplorerHelper — the very one its AppBarManager holds. That matters: when our
    // AppBars register/change, the Windows taskbar "likes to pop up" (ManagedShell's words), and the
    // AppBarManager only re-suppresses it when ITS OWN helper has HideExplorerTaskbar==true. A separate
    // helper here would leave the manager's copy false, so the taskbar reclaims the edge and our bars
    // never get their reservation. So we drive the shared ShellService.ExplorerHelper.
    private static ExplorerHelper        _helper => ShellService.ExplorerHelper;
    private static NativeMethods.ABState _savedState;
    private static bool                  _hasSavedState;
    private static bool                  _hiding;        // true between HideAll and ShowAll

    public static void HideAll()
    {
        // Touch the AppBarManager first so it exists (sharing this helper) BEFORE we flip HideExplorerTaskbar,
        // exactly as RetroBar's ShellManager is fully built before WindowManager hides the taskbar.
        _ = ShellService.AppBarManager;

        // Snapshot the user's current taskbar state BEFORE forcing AutoHide,
        // so we can put it back exactly as we found it on exit.
        if (!_hasSavedState)
        {
            _savedState    = _helper.GetTaskbarState();
            _hasSavedState = true;
        }
        _helper.HideExplorerTaskbar = true;
        _hiding = true;
        StartWatchdog();
    }

    public static void ShowAll()
    {
        _hiding = false;
        StopWatchdog();
        // Show the taskbar window, then restore the original auto-hide preference.
        // HideExplorerTaskbar=true internally sets AutoHide; without this, the
        // user's prior "never auto-hide" preference would be lost.
        _helper.HideExplorerTaskbar = false;
        if (_hasSavedState)
            _helper.SetTaskbarState(_savedState);
    }

    // Non-starved re-hide watchdog. The pointer-over-bar reassert (PanelWindow) only fires while the
    // BARS receive mouse moves — but the instant the auto-hidden taskbar slides up over the edge it
    // captures the hover, so the bar stops getting MouseMove and the reveal lingers until ManagedShell's
    // own re-hide finally runs. ManagedShell's backstop is a DispatcherTimer at *Background* priority on
    // the UI thread, BELOW WPF rendering, so the monitor graphs' per-frame repaint starves it.
    //
    // This watchdog runs on a dedicated BACKGROUND thread (a threadpool timer), so WPF rendering can't
    // starve it, at a fast cadence — and it only re-hides when the cursor is actually near a monitor's
    // bottom edge, exactly where an auto-hide reveal triggers. That squashes the reveal within a frame
    // even when the taskbar has stolen the pointer, while leaving the taskbar untouched when the mouse is
    // anywhere else. Re-hiding is a plain cross-thread SetWindowPos (safe off the UI thread). Runs only
    // while we're hiding the taskbar.
    private static System.Threading.Timer? _watchdog;
    private static int _watchdogBusy;   // 0/1 re-entrancy guard for overlapping threadpool callbacks

    // How far above a monitor's bottom edge (physical px) counts as "about to reveal the taskbar".
    // Generous so it covers tall bars and high-DPI scaling; acting here costs one SetWindowPos.
    private const int BottomHotZonePx = 120;

    private static void StartWatchdog()
    {
        if (_watchdog != null) return;
        _watchdog = new System.Threading.Timer(_ => WatchdogTick(), null, dueTime: 0, period: 20);
    }

    private static void StopWatchdog()
    {
        _watchdog?.Dispose();
        _watchdog = null;
    }

    private static void WatchdogTick()
    {
        if (!_hiding) return;
        // Skip if a previous tick is still running (they run on threadpool threads and could overlap).
        if (System.Threading.Interlocked.Exchange(ref _watchdogBusy, 1) == 1) return;
        try
        {
            var p   = System.Windows.Forms.Cursor.Position;        // physical px, any thread
            var scr = System.Windows.Forms.Screen.FromPoint(p);
            if (p.Y >= scr.Bounds.Bottom - BottomHotZonePx)
                Reassert();
        }
        catch { /* best-effort; ManagedShell's timer remains the backstop */ }
        finally { System.Threading.Interlocked.Exchange(ref _watchdogBusy, 0); }
    }

    /// <summary>
    /// Re-hides the (auto-hidden) Explorer taskbar right now. ManagedShell already re-hides it, but the
    /// PRIMARY taskbar is covered only by ExplorerHelper's internal DispatcherTimer, which runs at
    /// Background priority on the UI thread — and BetterBar's UI thread can be busy enough (live audio
    /// meters, the CPU graph's per-frame rendering) to starve it, so an auto-hide reveal lingers. The
    /// AppBarManager's reliable, event-driven re-suppression only fires on appbar activate / position
    /// change (which is why clicking a bar dismisses a stuck reveal). This lets the bars drive that same
    /// re-hide from a non-starved input event (pointer over the bar) using ManagedShell's own public
    /// calls — automating that click. No-op unless we're actually hiding the taskbar.
    /// </summary>
    public static void Reassert()
    {
        if (!_hiding) return;   // never re-hide after ShowAll (e.g. a stray watchdog tick on exit)
        try
        {
            int hide = (int)NativeMethods.SetWindowPosFlags.SWP_HIDEWINDOW;
            _helper.SetTaskbarVisibility(hide);
            _helper.SetSecondaryTaskbarVisibility(hide);
        }
        catch { /* best-effort; ManagedShell's timer remains the backstop */ }
    }
}
