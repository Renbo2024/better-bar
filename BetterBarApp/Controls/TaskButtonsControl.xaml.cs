using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using BetterBarApp.Models;
using BetterBarApp.Services;
using BetterBarApp.Windows;
using ManagedShell.WindowsTasks;

namespace BetterBarApp.Controls;

/// <summary>
/// Renders one persistent button per taskbar window. Buttons are created/removed only as windows
/// come and go; focus / title / icon changes update the existing button in place (no teardown), so
/// clicks are never swallowed mid-rebuild and focus changes are reflected instantly — matching
/// RetroBar's responsiveness.
/// </summary>
public partial class TaskButtonsControl : UserControl
{
    private sealed class WindowButton
    {
        public required ApplicationWindow Window;
        public required Button    Button;
        public required Image     Icon;
        public required TextBlock Label;
        public required Rectangle Accent;
        public ApplicationWindow.WindowState PressedState = ApplicationWindow.WindowState.Unknown;
        public double X, Y, W, H;   // current layout rect, so the accent can be re-placed cheaply
    }

    private readonly TaskButtonsItem _item;
    private readonly Dictionary<ApplicationWindow, WindowButton> _buttons = [];
    private List<ApplicationWindow> _order = [];
    private Brush _accentBrush = Brushes.DodgerBlue;             // focused/selected accent bar
    private Brush _unselectedAccentBrush = Brushes.DodgerBlue;   // running (unfocused) accent bar

    private double HGap => Math.Max(0, _item.HorizontalSpacing);   // configurable gap between columns
    private const double VGap = 3;                                 // fixed gap between rows
    private const double Pad = 2;

    private double _lastWidth  = -1;
    private double _lastHeight = -1;
    private bool   _syncQueued;
    private bool   _layoutQueued;

    // Windows we've hooked for monitor moves — ALL windows, not just displayed ones, so we notice a
    // window arriving on this bar's monitor (its button isn't here yet to be hooked otherwise).
    private readonly HashSet<ApplicationWindow> _monitorHooked = [];

    public TaskButtonsControl(TaskButtonsItem item)
    {
        _item = item;
        InitializeComponent();
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TaskbarService.EnsureInitialized();
        if (TaskbarService.Windows is INotifyCollectionChanged incc)
            incc.CollectionChanged += OnWindowsChanged;
        SizeChanged += OnSizeChanged;
        SyncMonitorHooks();
        Sync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (TaskbarService.Windows is INotifyCollectionChanged incc)
            incc.CollectionChanged -= OnWindowsChanged;
        SizeChanged -= OnSizeChanged;
        foreach (var w in _monitorHooked) w.PropertyChanged -= OnWindowMonitorChanged;
        _monitorHooked.Clear();
        foreach (var w in _buttons.Keys) w.PropertyChanged -= OnWindowPropertyChanged;
        _buttons.Clear();
        _order = [];
        ButtonCanvas.Children.Clear();
    }

