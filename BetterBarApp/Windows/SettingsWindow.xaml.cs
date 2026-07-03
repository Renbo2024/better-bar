using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BetterBarApp.Models;
using BetterBarApp.Pages;
using BetterBarApp.Services;

namespace BetterBarApp.Windows;

/// <summary>
/// The single, navigation-based settings app (Windows 11 Settings style), built on
/// WPF-UI. Replaces the old ConfigWindow + dialog stack. WPF-UI is scoped to this
/// window so the taskbar's own styling is unaffected.
/// </summary>
public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
{
    private static SettingsWindow? _instance;

    private readonly ScreenIdentifyOverlay _screenOverlay = new();

    public static void ShowOrActivate()
    {
        if (_instance == null || !_instance.IsLoaded)
        {
            _instance = new SettingsWindow();
            _instance.Show();
            _instance.Activate();
        }
        else
        {
            if (_instance.WindowState == WindowState.Minimized)
                _instance.WindowState = WindowState.Normal;
            _instance.Activate();
        }
    }

    // Item to open once the window is up; set by ShowItem when the window is being created fresh, and
    // consumed in OnLoaded (so we navigate there instead of the default BarsPage — no flash).
    private static (BarDefinition Def, BarItem Item)? _pendingItemNav;

    /// <summary>Opens Settings straight to a specific item's page within its bar definition.</summary>
    public static void ShowItem(BarDefinition def, BarItem item)
    {
        bool fresh = _instance == null || !_instance.IsLoaded;
        if (fresh) _pendingItemNav = (def, item);
        ShowOrActivate();
        if (!fresh)
            Navigate(ItemPages.PageTypeFor(item), new ItemEditContext(def, item));
    }

    private SettingsWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        // Let PageUp/PageDown scroll the current page even without a scroll wheel. Window-level preview
        // so it works regardless of which control has focus; PageUp/Down aren't used by the single-line
        // inputs here, so hijacking them is safe (Home/End are left alone for text editing).
        // Use AddHandler with handledEventsToo so we still see the key even if something marked it handled.
        AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnPreviewKeyDown), handledEventsToo: true);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.PageUp or Key.PageDown)) return;

        // The page's own ScrollViewer is usually NOT the one that scrolls — WPF-UI's NavigationView
        // hosts page content in its own scroll viewer, so the page is given unbounded height and its
        // inner viewer never scrolls (ScrollableHeight == 0). So find the nearest ANCESTOR scroll
        // viewer that actually has room to scroll, starting from the focused element; fall back to the
        // most-scrollable visible viewer in the window.
        var sv = NearestScrollableAncestor(Keyboard.FocusedElement as DependencyObject)
                 ?? MostScrollable(this);
        if (sv == null) return;

        if (e.Key == Key.PageDown) sv.PageDown();
        else                        sv.PageUp();
        e.Handled = true;
    }

    private static ScrollViewer? NearestScrollableAncestor(DependencyObject? node)
    {
        for (; node != null; node = VisualTreeHelper.GetParent(node))
            if (node is ScrollViewer { IsVisible: true } sv && sv.ScrollableHeight > 0)
                return sv;
        return null;
    }

    private static ScrollViewer? MostScrollable(DependencyObject root)
    {
        ScrollViewer? best = null;
        void Scan(DependencyObject n)
        {
            if (n is ScrollViewer { IsVisible: true } sv && sv.ScrollableHeight > 0 &&
                (best == null || sv.ScrollableHeight > best.ScrollableHeight))
                best = sv;
            int c = VisualTreeHelper.GetChildrenCount(n);
            for (int i = 0; i < c; i++) Scan(VisualTreeHelper.GetChild(n, i));
        }
        Scan(root);
        return best;
    }

    // ── Navigation façade ────────────────────────────────────────────────────────
    // Pages navigate via these helpers. A small "pending context" carries a parameter
    // (e.g. the bar being edited) to the target page, which reads it in its ctor — this
    // avoids any dependency on the journal/back-stack, so navigation is fully explicit.
    private static object? _pendingContext;

    public static void Navigate(Type pageType, object? context = null)
    {
        _pendingContext = context;
        _instance?.Nav.Navigate(pageType);
    }

    /// <summary>Consumes the navigation parameter passed to the page being created.</summary>
    public static object? TakeContext()
    {
        var c = _pendingContext;
        _pendingContext = null;
        return c;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Re-number monitors in case the layout changed, then show the identify overlay
        // so the "Screen N" selectors in the bar editor are easy to match to a display.
        ScreenService.Detect();
        _screenOverlay.Show(this);

        // A "Configure <item>" request jumps straight to that item's page; otherwise the default list.
        if (_pendingItemNav is { } p)
        {
            _pendingItemNav = null;
            Navigate(ItemPages.PageTypeFor(p.Item), new ItemEditContext(p.Def, p.Item));
        }
        else
        {
            Nav.Navigate(typeof(BarsPage));
        }
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        _screenOverlay.Hide();
        _instance = null;
    }
}
