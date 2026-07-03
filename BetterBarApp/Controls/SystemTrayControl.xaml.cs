using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BetterBarApp.Models;
using BetterBarApp.Services;
using ManagedShell.Common.Helpers;
using ManagedShell.Interop;
using ManagedShell.WindowsTray;

namespace BetterBarApp.Controls;

/// <summary>
/// Renders the Windows notification-area (system tray) icons, laid out exactly like the Launcher
/// (rows / spacing / margin). The clock never appears here; the sound icon is excluded by default.
///
/// This control is a pure CONSUMER: <see cref="TrayHostService"/> hosts the tray on its own thread
/// and publishes a frozen <see cref="TrayIconSnapshot"/> list (batched to ≤2/sec). We just re-flow the
/// grid whenever a new snapshot arrives, and forward mouse interaction back to the live icon via
/// <see cref="TrayHostService.Post"/> so the owning app's click/menu behaviour works as in Explorer.
/// </summary>
public partial class SystemTrayControl : UserControl
{
    private readonly SystemTrayItem _item;
    private double _lastBuiltHeight = -1;

    public SystemTrayControl(SystemTrayItem item)
    {
        _item = item;
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SizeChanged += OnSizeChanged;
        TrayHostService.Ensure();
        TrayHostService.SnapshotChanged += OnSnapshotChanged;
        Rebuild();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SizeChanged -= OnSizeChanged;
        TrayHostService.SnapshotChanged -= OnSnapshotChanged;
    }

    // Fires on the tray thread → marshal to the UI thread before touching WPF.
    private void OnSnapshotChanged() => Dispatcher.BeginInvoke(Rebuild);

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.HeightChanged) return;                            // we set Width ourselves
        if (Math.Abs(ActualHeight - _lastBuiltHeight) < 1.0) return;
        Rebuild();
    }

    private IEnumerable<TrayIconSnapshot> Icons()
    {
        foreach (var snap in TrayHostService.Snapshot)
        {
            if (_item.ExcludeSound      && snap.Guid == NotificationArea.VOLUME_GUID)     continue;
            if (_item.ExcludeMicrophone && snap.Guid == NotificationArea.MICROPHONE_GUID) continue;
            yield return snap;
        }
    }

    private void Rebuild()
    {
        if (ActualHeight <= 0) return;   // not laid out yet; OnSizeChanged fires when ready
        _lastBuiltHeight = ActualHeight;

        int    rows    = Math.Max(1, _item.Rows);
        double spacing = _item.IconSpacing;
        double panelH  = ActualHeight;
        const double minIcon = 8.0;

        // Same clamping as the Launcher: a cell (icon + spacing) is the highlightable container;
        // the margin is reduced if needed so every row keeps a usable icon size.
        double minCell   = minIcon + spacing;
        double maxMargin = Math.Max(0, (panelH - rows * minCell) / 2.0);
        double margin    = Math.Clamp(_item.IconMargin, 0, maxMargin);

        double availH   = panelH - 2 * margin;
        double cellSize = Math.Floor(availH / rows);
        double iconSize = Math.Max(minIcon, cellSize - spacing);

        OuterBorder.Padding = new Thickness(margin);

        var icons = Icons().ToList();
        IconPanel.Children.Clear();

        int n = icons.Count;
        if (n == 0)
        {
            Width = 2 * margin;
            IconPanel.Width = 0;
            IconPanel.Height = 0;
            InvalidatePanelMeasure();
            return;
        }

        int numCols   = (int)Math.Ceiling((double)n / rows);   // row-major fill
        double innerW = numCols * cellSize;
        double innerH = rows    * cellSize;

        Width            = 2 * margin + innerW;
        IconPanel.Width  = innerW;
        IconPanel.Height = innerH;

        for (int i = 0; i < n; i++)
        {
            var btn = BuildIconButton(icons[i], cellSize, iconSize);
            int row = i / numCols;
            int col = i % numCols;
            Canvas.SetLeft(btn, col * cellSize);
            Canvas.SetTop(btn,  row * cellSize);
            IconPanel.Children.Add(btn);
        }

        InvalidatePanelMeasure();
    }

    // Our Width changed in place; make the owning panel re-discover it (see BarItemsPanel.InvalidateForChild).
    private void InvalidatePanelMeasure() => BarItemsPanel.InvalidateForChild(this);

    private Button BuildIconButton(TrayIconSnapshot snap, double cellSize, double iconSize)
    {
        var img = new Image
        {
            Width = iconSize, Height = iconSize,
            Stretch = Stretch.Uniform, SnapsToDevicePixels = true,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Source = snap.Icon,
        };

        var btn = new Button { Width = cellSize, Height = cellSize, Content = img, ToolTip = snap.Title };
        if (TryFindResource("BarIconButton") is Style style) btn.Style = style;

        var ni = snap.Source;
        int DblTime() => System.Windows.Forms.SystemInformation.DoubleClickTime;

        // Capture cursor position / hit rect on the UI thread, then run the interaction on the tray
        // thread where the live NotifyIcon lives.
        btn.MouseEnter += (_, _) =>
        {
            var p = MouseHelper.GetCursorPositionParam();
            SetPlacement(btn, ni);
            TrayHostService.Post(() => ni.IconMouseEnter(p));
        };
        btn.MouseLeave += (_, _) =>
        {
            var p = MouseHelper.GetCursorPositionParam();
            TrayHostService.Post(() => ni.IconMouseLeave(p));
        };
        btn.MouseMove += (_, _) =>
        {
            var p = MouseHelper.GetCursorPositionParam();
            TrayHostService.Post(() => ni.IconMouseMove(p));
        };
        btn.PreviewMouseDown += (_, e) =>
        {
            var p = MouseHelper.GetCursorPositionParam();
            var button = e.ChangedButton;
            int dt = DblTime();
            SetPlacement(btn, ni);
            TrayHostService.Post(() => ni.IconMouseDown(button, p, dt));
        };
        btn.PreviewMouseUp += (_, e) =>
        {
            var p = MouseHelper.GetCursorPositionParam();
            var button = e.ChangedButton;
            int dt = DblTime();
            TrayHostService.Post(() => ni.IconMouseUp(button, p, dt));
            e.Handled = true;   // don't bubble to the panel's own right-click menu
        };

        return btn;
    }

    // Tell the icon's owner where the icon sits on screen (physical px) so its menu/flyout appears
    // next to it. The rect is computed on the UI thread; the assignment is marshalled to the tray thread.
    private static void SetPlacement(FrameworkElement el, NotifyIcon ni)
    {
        try
        {
            var tl = el.PointToScreen(new Point(0, 0));
            var br = el.PointToScreen(new Point(el.ActualWidth, el.ActualHeight));
            var rect = new NativeMethods.Rect
            {
                Left = (int)tl.X, Top = (int)tl.Y, Right = (int)br.X, Bottom = (int)br.Y,
            };
            TrayHostService.Post(() => ni.Placement = rect);
        }
        catch { }
    }
}
