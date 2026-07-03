using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BetterBarApp.Windows;

/// <summary>
/// Our own calendar popup, shown when the bar clock is clicked (replacing the native Windows
/// date/time flyout). Renders a month grid themed off the active bar palette: weekend days carry
/// a subtle accent, today is highlighted prominently. Paging arrows move by month or year, and a
/// "Today" button (shown only when paged away) jumps back to the current month. Closes on focus
/// loss or Escape.
/// </summary>
public partial class CalendarFlyoutWindow : Window
{
    private static CalendarFlyoutWindow? _open;
    // When the flyout dismisses because the clock was clicked, the click's mouse-down deactivates it
    // first; this timestamp lets the following ShowFor suppress the reopen, so the clock toggles.
    private static DateTime _lastDismiss = DateTime.MinValue;

    private const double SlideDistance = 12;   // px the surface travels while sliding in/out

    private readonly FrameworkElement _anchor;
    private DateTime _viewMonth;   // first day of the month currently shown
    private bool _positioned;
    private bool _placedAbove = true;
    private bool _dismissing;

    public static void ShowFor(FrameworkElement anchor)
    {
        // Clicking the clock while the calendar is open toggles it closed (it has just begun
        // dismissing from the deactivation), so swallow this reopen.
        if ((DateTime.UtcNow - _lastDismiss).TotalMilliseconds < 250) return;

        _open?.Close();
        var w = new CalendarFlyoutWindow(anchor);
        _open = w;
        w.Show();
        w.Activate();
    }

    private CalendarFlyoutWindow(FrameworkElement anchor)
    {
        _anchor    = anchor;
        _viewMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        InitializeComponent();

        Left = -10000; Top = -10000;   // parked off-screen until positioned (see OnContentRendered)

        BuildWeekdayHeader();
        Render();

        Deactivated += (_, _) => BeginDismiss();
        Closed      += (_, _) => { if (_open == this) _open = null; };
        KeyDown     += (_, e) => { if (e.Key == Key.Escape) BeginDismiss(); };
    }

    // ── Calendar rendering ────────────────────────────────────────────────────────

    private static DayOfWeek FirstDay => CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;

    private static bool IsWeekend(DayOfWeek d) => d is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private void BuildWeekdayHeader()
    {
        var names = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames; // indexed by DayOfWeek
        for (int i = 0; i < 7; i++)
        {
            var dow = (DayOfWeek)(((int)FirstDay + i) % 7);
            var tb = new TextBlock
            {
                Text                = names[(int)dow],
                FontSize            = 11,
                FontWeight          = FontWeights.SemiBold,
                TextAlignment       = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin              = new Thickness(0, 0, 0, 4)
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, IsWeekend(dow) ? "Accent" : "TextSecondary");
            WeekdayHeader.Children.Add(tb);
        }
    }

