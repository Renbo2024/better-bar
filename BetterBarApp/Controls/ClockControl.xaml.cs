using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BetterBarApp.Models;
using BetterBarApp.Windows;

namespace BetterBarApp.Controls;

/// <summary>
/// Renders a <see cref="ClockItem"/>: the title / time / date / time-zone lines it
/// enables, in the configured top-to-bottom order, each with its own font and colour, in
/// the clock's time zone. Ticks once a second. Rebuilt when the item's settings change.
/// </summary>
public partial class ClockControl : UserControl
{
    private readonly ClockItem _item;
    private readonly DispatcherTimer _timer;
    private readonly TextBlock _title = NewLine();
    private readonly TextBlock _time  = NewLine();
    private readonly TextBlock _date  = NewLine();
    private readonly TextBlock _zone  = NewLine();

    public ClockControl(ClockItem item)
    {
        _item = item;
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateNow();
        Loaded   += OnLoaded;
        Unloaded += (_, _) => _timer.Stop();
    }

    private static TextBlock NewLine() => new() { TextAlignment = TextAlignment.Center };

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyConfig();
        UpdateNow();
        _timer.Start();

        // Our text lines are populated here (ApplyConfig/UpdateNow), AFTER the panel measured us at our
        // initially-empty width. Custom fonts also realize their glyphs asynchronously. Force a panel
        // re-measure once that has settled, or the clock stays sized for empty text and clips on its
        // right edge until something else reflows the bar (see BarItemsPanel.InvalidateForChild).
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => BarItemsPanel.InvalidateForChild(this)));
    }

    // Open our own themed calendar flyout (replacing the native Windows date/time panel).
    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        try { CalendarFlyoutWindow.ShowFor(this); } catch { }
    }

    private void ApplyConfig()
    {
        Stack.Margin = new Thickness(_item.Margin, 0, _item.Margin, 0);

        ApplyLine(_title, _item.TitleFontFamily,    _item.TitleFontSize,    _item.TitleBold,    _item.TitleItalic,    _item.TitleColor);
        ApplyLine(_time,  _item.TimeFontFamily,     _item.TimeFontSize,     _item.TimeBold,     _item.TimeItalic,     _item.TimeColor);
        ApplyLine(_date,  _item.DateFontFamily,     _item.DateFontSize,     _item.DateBold,     _item.DateItalic,     _item.DateColor);
        ApplyLine(_zone,  _item.TimeZoneFontFamily, _item.TimeZoneFontSize, _item.TimeZoneBold, _item.TimeZoneItalic, _item.TimeZoneColor);

        _title.Text = _item.Title;   // static text — not time-dependent

        // Add the enabled lines in the configured order.
        Stack.Children.Clear();
        foreach (var line in _item.LineOrder)
        {
            switch (line)
            {
                case ClockLine.Title    when _item.ShowTitle:    Stack.Children.Add(_title); break;
                case ClockLine.Time     when _item.ShowTime:     Stack.Children.Add(_time);  break;
                case ClockLine.Date     when _item.ShowDate:     Stack.Children.Add(_date);  break;
                case ClockLine.TimeZone when _item.ShowTimeZone: Stack.Children.Add(_zone);  break;
            }
        }
    }

    private static void ApplyLine(TextBlock tb, string family, int size, bool bold, bool italic, string color)
    {
        if (!string.IsNullOrWhiteSpace(family)) tb.FontFamily = new FontFamily(family);
        tb.FontSize   = Math.Max(1, size);
        tb.FontWeight = bold   ? FontWeights.Bold   : FontWeights.Normal;
        tb.FontStyle  = italic ? FontStyles.Italic  : FontStyles.Normal;

        // Custom colour, or fall back to the bar's text brush.
        var brush = ParseBrush(color);
        if (brush != null) tb.Foreground = brush;
        else tb.SetResourceReference(ForegroundProperty, "TaskBtnFg");
    }

    private static Brush? ParseBrush(string color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;
        try { return (Brush)new BrushConverter().ConvertFromString(color)!; }
        catch { return null; }
    }

    private void UpdateNow()
    {
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.Now, _item.ResolveZone());
        bool changed = false;
        if (_item.ShowTime)     changed |= SetText(_time, now.ToString(_item.TimeFormat()));
        if (_item.ShowDate)     changed |= SetText(_date, now.ToString(_item.DateFormat()));
        if (_item.ShowTimeZone) changed |= SetText(_zone, _item.TimeZoneLabel());

        // The clock's text can change width over time (e.g. 9:59 → 10:00, or a date rollover) and on the
        // first tick after a (re)build. The panel caches the finite width it allocated us, so a widened
        // clock would clip on the right until a reflow — nudge the panel whenever our text changes.
        if (changed) BarItemsPanel.InvalidateForChild(this);
    }

    private static bool SetText(TextBlock tb, string text)
    {
        if (tb.Text == text) return false;
        tb.Text = text;
        return true;
    }
}
