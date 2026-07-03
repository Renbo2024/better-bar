using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace BetterBarApp.Controls;

/// <summary>
/// Optional hover tooltip with a configurable delay that trails the cursor — so it never sits
/// statically over a neighbouring icon the user might want to click. One shared, themed popup is
/// reused across all attached elements. The tip rides just outside the bar (below a top-docked bar,
/// above a bottom-docked one) and tracks the cursor horizontally; it hides the moment the pointer
/// leaves the element.
/// </summary>
public static class HoverTip
{
    private static Popup?     _popup;
    private static Border?    _border;
    private static TextBlock? _text;
    private static readonly DispatcherTimer _timer = new();

    private static FrameworkElement? _target;
    private static Func<string?>?    _textFn;
    private static double _vOffset;

    /// <summary>Wire hover-tip behaviour onto <paramref name="el"/>. No-op when <paramref name="enabled"/> is false.</summary>
    public static void Attach(FrameworkElement el, Func<string?> textProvider, bool enabled, int delayMs)
    {
        if (!enabled) return;
        el.MouseEnter += (_, _) => Schedule(el, textProvider, delayMs);
        el.MouseMove  += (_, _) => { if (ReferenceEquals(_target, el)) Track(); };
        el.MouseLeave += (_, _) => { if (ReferenceEquals(_target, el)) Hide(); };
    }

    private static void Schedule(FrameworkElement el, Func<string?> fn, int delayMs)
    {
        _target = el;
        _textFn = fn;
        _timer.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(0, delayMs));
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        Show();
    }

    private static void Show()
    {
        if (_target is not { IsVisible: true }) return;
        string? txt = _textFn?.Invoke();
        if (string.IsNullOrEmpty(txt)) return;

        EnsurePopup();
        _text!.Text = txt;
        _popup!.PlacementTarget = _target;

        // Keep the tip off the icon row: below a top-docked bar, above a bottom-docked one.
        bool placeBelow = InTopHalfOfScreen(_target);
        _border!.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tipH = _border.DesiredSize.Height;
        _vOffset = placeBelow ? _target.ActualHeight + 4 : -(tipH + 4);

        Track();                  // initial X at the cursor
        _popup.IsOpen = true;
    }

    private static void Track()
    {
        if (_popup == null || _target == null) return;
        _popup.HorizontalOffset = Mouse.GetPosition(_target).X + 12;
        _popup.VerticalOffset   = _vOffset;
    }

    /// <summary>Immediately hide the shared tip (e.g. when opening a flyout over the bar).</summary>
    public static void Dismiss() => Hide();

    private static void Hide()
    {
        _timer.Stop();
        if (_popup != null) _popup.IsOpen = false;
        _target = null;
        _textFn = null;
    }

    private static bool InTopHalfOfScreen(FrameworkElement el)
    {
        try
        {
            var tl = el.PointToScreen(new Point(0, 0));
            var sc = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)tl.X, (int)tl.Y));
            return tl.Y < sc.Bounds.Top + sc.Bounds.Height / 2.0;
        }
        catch { return true; }
    }

    private static void EnsurePopup()
    {
        if (_popup != null) return;

        _text = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 480 };
        _text.SetResourceReference(TextBlock.ForegroundProperty, "TaskBtnFg");

        _border = new Border
        {
            Child            = _text,
            Padding          = new Thickness(7, 4, 7, 4),
            CornerRadius     = new CornerRadius(4),
            BorderThickness  = new Thickness(1),
            IsHitTestVisible = false,
        };
        _border.SetResourceReference(Border.BackgroundProperty, "StartMenuBg");
        _border.SetResourceReference(Border.BorderBrushProperty, "StartMenuBorder");

        _popup = new Popup
        {
            Child              = _border,
            AllowsTransparency = true,
            Placement          = PlacementMode.Relative,
            PopupAnimation     = PopupAnimation.Fade,
            Focusable          = false,
            IsHitTestVisible   = false,
        };
    }
}
