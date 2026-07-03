using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using BetterBarApp.Controls;
using BetterBarApp.Models;
using BetterBarApp.Services;
using ManagedShell.AppBar;
using ManagedShell.Interop;

namespace BetterBarApp.Windows;

/// <summary>
/// A single bar. Positioning, work-area reservation, multi-monitor placement and
/// DPI handling are delegated entirely to ManagedShell's <see cref="AppBarWindow"/>
/// (RetroBar's engine) — this class only owns the item content and the external
/// drag-drop / start-button hover behaviour.
/// </summary>
public partial class PanelWindow : AppBarWindow
{
    private readonly PanelConfig   _panel;   // placement: monitor + edge
    private readonly BarDefinition _def;     // content: height + items
    private IntPtr _hwnd;

    // Tracks what the bar is currently laid out for, so Refresh() can tell a
    // layout change (edge/monitor/height) from an item-only change.
    private PanelPosition _registeredPosition;
    private int           _registeredHeight;
    private int           _registeredMonitor;

    public PanelWindow(PanelConfig panel, BarDefinition def)
        : base(ShellService.AppBarManager,
               ShellService.ExplorerHelper,
               ShellService.FullScreenHelper,
               ResolveScreen(panel),
               ResolveEdge(panel.Position),
               AppBarMode.Normal,
               def.HeightPx)
    {
        _panel = panel;
        _def   = def;
        InitializeComponent();

        // Let ManagedShell re-place us on display / DPI / work-area changes.
        ProcessScreenChanges = true;

        _registeredPosition = panel.Position;
        _registeredHeight   = def.HeightPx;
        _registeredMonitor  = panel.MonitorNumber;
    }

    private static AppBarScreen ResolveScreen(PanelConfig panel)
    {
        var info = ScreenService.ForNumber(panel.MonitorNumber);
        if (info is { IsPrimary: false })
        {
            var match = AppBarScreen.FromAllScreens().FirstOrDefault(s => s.DeviceName == info.DeviceName);
            if (match != null) return match;
        }
        return AppBarScreen.FromPrimaryScreen();
    }

    private static AppBarEdge ResolveEdge(PanelPosition pos) =>
        pos == PanelPosition.Top ? AppBarEdge.Top : AppBarEdge.Bottom;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // RESERVE FIRST, on an empty bar. The appbar handshake (ABM_SETPOS) runs on this UI thread's
        // message pump; if we build the items first, constructing heavy controls (and starting their
        // live timers / per-frame rendering) jams the pump exactly when the reservation needs it, and
        // the OS never shrinks the work area. So we confirm/retry the reservation while the bar is still
        // empty and the pump is free, THEN fill the bar.
        EnsureReserved();

        // Build items STAGGERED — one per Background-priority dispatcher pass — so heavy item creation
        // never competes with the reservation handshake or the other bars coming up. This is the
        // "reserve first, then stagger the rest" loading sequence.
        BuildItemControls(staggered: true);

