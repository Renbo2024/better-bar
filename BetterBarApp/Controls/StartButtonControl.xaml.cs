using System.Windows;
using System.Windows.Controls;
using BetterBarApp.Models;
using BetterBarApp.Windows;

namespace BetterBarApp.Controls;

public partial class StartButtonControl : UserControl
{
    private StartMenuWindow? _openMenu;

    private readonly StartButtonItem _item;
    private double _lastBuiltHeight = -1;

    public StartButtonControl(StartButtonItem item)
    {
        _item = item;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SizeChanged += OnSizeChanged;
        Rebuild();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.HeightChanged) return;
        if (Math.Abs(ActualHeight - _lastBuiltHeight) < 1.0) return;
        Rebuild();
    }

    private void Rebuild()
    {
        if (ActualHeight <= 0) return;
        _lastBuiltHeight = ActualHeight;

        // Clamp the margin so the button is at least minSize px.
        const double minSize = 12;
        double panelH = ActualHeight;
        double maxMargin = Math.Max(0, (panelH - minSize) / 2.0);
        double margin    = Math.Clamp(_item.Margin, 0, maxMargin);

        double buttonSize = panelH - 2 * margin;

        // Outer width: same margin on left and right plus the square button.
        Width = 2 * margin + buttonSize;

        // Button sits centered in the control with the user's margin on all sides.
        // The Path fills the button (no extra inset) so margin alone controls spacing.
        StartBtn.Width  = buttonSize;
        StartBtn.Height = buttonSize;
        StartBtn.Margin = new Thickness(margin);
        StartIcon.Margin = new Thickness(0);
    }

    private void StartBtn_Click(object sender, RoutedEventArgs e) => OpenOrToggleMenu();

    /// <summary>Opens or closes the menu, as if the button were clicked (used by the Windows-key hook).</summary>
    public void ToggleMenu() => OpenOrToggleMenu();

    private void OpenOrToggleMenu()
    {
        // Click again while already open → close, like the real Start button toggle.
        if (_openMenu != null && _openMenu.IsLoaded)
        {
            _openMenu.Close();
            _openMenu = null;
            return;
        }

        var host = Window.GetWindow(this);
        if (host == null) return;

        var menu = new StartMenuWindow(_item, host);
        _openMenu = menu;
        menu.Closed += (_, _) => { if (_openMenu == menu) _openMenu = null; };

        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (dpi <= 0) dpi = 1.0;

        // Horizontal anchor: the LEFT of the whole start-button item (this control, margin included),
        // not the inset icon — so a start button that's first in the bar puts the menu against the
        // monitor's left edge.
        double itemLeftPhys = PointToScreen(new Point(0, 0)).X;

        // Vertical anchor: the BAR's own edges (the host AppBar window), not the icon — so the menu
        // sits flush against the panel. Bottom bar → the menu's bottom meets the bar's top edge;
        // top bar → the menu's top meets the bar's bottom edge.
        var barTopLeft       = host.PointToScreen(new Point(0, 0));   // physical px
        double barTopPhys    = barTopLeft.Y;
        double barBottomPhys = barTopLeft.Y + host.ActualHeight * dpi;

        var screen = System.Windows.Forms.Screen.FromPoint(
            new System.Drawing.Point((int)barTopLeft.X, (int)barTopPhys));
        bool openDown = barTopPhys < screen.Bounds.Top + screen.Bounds.Height / 2;   // top bar → open down

        // Edges and the monitor's usable bounds, in logical (WPF) units.
        double barTop     = barTopPhys    / dpi;
        double barBottom  = barBottomPhys / dpi;
        double workTop    = screen.WorkingArea.Top    / dpi;
        double workBottom = screen.WorkingArea.Bottom / dpi;

        // Cap the menu at the space available in the open direction (it grows to fit via
        // SizeToContent, then scrolls internally).
        menu.MaxHeight = Math.Max(0, openDown ? workBottom - barBottom : barTop - workTop);
        // Floor the height so search results have room even when the icon list is empty.
        menu.MinHeight = Math.Min(_item.MinMenuHeight, menu.MaxHeight);

        menu.Left = itemLeftPhys / dpi;
        menu.Top  = openDown ? barBottom : barTop;   // down: hang below the bar / up: provisional
        if (openDown) menu.SetSearchAtTop();   // top panel: search at top, grow downward
        menu.Show();   // SizeToContent settles ActualHeight during Show()

        // Opening upward (bottom bar): pin the menu's BOTTOM to the bar's top edge so it grows upward
        // as results change without the search box bobbing.
        if (!openDown)
            menu.SetBottomAnchor(barTop);

        menu.Activate();
    }

    /// <summary>
    /// Opens the start menu for a drag-in-progress and pins it so it survives
    /// the source app keeping foreground focus. Called by PanelWindow when an
    /// external file drag has dwelled over this control long enough.
    /// </summary>
    public void OpenMenuForDrag()
    {
        if (_openMenu != null && _openMenu.IsLoaded) return;
        OpenOrToggleMenu();
        if (_openMenu != null)
            _openMenu.Pinned = true;
    }
}