    private void OnWindowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncMonitorHooks();
        QueueSync();
    }

    // Watch every window for monitor / taskbar-visibility changes (add new, drop gone). We hook ALL
    // windows, not just displayed ones, so a window arriving on this bar's monitor is noticed.
    private void SyncMonitorHooks()
    {
        var current = TaskbarService.Windows?.Cast<ApplicationWindow>().ToHashSet() ?? [];
        foreach (var w in _monitorHooked.Where(w => !current.Contains(w)).ToList())
        {
            w.PropertyChanged -= OnWindowMonitorChanged;
            _monitorHooked.Remove(w);
        }
        foreach (var w in current)
            if (_monitorHooked.Add(w)) w.PropertyChanged += OnWindowMonitorChanged;
    }

    // A window changed monitor (or taskbar visibility) → re-evaluate which windows belong on this bar.
    private void OnWindowMonitorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ApplicationWindow.HMonitor) or nameof(ApplicationWindow.ShowInTaskbar))
            QueueSync();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if ((e.HeightChanged && Math.Abs(ActualHeight - _lastHeight) >= 1.0) ||
            (e.WidthChanged  && Math.Abs(ActualWidth  - _lastWidth)  >= 1.0))
            Layout();
    }

    // ── Window source / ordering ─────────────────────────────────────────────────

    // Visible windows in display order: same-app instances clustered together (stable, first-seen),
    // then priority-list matches pulled to the front (earlier entry = higher priority).
    private List<ApplicationWindow> ComputeOrderedWindows()
    {
        if (TaskbarService.Windows == null) return [];

        IntPtr panelMonitor = _item.ShowAllMonitors ? IntPtr.Zero : GetHostMonitor();

        var visible = TaskbarService.Windows
            .Cast<ApplicationWindow>()
            .Where(w => w.ShowInTaskbar)
            .Where(w => _item.ShowAllMonitors || w.HMonitor == panelMonitor)
            .ToList();

        var clusters = new List<List<ApplicationWindow>>();
        var byKey = new Dictionary<string, List<ApplicationWindow>>();
        foreach (var w in visible)
        {
            string key = w.Category ?? w.Title ?? string.Empty;
            if (!byKey.TryGetValue(key, out var cluster))
            {
                cluster = []; byKey[key] = cluster; clusters.Add(cluster);
            }
            cluster.Add(w);
        }

        var priority = ParsePriorityList();
        if (priority.Count > 0)
            clusters = [.. clusters.OrderBy(c => PriorityIndex(c, priority))];   // OrderBy is stable

        return [.. clusters.SelectMany(c => c)];
    }

    private List<string> ParsePriorityList() =>
        [.. _item.PriorityOrder.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static int PriorityIndex(List<ApplicationWindow> cluster, List<string> priority)
    {
        for (int i = 0; i < priority.Count; i++)
        {
            string term = priority[i];
            if (cluster.Any(w =>
                    (w.Title?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (w.Category?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)))
                return i;
        }
        return int.MaxValue;
    }

    // Coalesce structural bursts (windows opening/closing, reorders) into one Sync per frame.
    private void QueueSync()
    {
        if (_syncQueued) return;
        _syncQueued = true;
        Dispatcher.BeginInvoke(() => { _syncQueued = false; Sync(); });
    }

    // Re-run Layout after the current measure/arrange pass completes, so it uses the settled width.
    // Loaded priority is below Render (the layout pass), so the panel has finished re-arranging us.
    private void QueueLayout()
    {
        if (_layoutQueued) return;
        _layoutQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { _layoutQueued = false; Layout(); });
    }

    // ── Structural sync: add/remove/reorder buttons without recreating survivors ──
    private void Sync()
    {
        var desired = ComputeOrderedWindows();

        bool changed = false;
        foreach (var w in _buttons.Keys.Where(w => !desired.Contains(w)).ToList())
        {
            RemoveButton(w);
            changed = true;
        }
        foreach (var w in desired)
            if (!_buttons.ContainsKey(w)) { AddButton(w); changed = true; }

        _order = desired;
        // Button count → natural width changed. Invalidate the PANEL (not just ourselves): re-measuring
        // ourselves hits BarItemsPanel's cached finite constraint and reports the stale width, so the
        // bar never reflows until a settings change recreates us. See BarItemsPanel.InvalidateForChild.
        if (changed) BarItemsPanel.InvalidateForChild(this);
        Layout();
        // ...and re-lay out once more AFTER the panel re-measures/arranges (the Render-priority layout
        // pass), so positioning uses the settled width rather than the stale one we may have now.
        QueueLayout();
    }

    private void AddButton(ApplicationWindow w)
    {
        var icon = new Image
        {
            Width = 16, Height = 16,
            Source = w.Icon,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };

        var label = new TextBlock
        {
            Text = w.Title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Explicit (the global implicit TextBlock style forces a near-black Foreground); a resource
        // *reference* keeps it tracking live theme swaps.
        label.SetResourceReference(TextBlock.ForegroundProperty, "TaskBtnFg");

        var content = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(icon, Dock.Left);
        content.Children.Add(icon);
        content.Children.Add(label);

        var btn = new Button
        {
            Style   = (Style)FindResource("TaskButton"),
            Content = content,
            Padding = new Thickness(_item.TextMargin),   // empty space around the icon+label (all sides)
            Visibility = Visibility.Collapsed,           // hidden until Layout gives it a slot + width
        };
        TaskButtonState.SetIsActive(btn, w.State == ApplicationWindow.WindowState.Active);

        // Optional cursor-trailing hover tip; the provider reads the live title so renames show through.
        HoverTip.Attach(btn, () => w.Title, _item.ShowTooltips, _item.TooltipDelayMs);

        var accent = new Rectangle { IsHitTestVisible = false, Visibility = Visibility.Collapsed };

        var wb = new WindowButton { Window = w, Button = btn, Icon = icon, Label = label, Accent = accent };

        // RetroBar's activation logic exactly: capture the window's state on mouse-DOWN (before the
        // asynchronous shell-hook foreground update, so it's still accurate), then on Click minimize
        // if it was active (and can be), else bring it to front (restoring a minimized window).
        btn.PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                wb.PressedState = w.State;
        };
        btn.Click += (_, _) =>
        {
            if (wb.PressedState == ApplicationWindow.WindowState.Active && w.CanMinimize)
                w.Minimize();
            else
                w.BringToFront();
        };
        // WindowStyle=None windows don't get WM_CONTEXTMENU, so handle right-click explicitly.
        btn.PreviewMouseRightButtonUp += (_, e) => { ShowButtonMenu(w, btn); e.Handled = true; };

        w.PropertyChanged += OnWindowPropertyChanged;

        ButtonCanvas.Children.Add(btn);
        ButtonCanvas.Children.Add(accent);
        _buttons[w] = wb;
    }

    private void RemoveButton(ApplicationWindow w)
    {
        if (!_buttons.TryGetValue(w, out var wb)) return;
        w.PropertyChanged -= OnWindowPropertyChanged;
        ButtonCanvas.Children.Remove(wb.Button);
        ButtonCanvas.Children.Remove(wb.Accent);
        _buttons.Remove(w);
    }

    // ── Layout (positions + sizes; no button creation) ────────────────────────────

    // Width we'd like at full size (all columns at MaxButtonWidth); the panel may grant us less.
    private double ComputeNaturalWidth()
    {
        int n = _order.Count;
        if (n == 0) return 0;
        int rows    = Math.Max(1, _item.Rows);
        int numCols = (int)Math.Ceiling((double)n / rows);
        double maxBtnW = Math.Max(8, _item.MaxButtonWidth);
        return numCols * maxBtnW + (numCols - 1) * HGap + 2 * Pad;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        base.MeasureOverride(constraint);
        double h = double.IsInfinity(constraint.Height) ? 0 : constraint.Height;
        double w = double.IsInfinity(constraint.Width) ? ComputeNaturalWidth() : constraint.Width;
        return new Size(w, h);
    }

    private void Layout()
    {
        // Need a real arranged size. Laying out at zero/unknown width would fall back to full
        // MaxButtonWidth and overflow buttons into each other; OnSizeChanged / QueueLayout re-runs
        // us once the panel has settled.
        if (ActualHeight <= 0 || ActualWidth <= 0) return;
        _lastWidth  = ActualWidth;
        _lastHeight = ActualHeight;

        int n = _order.Count;
        if (n == 0) return;

        int rows = Math.Max(1, _item.Rows);
        double availH    = ActualHeight - 2 * Pad;
        double rowHeight = Math.Max(8, Math.Floor((availH - (rows - 1) * VGap) / rows));
        int    numCols   = (int)Math.Ceiling((double)n / rows);
        double maxBtnW   = Math.Max(8, _item.MaxButtonWidth);
        double gaps      = (numCols - 1) * HGap;

        // MaxButtonWidth is a ceiling; if the granted width is smaller, shrink uniformly (no minimum).
        double availableW = ActualWidth;
        double naturalW   = numCols * maxBtnW + gaps + 2 * Pad;
        double btnW = (availableW <= 0 || naturalW <= availableW)
            ? maxBtnW
            : Math.Max(1, Math.Floor((availableW - 2 * Pad - gaps) / numCols));

        _accentBrush           = AccentBrush();
        _unselectedAccentBrush = UnselectedAccentBrush();

        // ── Reconcile the canvas so a stale/orphaned button can never render at full width over a
        //    neighbour (the cause of overlapping labels on first load). ──
        // 1. Drop any orphaned canvas children (their window is no longer tracked).
        var live = new HashSet<UIElement>();
        foreach (var b in _buttons.Values) { live.Add(b.Button); live.Add(b.Accent); }
        for (int j = ButtonCanvas.Children.Count - 1; j >= 0; j--)
            if (!live.Contains(ButtonCanvas.Children[j])) ButtonCanvas.Children.RemoveAt(j);
        // 2. Hide any tracked button that isn't in the current order (so it gets no slot and can't show
        //    un-sized). Anything in the order is shown + sized + positioned below.
        var shown = new HashSet<ApplicationWindow>(_order);
        foreach (var (win, b) in _buttons)
            if (!shown.Contains(win)) { b.Button.Visibility = Visibility.Collapsed; b.Accent.Visibility = Visibility.Collapsed; }

        for (int i = 0; i < n; i++)
        {
            if (!_buttons.TryGetValue(_order[i], out var wb)) continue;   // never throw on a transient desync
            int row = i / numCols, col = i % numCols;
            wb.X = Pad + col * (btnW + HGap);
            wb.Y = Pad + row * (rowHeight + VGap);
            wb.W = btnW;
            wb.H = rowHeight;

            wb.Button.Visibility = Visibility.Visible;
            wb.Button.Width  = btnW;
            wb.Button.Height = rowHeight;
            Canvas.SetLeft(wb.Button, wb.X);
            Canvas.SetTop(wb.Button, wb.Y);

            ApplyAccent(wb);
        }
    }

    // Windows 11-style accent pill along the button's bottom: width a configurable % of the button,
    // wider when focused, narrower when just running. Re-placed cheaply from the stored rect.
    private void ApplyAccent(WindowButton wb)
    {
        double accentH = _item.AccentThickness;   // 0 = no bar
        bool active = wb.Window.State == ApplicationWindow.WindowState.Active;
        double frac = (active ? _item.SelectedPillPercent : _item.UnselectedPillPercent);
        frac = Math.Clamp(frac, 0, 100) / 100.0;

        if (accentH < 1 || frac <= 0 || wb.W <= 0)
        {
            wb.Accent.Visibility = Visibility.Collapsed;
            return;
        }

        double barW = Math.Max(1, wb.W * frac);
        wb.Accent.Visibility = Visibility.Visible;
        wb.Accent.Width   = barW;
        wb.Accent.Height  = accentH;
        wb.Accent.RadiusX = accentH / 2;
        wb.Accent.RadiusY = accentH / 2;
        wb.Accent.Fill    = active ? _accentBrush : _unselectedAccentBrush;
        Canvas.SetLeft(wb.Accent, wb.X + (wb.W - barW) / 2);
        Canvas.SetTop(wb.Accent, wb.Y + wb.H - accentH);
    }

    private Brush AccentBrush()
    {
        if (!string.IsNullOrWhiteSpace(_item.AccentColor))
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(_item.AccentColor)!); }
            catch { /* fall through to theme accent */ }
        }
        return TryFindResource("Accent") as Brush ?? Brushes.DodgerBlue;
    }

    // Accent-bar colour for running (unfocused) buttons; "" falls back to the theme's default grey
    // (TaskBtnUnselectedAccent — a grey that suits the active theme's panel).
    private Brush UnselectedAccentBrush()
    {
        if (!string.IsNullOrWhiteSpace(_item.UnselectedAccentColor))
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(_item.UnselectedAccentColor)!); }
            catch { /* fall through to the theme grey */ }
        }
        return TryFindResource("TaskBtnUnselectedAccent") as Brush
               ?? new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x6E));
    }

    // ── Live per-window updates (no rebuild) ───────────────────────────────────────
    private void OnWindowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // ManagedShell raises some of these (e.g. Icon) on a background thread — marshal to the UI
        // thread before touching any WPF object. Same-thread events stay synchronous (instant).
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnWindowPropertyChanged(sender, e));
            return;
        }

        if (sender is not ApplicationWindow w || !_buttons.TryGetValue(w, out var wb)) return;

        switch (e.PropertyName)
        {
            case nameof(ApplicationWindow.State):
                TaskButtonState.SetIsActive(wb.Button, w.State == ApplicationWindow.WindowState.Active);
                ApplyAccent(wb);
                break;
            case nameof(ApplicationWindow.Icon):
                wb.Icon.Source = w.Icon;
                break;
            case nameof(ApplicationWindow.Title):
                wb.Label.Text = w.Title;   // the hover tip reads w.Title live, so no separate update needed
                if (!string.IsNullOrWhiteSpace(_item.PriorityOrder)) QueueSync();   // title may change order
                break;
            case nameof(ApplicationWindow.ShowInTaskbar):
                QueueSync();
                break;
        }
    }

    // ── Right-click menu — styled like the Windows window menu (RetroBar-style), with BetterBar's
    //    own items at the TOP above a divider, then the standard Marlett window-action commands. ──
    private void ShowButtonMenu(ApplicationWindow w, Button btn)
    {
        var menu = BarItemMenu.Create(this, btn);

        if (Window.GetWindow(this) is PanelWindow pw)
            menu.Items.Add(BarItemMenu.MakeConfigureItem(this, pw.Definition, _item));
        BarItemMenu.AddBetterBarCommands(this, menu);   // BetterBar's items at the TOP
        menu.Items.Add(MakeSeparator());
        AddSystemCommands(menu, w);

        menu.IsOpen = true;
    }

    // Replicates the window's system menu, driving each command via WM_SYSCOMMAND. Marlett glyphs
    // match the classic window controls: Restore '2', Minimize '0', Maximize '1', Close 'r'.
    private void AddSystemCommands(ContextMenu menu, ApplicationWindow w)
    {
        var h     = w.Handle;
        bool min  = IsIconic(h);
        bool max  = IsZoomed(h);
        int  style = GetWindowLong(h, GWL_STYLE);
        bool canMax  = (style & WS_MAXIMIZEBOX) != 0;
        bool canMin  = (style & WS_MINIMIZEBOX) != 0;
        bool canSize = (style & WS_THICKFRAME)  != 0;

        menu.Items.Add(SysItem("Restore",  SC_RESTORE,  h, min || max,             "2"));
        menu.Items.Add(SysItem("Move",     SC_MOVE,     h, !min && !max,           null));
        menu.Items.Add(SysItem("Size",     SC_SIZE,     h, canSize && !min && !max, null));
        menu.Items.Add(SysItem("Minimize", SC_MINIMIZE, h, canMin && !min,         "0"));
        menu.Items.Add(SysItem("Maximize", SC_MAXIMIZE, h, canMax && !max,         "1"));
        menu.Items.Add(MakeSeparator());
        menu.Items.Add(SysItem("Close",    SC_CLOSE,    h, true,                   "r"));
    }

    private MenuItem SysItem(string header, int sysCommand, IntPtr hwnd, bool enabled, string? marlett)
    {
        var mi = MakeItem(header, marlett, enabled);
        mi.Click += (_, _) => PostMessage(hwnd, WM_SYSCOMMAND, (IntPtr)sysCommand, IntPtr.Zero);
        return mi;
    }

    private MenuItem MakeItem(string header, string? marlettGlyph, bool enabled)
    {
        var mi = new MenuItem { Header = header, IsEnabled = enabled };
        if (TryFindResource("SystemMenuItem") is Style s) mi.Style = s;
        if (marlettGlyph != null)
        {
            var glyph = new TextBlock
            {
                Text = marlettGlyph,
                FontFamily = new FontFamily("Marlett"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            // Icon presenter doesn't inherit the menu foreground reliably — set it explicitly so the
            // glyph is light on the dark menu surface (tracks the theme).
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "TaskBtnFg");
            mi.Icon = glyph;
        }
        return mi;
    }

    private Separator MakeSeparator()
    {
        var sep = new Separator();
        if (TryFindResource("SystemMenuSeparator") is Style s) sep.Style = s;
        return sep;
    }


    // ── Win32 ──────────────────────────────────────────────────────────────────────
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int  GetWindowLong(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint MONITOR_DEFAULTTONEAREST = 0x2;
    private const int  GWL_STYLE      = -16;
    private const int  WS_MAXIMIZEBOX = 0x10000;
    private const int  WS_MINIMIZEBOX = 0x20000;
    private const int  WS_THICKFRAME  = 0x40000;
    private const uint WM_SYSCOMMAND  = 0x0112;
    private const int  SC_SIZE     = 0xF000;
    private const int  SC_MOVE     = 0xF010;
    private const int  SC_MINIMIZE = 0xF020;
    private const int  SC_MAXIMIZE = 0xF030;
    private const int  SC_CLOSE    = 0xF060;
    private const int  SC_RESTORE  = 0xF120;

    private IntPtr GetHostMonitor()
    {
        var host = Window.GetWindow(this);
        if (host == null) return IntPtr.Zero;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(host).Handle;
        return hwnd == IntPtr.Zero ? IntPtr.Zero : MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
    }
}
