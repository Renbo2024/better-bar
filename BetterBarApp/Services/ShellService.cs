using ManagedShell.AppBar;

namespace BetterBarApp.Services;

/// <summary>
/// Shared ManagedShell infrastructure for the AppBar panels. ManagedShell is the
/// engine RetroBar uses; its <see cref="AppBarManager"/> handles AppBar registration,
/// work-area reservation, multi-monitor placement and DPI (incl. system-DPI-aware)
/// correctly — which is why panels delegate positioning to it.
///
/// FullScreenHelper reuses the single process-wide TasksService owned by
/// <see cref="TaskbarService"/> — ManagedShell only permits one (its ctor registers
/// a WPF DependencyProperty), and creating a second throws. We don't use full-screen
/// auto-hide, but AppBarWindow requires a non-null helper.
/// </summary>
public static class ShellService
{
    public static ExplorerHelper   ExplorerHelper   { get; }
    public static AppBarManager    AppBarManager    { get; }
    public static FullScreenHelper FullScreenHelper { get; }

    static ShellService()
    {
        // Construct with the NotificationArea overload (passing null is fine — ManagedShell null-guards
        // it). In 0.0.337 ONLY this ctor wires up ExplorerHelper's internal 100 ms TaskbarMonitor, which
        // re-hides the Explorer taskbar whenever its auto-hide reveal slides it back into view. The
        // parameterless ctor skips that setup, which left the hidden taskbar popping up on an edge hover.
        // This is the same engine RetroBar relies on (it gets the monitor via ManagedShell's ShellManager).
        ExplorerHelper   = new ExplorerHelper(null);
        AppBarManager    = new AppBarManager(ExplorerHelper);
        FullScreenHelper = new FullScreenHelper(TaskbarService.TasksService);
    }
}