        // Pointer-over-the-bar re-hides the (auto-hidden) Explorer taskbar. The reveal only happens when
        // the cursor is at this screen edge — i.e. on/over the bar — so this catches it at the moment it
        // matters, from a non-starved input event, instead of waiting on ManagedShell's Background-priority
        // timer. Throttled. See TaskbarHider.Reassert for why this is needed.
        MouseEnter += (_, _) => ReassertTaskbar();
        MouseMove  += (_, _) => ReassertTaskbar();
    }

    private int _lastReassertTick;

    private void ReassertTaskbar()
    {
        int now = Environment.TickCount;
        if (now - _lastReassertTick < 250) return;   // at most ~4 cheap Win32 calls/sec while over the bar
        _lastReassertTick = now;
        TaskbarHider.Reassert();
    }

    /// <summary>
    /// Re-assert our AppBar work-area reservation when the OS work area changes. ManagedShell's base
    /// AppBarWindow only re-reserves via the AppBar protocol (when the shell moves our window) — it does
    /// NOT react to a raw <c>SPI_SETWORKAREA</c>. Hiding the Explorer taskbar expands the work area
    /// ASYNCHRONOUSLY, and if that expansion lands after our bar reserved it silently wipes the
    /// reservation: the bar stays visible but maximized windows extend underneath it. Both RetroBar
    /// (Taskbar.WndProc) and Cairo Shell (CairoAppBarWindow.WndProc) handle exactly this message the
    /// same way. <see cref="UpdatePosition"/> re-runs ABSetPos; it's convergent (ABM_SETPOS only
    /// re-broadcasts SPI_SETWORKAREA when the work area actually changes), so this does not loop.
    /// </summary>
    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        var result = base.WndProc(hwnd, msg, wParam, lParam, ref handled);

        if (ProcessScreenChanges && !AllowClose
            && msg == (int)NativeMethods.WM.SETTINGCHANGE
            && wParam == (IntPtr)NativeMethods.SPI.SETWORKAREA)
        {
            try { UpdatePosition(); } catch { }
        }

        return result;
    }

    // ── Reservation reliability ──────────────────────────────────────────────────────────────────────
    // The registration-time ABM_SETPOS (in the base's OnSourceInitialized) often fails to actually
    // shrink the OS work area on this app: at startup the UI thread is saturated (all bars register in
    // one synchronous loop, and live widgets like the CPU graph drive per-frame work), so the appbar
    // handshake doesn't settle, and the asynchronous taskbar-hide work-area change can wipe a fresh
    // reservation. RetroBar/Cairo re-assert via UpdatePosition after load; we go one step further and
    // re-assert until the work area has ACTUALLY shrunk by our height, then stop. Bounded + self-
    // terminating, so unlike the old blind timer it cannot cycle.
    private System.Windows.Threading.DispatcherTimer? _reserveTimer;
    private int _reserveAttempts;

    private void EnsureReserved()
    {
        _reserveTimer?.Stop();          // re-entrant safe (also called from Refresh after a layout change)
        _reserveTimer = null;
        _reserveAttempts = 0;
        if (IsReserved()) return;
        _reserveTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(300),
        };
        _reserveTimer.Tick += (_, _) =>
        {
            if (AllowClose || IsReserved() || ++_reserveAttempts > 12)
            {
                _reserveTimer?.Stop();
                _reserveTimer = null;
                return;
            }
            try { UpdatePosition(); } catch { }
        };
        _reserveTimer.Start();
    }

    /// <summary>True once the OS work area on our monitor has actually shrunk by ~our bar height.</summary>
    private bool IsReserved()
    {
        try
        {
            var sc = System.Windows.Forms.Screen.FromHandle(_hwnd == IntPtr.Zero ? Handle : _hwnd);
            int expected = (int)Math.Round(DesiredHeight * DpiScale) - 2;   // small slack for rounding
            int actual = AppBarEdge == AppBarEdge.Bottom
                ? sc.Bounds.Bottom - sc.WorkingArea.Bottom
                : sc.WorkingArea.Top - sc.Bounds.Top;
            return actual >= expected;
        }
        catch { return false; }
    }

    /// <summary>The leftmost Start Button control on this bar, if any (items are built left-to-right).</summary>
    public Controls.StartButtonControl? FindStartButton()
    {
        foreach (var child in ItemsHost.Children)
            if (child is Controls.StartButtonControl sb) return sb;
        return null;
    }

    /// <summary>This bar's placement (monitor / edge), used to rank Start Buttons across panels.</summary>
    public PanelConfig Panel => _panel;

    // ── Source init: register the external (OLE) drop target ─────────────────────
    // ManagedShell's base OnSourceInitialized creates the HWND and registers the
    // AppBar; we then hook our drag-drop on top of the now-valid handle.
    private PanelDropTarget? _nativeDropTarget;
    private bool _dropRegistered;

    protected override void OnSourceInitialized(object sender, EventArgs e)
    {
        // Let ManagedShell's AppBar registration run and settle FIRST, exactly as RetroBar does —
        // nothing of ours touches this handshake synchronously.
        base.OnSourceInitialized(sender, e);

        _hwnd = Handle;   // set by the base
        if (_hwnd == IntPtr.Zero) return;

        // (The base already hides us from Alt+Tab / the taskbar via WindowHelper.HideWindowFromTasks,
        //  so no manual WS_EX_TOOLWINDOW handling is needed here.)

        // Register our OLE drop target (for start-button drag-to-open) AFTER the AppBar is up, off the
        // source-init critical path, fully guarded and silent. A slow/failed OLE call here (seen over
        // RDP) must never block startup or disturb the AppBar reservation — previously a modal error
        // dialog raised on RegisterDragDrop failure could freeze the bar during init.
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(RegisterExternalDropTarget));
    }

    private void RegisterExternalDropTarget()
    {
        if (_hwnd == IntPtr.Zero || _dropRegistered) return;
        try
        {
            NativeDropTarget.OleInitialize(IntPtr.Zero);
            // UIPI bypass — allow drag-drop messages from lower-integrity Explorer.
            NativeDropTarget.AllowDragDropMessages(_hwnd);

            _nativeDropTarget = new PanelDropTarget
            {
                OnDragMove  = OnExternalDragMove,
                OnDragLeave = OnExternalDragLeave,
                OnDrop      = OnExternalDrop,
            };

            NativeDropTarget.RevokeDragDrop(_hwnd);
            _dropRegistered = NativeDropTarget.RegisterDragDrop(_hwnd, _nativeDropTarget) == 0;
        }
        catch
        {
            // Drag-to-open is a nice-to-have; never let its setup take the bar down.
            _dropRegistered = false;
        }
    }

    // Called by the base when the window is actually closing (AllowClose set).
    protected override void CustomClosing() => UnregisterNativeDropTarget();

    private void UnregisterNativeDropTarget()
    {
        if (_dropRegistered)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) NativeDropTarget.RevokeDragDrop(hwnd);
            _dropRegistered = false;
        }
        _nativeDropTarget = null;
    }

    // ── External drag → start-button hover-to-open ───────────────────────────────
    private StartButtonControl? _hoverStartBtn;
    private System.Windows.Threading.DispatcherTimer? _hoverTimer;

    private void OnExternalDragMove(NativeDropTarget.POINTL pt)
    {
        Point clientPt;
        try   { clientPt = PointFromScreen(new Point(pt.x, pt.y)); }
        catch { return; }

        var hit  = VisualTreeHelper.HitTest(this, clientPt);
        var node = hit?.VisualHit as DependencyObject;
        while (node != null && node is not StartButtonControl)
            node = VisualTreeHelper.GetParent(node);
        var startBtn = node as StartButtonControl;

        if (startBtn != _hoverStartBtn)
        {
            StopHoverTimer();
            _hoverStartBtn = startBtn;
            if (startBtn != null) StartHoverTimer();
        }
    }

    private void OnExternalDragLeave()
    {
        StopHoverTimer();
        _hoverStartBtn = null;
    }

    private void OnExternalDrop(NativeDropTarget.POINTL pt)
    {
        StopHoverTimer();
        _hoverStartBtn = null;
    }

    private void StartHoverTimer()
    {
        _hoverTimer ??= new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1500),
        };
        _hoverTimer.Tick -= HoverTick;
        _hoverTimer.Tick += HoverTick;
        _hoverTimer.Start();
    }

    private void StopHoverTimer()
    {
        if (_hoverTimer == null) return;
        _hoverTimer.Stop();
        _hoverTimer.Tick -= HoverTick;
    }

    private void HoverTick(object? sender, EventArgs e)
    {
        StopHoverTimer();
        _hoverStartBtn?.OpenMenuForDrag();
    }

    // ── Item content ─────────────────────────────────────────────────────────────
    private bool _growUsed;        // enforce "only one grow-to-fill per panel" across a (possibly staggered) build
    private int  _buildGeneration; // bumped per build so a stale staggered pump abandons itself

    /// <summary>
    /// Builds the bar's item controls. <paramref name="staggered"/> spreads construction across
    /// Background-priority dispatcher passes (used at startup so heavy items don't jam the appbar
    /// reservation handshake); the synchronous path is used for live edits via <see cref="Refresh"/>.
    /// </summary>
    private void BuildItemControls(bool staggered)
    {
        int gen = ++_buildGeneration;
        ItemsHost.Children.Clear();
        _growUsed = false;

        if (!staggered)
        {
            foreach (var item in _def.Items) AddItemControl(item);
            return;
        }

        var queue = new Queue<BarItem>(_def.Items);
        void Pump()
        {
            if (gen != _buildGeneration || queue.Count == 0) return;   // superseded by a newer build, or done
            AddItemControl(queue.Dequeue());
            if (queue.Count > 0)
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, (Action)Pump);
        }
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, (Action)Pump);
    }

    /// <summary>The bar definition this panel renders (so item controls can open their own config).</summary>
    public BarDefinition Definition => _def;

    // Associates a built item control with its model, so a right-click anywhere in the item's area can
    // find which item it is (for the "Configure <type>" command).
    public static readonly DependencyProperty ItemProperty = DependencyProperty.RegisterAttached(
        "Item", typeof(BarItem), typeof(PanelWindow));
    public static void SetItem(DependencyObject e, BarItem v) => e.SetValue(ItemProperty, v);
    public static BarItem? GetItem(DependencyObject e) => e.GetValue(ItemProperty) as BarItem;

    private void AddItemControl(BarItem item)
    {
        FrameworkElement? ctrl = item switch
        {
            LauncherItem    launcher => new LauncherControl(launcher),
            TaskButtonsItem tasks    => new TaskButtonsControl(tasks),
            SeparatorItem   sep      => new SeparatorControl(sep),
            StartButtonItem start    => new StartButtonControl(start),
            ClockItem       clock    => new ClockControl(clock),
            SystemMonitorItem sysmon => new SystemMonitorControl(sysmon),
            AudioControlItem  audio  => new AudioControlControl(audio),
            SystemTrayItem    tray   => new SystemTrayControl(tray),
            PowerItem         power  => new PowerControl(power),
            WeatherItem       weather => new WeatherControl(weather),
            _                       => null,
        };
        if (ctrl == null) return;

        bool isGrow = item is IGrowToFillItem { GrowToFill: true } && !_growUsed;
        if (isGrow) _growUsed = true;

        // The panel fills each item's allotted rect; controls stretch into it.
        ctrl.VerticalAlignment   = VerticalAlignment.Stretch;
        ctrl.HorizontalAlignment = HorizontalAlignment.Stretch;

        BarItemsPanel.SetGrow(ctrl, isGrow);
        BarItemsPanel.SetShrinkable(ctrl, item.Shrinkable);
        SetItem(ctrl, item);   // so a right-click in this area can offer "Configure <type>"
        ItemsHost.Children.Add(ctrl);
    }

    /// <summary>
    /// Applies edited config. Edge/monitor/height changes are pushed to the AppBar
    /// (which re-reserves and re-places); item-only changes just rebuild controls.
    /// </summary>
    public void Refresh()
    {
        bool edgeOrMonitorChanged =
            _panel.Position      != _registeredPosition ||
            _panel.MonitorNumber != _registeredMonitor;
        bool heightChanged = _def.HeightPx != _registeredHeight;

        if (edgeOrMonitorChanged)
        {
            UnregisterAppBar();
            Screen        = ResolveScreen(_panel);
            AppBarEdge    = ResolveEdge(_panel.Position);
            DesiredHeight = _def.HeightPx;
            RegisterAppBar();
            EnsureReserved();   // confirm the new edge/monitor actually reserved
        }
        else if (heightChanged)
        {
            DesiredHeight = _def.HeightPx;
            UpdatePosition();
            EnsureReserved();
        }

        _registeredPosition = _panel.Position;
        _registeredHeight   = _def.HeightPx;
        _registeredMonitor  = _panel.MonitorNumber;

        // A live edit is a discrete user action on one bar — build synchronously for an immediate result.
        BuildItemControls(staggered: false);
    }

    // ── Context menu / commands ──────────────────────────────────────────────────
    private MenuItem? _configureItem;   // the dynamically-injected "Configure <type>" entry, if any

    private void Grid_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var menu = (ContextMenu)FindResource("PanelMenu");

        // Drop any Configure entry from a previous right-click, then add one for the item under the
        // cursor (right-clicking the bar background, between items, shows just Settings / Exit).
        if (_configureItem != null) { menu.Items.Remove(_configureItem); _configureItem = null; }
        if (FindItem(e.OriginalSource as DependencyObject) is { } item)
        {
            _configureItem = BarItemMenu.MakeConfigureItem(this, _def, item);
            menu.Items.Insert(0, _configureItem);
        }

        menu.PlacementTarget = this;
        menu.IsOpen = true;
        e.Handled = true;
    }

    // Walks up the visual tree from the clicked element to the item control that carries the BarItem.
    private static BarItem? FindItem(DependencyObject? node)
    {
        for (; node != null; node = VisualTreeHelper.GetParent(node))
            if (GetItem(node) is { } item) return item;
        return null;
    }

    private void Configure_Click(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(SettingsWindow.ShowOrActivate);

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        PanelManager.ShutdownAll();   // unregister AppBars / free reserved space
        Application.Current.Shutdown();
    }
}
