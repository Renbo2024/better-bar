using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Threading;
using ManagedShell.WindowsTray;

namespace BetterBarApp.Services;

/// <summary>One immutable, render-safe view of a tray icon, produced on the tray thread and consumed
/// on the UI thread. <see cref="Icon"/> is frozen so it can cross threads; <see cref="Source"/> is the
/// live <see cref="NotifyIcon"/> — only ever touched again via <see cref="TrayHostService.Post"/>.</summary>
public sealed record TrayIconSnapshot(NotifyIcon Source, ImageSource? Icon, string Title, string Guid);

/// <summary>
/// Owns the single process-wide notification area (system tray). BetterBar becomes the tray host —
/// receiving Shell_NotifyIcon messages in place of the (hidden) Explorer taskbar.
///
/// The host runs on its OWN dedicated STA thread, NOT the UI thread. When BetterBar becomes the host
/// every app re-registers its icon at once, and ManagedShell resolves each one (icon extraction,
/// owner-process/UWP lookup) synchronously — a burst that can block its thread for many seconds. By
/// keeping that thread separate, the bar's UI thread never freezes.
///
/// Producer/consumer split: change notifications on the tray thread only set a dirty flag; a 0.5s
/// timer on that thread batches them into a frozen <see cref="Snapshot"/> and raises
/// <see cref="SnapshotChanged"/>. The UI renders from the snapshot and never reads the live objects.
/// </summary>
public static class TrayHostService
{
    private static Thread? _thread;
    private static Dispatcher? _dispatcher;
    private static NotificationArea? _area;
    private static DispatcherTimer? _timer;
    private static volatile bool _dirty;
    private static readonly HashSet<NotifyIcon> _hooked = [];

    private static volatile IReadOnlyList<TrayIconSnapshot> _snapshot = [];

    /// <summary>Latest published icon snapshot. Safe to read from any thread.</summary>
    public static IReadOnlyList<TrayIconSnapshot> Snapshot => _snapshot;

    /// <summary>Raised (on the tray thread) after a new <see cref="Snapshot"/> is published.</summary>
    public static event Action? SnapshotChanged;

    /// <summary>Spins up the tray host thread once. Blocks only until the thread is ready.</summary>
    public static void Ensure()
    {
        if (_thread != null) return;

        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            try
            {
                _area = new NotificationArea(new TrayService(), new ExplorerTrayService());
                _area.Initialize();   // becomes the tray host; triggers the re-register burst
                HookArea();
                Publish();            // seed an initial (possibly empty) snapshot
            }
            catch { /* IsFailed → no icons; never crash the bar */ }

            // Batch consumer: coalesce all the burst's churn into ≤2 publishes/second.
            _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(500),
            };
            _timer.Tick += (_, _) => { if (_dirty) { _dirty = false; Publish(); } };
            _timer.Start();

            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "BetterBar Tray Host",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();
    }

    // ── tray thread ──────────────────────────────────────────────────────────────────────────────
    private static void HookArea()
    {
        if (_area?.TrayIcons is INotifyCollectionChanged incc)
            incc.CollectionChanged += OnIconsChanged;
        HookIcons();
    }

    private static void OnIconsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HookIcons();
        _dirty = true;
    }

    // Add/remove leaves the collection unchanged but an icon's image/title/visibility can still change,
    // so each icon is watched individually; any of those marks us dirty for the next publish.
    private static void HookIcons()
    {
        var current = _area?.TrayIcons?.Cast<NotifyIcon>().ToHashSet() ?? [];
        foreach (var ni in _hooked.Where(n => !current.Contains(n)).ToList())
        {
            ni.PropertyChanged -= OnIconPropertyChanged;
            _hooked.Remove(ni);
        }
        foreach (var ni in current)
            if (_hooked.Add(ni)) ni.PropertyChanged += OnIconPropertyChanged;
    }

    private static void OnIconPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NotifyIcon.Icon) or nameof(NotifyIcon.Title)
            or nameof(NotifyIcon.IsHidden) or nameof(NotifyIcon.IsPinned))
            _dirty = true;
    }

    // Build a render-safe snapshot of the currently-visible icons and publish it.
    private static void Publish()
    {
        var list = new List<TrayIconSnapshot>();
        if (_area?.TrayIcons != null)
            foreach (var o in _area.TrayIcons)
                if (o is NotifyIcon ni && !ni.IsHidden)
                {
                    ImageSource? icon = ni.Icon;
                    if (icon != null && !icon.IsFrozen)         // freeze a copy so the UI thread can draw it
                    {
                        if (icon.CanFreeze) { icon = (ImageSource)icon.Clone(); icon.Freeze(); }
                        else icon = null;
                    }
                    list.Add(new TrayIconSnapshot(ni, icon, ni.Title ?? "", ni.GUID.ToString()));
                }

        _snapshot = list;
        SnapshotChanged?.Invoke();
    }

    /// <summary>Marshal an interaction with a live <see cref="NotifyIcon"/> onto the tray thread
    /// (mouse forwarding, placement). Safe to call from the UI thread; no-op if the host is down.</summary>
    public static void Post(Action work)
    {
        var d = _dispatcher;
        if (d == null) return;
        d.BeginInvoke(() => { try { work(); } catch { } });
    }

    public static void Shutdown()
    {
        var d = _dispatcher;
        var t = _thread;
        if (d == null) return;

        // Dispose on the tray thread so the tray host is released (Explorer can reclaim it), and wait
        // briefly so it actually happens before the process exits.
        try
        {
            var op = d.InvokeAsync(() =>
            {
                try { _timer?.Stop(); } catch { }
                try { (_area as IDisposable)?.Dispose(); } catch { }
            });
            op.Task.Wait(TimeSpan.FromSeconds(2));
        }
        catch { }

        try { d.InvokeShutdown(); } catch { }
        try { t?.Join(TimeSpan.FromSeconds(1)); } catch { }

        _thread = null;
        _dispatcher = null;
        _area = null;
    }
}