    private void Render()
    {
        MonthLabel.Text = _viewMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

        var today = DateTime.Today;
        // Hidden (not Collapsed) on the current month so its row is always reserved — toggling it
        // never resizes the flyout.
        TodayBtn.Visibility =
            _viewMonth.Year == today.Year && _viewMonth.Month == today.Month
                ? Visibility.Hidden : Visibility.Visible;

        // Back up from the 1st to the cell under the first weekday column.
        int lead = (((int)_viewMonth.DayOfWeek - (int)FirstDay) + 7) % 7;
        var start = _viewMonth.AddDays(-lead);

        // Theme-derived day styling. We branch on the surface luminance (not the theme name) so it
        // adapts to user themes too: on a dark surface "today" is a high-opacity accent fill with a
        // lighter-blue outline; on a light surface that washes out, so the alternate treatment is a
        // soft accent tint with a solid accent outline. Weekends are just a bluer number, no fill.
        var accent     = ResolveColor("Accent", Color.FromRgb(0x00, 0x78, 0xD4));
        bool darkSurf  = Luminance(ResolveColor("StartMenuBg", Colors.Black)) < 0.5;

        var weekendFg  = Frozen(darkSurf ? Color.FromRgb(0x5C, 0xB8, 0xFF) : accent);
        var todayFill  = Frozen(darkSurf
            ? Color.FromArgb(0xE6, accent.R, accent.G, accent.B)
            : Color.FromArgb(0x2E, accent.R, accent.G, accent.B));
        var todayEdge  = Frozen(darkSurf ? Color.FromRgb(0x8F, 0xCB, 0xFF) : accent);
        var todayFg    = darkSurf ? (Brush)Brushes.White : Frozen(accent);

        DayGrid.Children.Clear();
        for (int i = 0; i < 42; i++)
        {
            var date     = start.AddDays(i);
            bool inMonth = date.Month == _viewMonth.Month && date.Year == _viewMonth.Year;
            bool isToday = date == today;

            var text = new TextBlock
            {
                Text                = date.Day.ToString(CultureInfo.CurrentCulture),
                FontSize            = 13,
                TextAlignment       = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
            var cell = new Border
            {
                CornerRadius = new CornerRadius(6),
                Margin       = new Thickness(1),
                MinHeight    = 30,
                Child        = text
            };

            if (isToday)
            {
                // Today: prominent blue chip — high-opacity fill with a lighter-blue outline.
                cell.Background      = todayFill;
                cell.BorderBrush     = todayEdge;
                cell.BorderThickness = new Thickness(1.5);
                text.Foreground      = todayFg;
                text.FontWeight      = FontWeights.Bold;
            }
            else if (!inMonth)
            {
                // Leading/trailing days from the adjacent months, dimmed.
                text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                text.Opacity = 0.45;
            }
            else if (IsWeekend(date.DayOfWeek))
            {
                // Weekends: just a bluer number, no background.
                text.Foreground = weekendFg;
            }
            else
            {
                text.SetResourceReference(TextBlock.ForegroundProperty, "TaskBtnFg");
            }

            DayGrid.Children.Add(cell);
        }
    }

    private void PrevMonth_Click(object sender, RoutedEventArgs e) { _viewMonth = _viewMonth.AddMonths(-1); Render(); }
    private void NextMonth_Click(object sender, RoutedEventArgs e) { _viewMonth = _viewMonth.AddMonths(1);  Render(); }
    private void PrevYear_Click (object sender, RoutedEventArgs e) { _viewMonth = _viewMonth.AddYears(-1);  Render(); }
    private void NextYear_Click (object sender, RoutedEventArgs e) { _viewMonth = _viewMonth.AddYears(1);   Render(); }

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        _viewMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        Render();
    }

    private Color ResolveColor(string key, Color fallback)
        => TryFindResource(key) is SolidColorBrush b ? b.Color : fallback;

    // Perceptual-ish luminance (0..1) used to decide dark vs light surface.
    private static double Luminance(Color c)
        => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    // ── Positioning ───────────────────────────────────────────────────────────────
    // Pre-move onto the anchor's monitor before SizeToContent measures (mixed-DPI correctness), then
    // do the final placement once the size is finalized in OnContentRendered. The Root stays at
    // opacity 0 until AnimateIn, so neither step flashes.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        FlyoutPlacement.MoveToAnchorMonitor(this, _anchor);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_positioned) return;
        _positioned = true;
        _placedAbove = FlyoutPlacement.Place(this, _anchor);
        AnimateIn();
    }

    // ── Slide + fade in / out ─────────────────────────────────────────────────────
    // Emerge from the bar edge: a flyout above the bar slides up into place, one below slides down.

    private void AnimateIn()
    {
        double from = _placedAbove ? SlideDistance : -SlideDistance;
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        Root.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        Slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
    }

    private void BeginDismiss()
    {
        if (_dismissing) return;
        _dismissing  = true;
        _lastDismiss = DateTime.UtcNow;

        double to = _placedAbove ? SlideDistance : -SlideDistance;
        var ease  = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        var fade  = new DoubleAnimation(Root.Opacity, 0, TimeSpan.FromMilliseconds(120));
        fade.Completed += (_, _) => Close();
        Slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, to, TimeSpan.FromMilliseconds(120)) { EasingFunction = ease });
        Root.BeginAnimation(OpacityProperty, fade);
    }
}
