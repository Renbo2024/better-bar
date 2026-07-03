using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BetterBarApp.Controls;
using BetterBarApp.Models;
using BetterBarApp.Services.Weather;
using Wpf.Ui.Controls;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using TextBlock = System.Windows.Controls.TextBlock;
using Grid = System.Windows.Controls.Grid;

namespace BetterBarApp.Windows;

/// <summary>
/// Flyout shown when a Weather bar item is clicked. Two tabs — Hourly and Daily (the last viewed one
/// re-opens by default) — each with a detail list on the left and line charts (temperature, humidity,
/// wind, precipitation) on the right, capped at the item's flyout count. Mirrors the calendar flyout's
/// bar-anchored positioning and slide/fade. Closes on focus loss or Escape.
/// </summary>
public partial class WeatherFlyoutWindow : Window
{
    private static WeatherFlyoutWindow? _open;
    private static DateTime _lastDismiss = DateTime.MinValue;

    private const double SlideDistance = 12;

    private readonly WeatherItem  _item;
    private readonly FrameworkElement _anchor;
    private bool _positioned;
    private bool _placedAbove = true;
    private bool _dismissing;

    public static void ShowFor(WeatherItem item, WeatherData? data, FrameworkElement anchor)
    {
        // Clicking the item while open toggles it closed (it deactivated on mouse-down).
        if ((DateTime.UtcNow - _lastDismiss).TotalMilliseconds < 250) return;

        _open?.Close();
        var w = new WeatherFlyoutWindow(item, data, anchor);
        _open = w;
        w.Show();
        w.Activate();
    }

    private WeatherFlyoutWindow(WeatherItem item, WeatherData? data, FrameworkElement anchor)
    {
        _item   = item;
        _anchor = anchor;
        InitializeComponent();

        Left = -10000; Top = -10000;   // parked off-screen until positioned (see OnContentRendered)
        Build(data);

        Deactivated += (_, _) => BeginDismiss();
        Closed      += (_, _) => { if (_open == this) _open = null; };
        KeyDown     += (_, e) => { if (e.Key == Key.Escape) BeginDismiss(); };
    }

    private const int HourPageSize = 7, DayPageSize = 7;   // items per page (uniform)
    private const int HourLimit = 48, DayLimit = 14;       // provider maxima we page through

    private WeatherData? _data;
    private DateTime _now;

    // Which tab was last viewed; re-shown by default on the next open.
    private static int _lastTab;
    // Current page per tab (0 = hourly, 1 = daily); reset each open.
    private readonly int[] _page = [0, 0];

    // Chart line colours (data-viz palette; read on both light and dark surfaces).
    private static readonly Brush TempBrush     = Frozen(0xE8, 0x91, 0x5B);  // daily High
    private static readonly Brush LowBrush      = Frozen(0x5A, 0xA0, 0xE0);  // daily Low
    private static readonly Brush TempHourBrush = Frozen(0xB5, 0x7E, 0xDC);  // hourly single temp (not a High)
    private static readonly Brush HumBrush      = Frozen(0x4F, 0xB0, 0xE8);
    private static readonly Brush WindBrush     = Frozen(0x79, 0xC0, 0xA8);
    private static readonly Brush PrecipBrush   = Frozen(0x5B, 0x8D, 0xEF);
    private static readonly Brush UvBrush       = Frozen(0xE0, 0xA9, 0x2E);  // amber — UV index

    private void Build(WeatherData? data)
    {
        _data = data;
        HeaderText.Text = _item.EffectiveTitle;

        if (data == null)
        {
            SubText.Text = "Weather unavailable";
            HourlyTab.Visibility = DailyTab.Visibility = Visibility.Collapsed;
            PrevBtn.Visibility = NextBtn.Visibility = Visibility.Collapsed;
            return;
        }

        _now = data.Current.Time;
        var c = data.Current;
        SubText.Text = $"Now {Temp(c.Temp)} · {WeatherCodes.Describe(c.Code)} · Humidity {c.Humidity}% · "
                     + $"Wind {WeatherSummary.WindPhrase(c.Wind, c.WindDir, data.WindUnit)}";

        // Narrative: how precipitation is spread over the next half-day.
        var soon = data.Hourly.Where(e => e.Time > _now).Take(12).ToList();
        var narrative = WeatherSummary.Precipitation(soon, data.PrecipUnit, data.SnowUnit);
        NarrativeText.Text       = narrative;
        NarrativeText.Visibility = narrative.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        SelectTab(_lastTab);
    }

    // ── Tabs + pagination ───────────────────────────────────────────────────────
    private void HourlyTab_Click(object sender, MouseButtonEventArgs e) => SelectTab(0);
    private void DailyTab_Click(object sender, MouseButtonEventArgs e)  => SelectTab(1);
    private void Prev_Click(object sender, MouseButtonEventArgs e) { _page[_lastTab]--; RenderPage(); }
    private void Next_Click(object sender, MouseButtonEventArgs e) { _page[_lastTab]++; RenderPage(); }

    private void SelectTab(int tab)
    {
        _lastTab = tab;
        StyleTab(HourlyTab, HourlyTabText, tab == 0);
        StyleTab(DailyTab, DailyTabText, tab == 1);
        RenderPage();
    }

    // Rebuilds the detail list + charts for the current tab's current page, and the pager state.
    private void RenderPage()
    {
        if (_data == null) return;
        bool hourly  = _lastTab == 0;
        int pageSize = hourly ? HourPageSize : DayPageSize;

        // Hourly: as many hours as the API gives (≤48), trimmed to whole pages so there's never a
        // short final page. Daily: today + up to 14 days (exactly two full pages of 7).
        var allHours = _data.Hourly.Where(e => e.Time > _now).Take(HourLimit).ToList();
        int whole = allHours.Count / HourPageSize * HourPageSize;
        var hours = (whole > 0 ? allHours.Take(whole) : allHours).ToList();
        var days  = _data.Daily.Where(e => e.Date.Date >= _now.Date).Take(DayLimit).ToList();
        int total = hourly ? hours.Count : days.Count;
        int pages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));

        _page[_lastTab] = Math.Clamp(_page[_lastTab], 0, pages - 1);
        int page = _page[_lastTab];
        int skip = page * pageSize;

        DetailList.Children.Clear();
        Charts.Children.Clear();

        if (hourly)
        {
            var slice = hours.Skip(skip).Take(pageSize).ToList();
            for (int i = 0; i < slice.Count; i++)
                DetailList.Children.Add(HourRow(slice[i], last: i == slice.Count - 1));
            // Slot order matches the Daily tab (temperature, wind, precip chance), with the bottom slot
            // shared: Humidity here ↔ UV index on Daily.
            AddChart(Charts, "Temperature", "°", slice.Select(h => h.Temp), null, TempHourBrush);
            AddChart(Charts, "Wind", $" {_data.WindUnit}", slice.Select(h => h.Wind), null, WindBrush);
            AddChart(Charts, "Precip chance", "%", slice.Select(h => (double)h.PrecipProb), null, PrecipBrush,
                     fill: true, axisMin: 0, axisMax: 100);
            AddChart(Charts, "Humidity", "%", slice.Select(h => (double)h.Humidity), null, HumBrush);
            PageLabel.Text = slice.Count > 0
                ? $"{slice[0].Time.ToString("h tt", CultureInfo.CurrentCulture)} – {slice[^1].Time.ToString("h tt", CultureInfo.CurrentCulture)}"
                : "—";
        }
        else
        {
            var slice = days.Skip(skip).Take(pageSize).ToList();
            for (int i = 0; i < slice.Count; i++)
                DetailList.Children.Add(DayRow(slice[i], _now.Date, last: i == slice.Count - 1));
            AddChart(Charts, "High / Low", "°", slice.Select(d => d.TempMax), slice.Select(d => d.TempMin), TempBrush, LowBrush);
            AddChart(Charts, "Wind", $" {_data.WindUnit}", slice.Select(d => d.WindMax), null, WindBrush);
            AddChart(Charts, "Precip chance", "%", slice.Select(d => (double)d.PrecipProb), null, PrecipBrush,
                     fill: true, axisMin: 0, axisMax: 100);
            AddChart(Charts, "UV index", "", slice.Select(d => d.UvMax), null, UvBrush, axisMin: 0, axisMax: 11);
            PageLabel.Text = slice.Count > 0
                ? $"{DayLabel(slice[0].Date)} – {DayLabel(slice[^1].Date)}"
                : "—";
        }

        SetEnabled(PrevBtn, page > 0);
        SetEnabled(NextBtn, page < pages - 1);
    }

    private string DayLabel(DateTime d)
        => d.Date == _now.Date ? "Today" : d.ToString("ddd", CultureInfo.CurrentCulture);

    private static void SetEnabled(UIElement btn, bool enabled)
    {
        btn.Opacity = enabled ? 1.0 : 0.3;
        btn.IsHitTestVisible = enabled;
    }

    private static void AddChart(Panel host, string title, string unit,
        IEnumerable<double> s1, IEnumerable<double>? s2, Brush stroke1, Brush? stroke2 = null, bool fill = false,
        double? axisMin = null, double? axisMax = null)
        => host.Children.Add(WeatherChart.Build(title, unit, s1.ToList(), s2?.ToList(), stroke1, stroke2, fill, axisMin, axisMax));

    private static void StyleTab(Border tab, TextBlock text, bool selected)
    {
        if (selected)
        {
            tab.SetResourceReference(Border.BackgroundProperty, "Accent");
            text.Foreground = Brushes.White;
        }
        else
        {
            tab.Background = Brushes.Transparent;
            text.SetResourceReference(TextBlock.ForegroundProperty, "TaskBtnFg");
        }
    }

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }

    private FrameworkElement HourRow(HourEntry h, bool last)
    {
        string detail = Join(" · ", $"RH {h.Humidity}%", Precip(h.PrecipProb, h.Precip, h.Snowfall));
        // The hourly range spans multiple days, so each row carries the day under its time.
        return Row(h.Time.ToString("h tt", CultureInfo.CurrentCulture), h.Code, Temp(h.Temp), detail, last,
                   subLabel: DayLabel(h.Time));
    }

    private FrameworkElement DayRow(DayEntry d, DateTime today, bool last)
    {
        string label  = d.Date.Date == today ? "Today" : d.Date.ToString("ddd", CultureInfo.CurrentCulture);
        string detail = Precip(d.PrecipProb, d.PrecipSum, d.SnowfallSum);
        return Row(label, d.Code, $"{Temp(d.TempMax)} / {Temp(d.TempMin)}", detail, last);
    }

    // A row: [time/day (+ optional sub-label)] [icon] [main over an optional grey detail line], hairline below.
    private FrameworkElement Row(string label, int code, string main, string detail, bool last, string? subLabel = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(Cell(label, -1, size: 13, opacity: 0.9));
        if (subLabel != null) labels.Children.Add(Cell(subLabel, -1, size: 10, opacity: 0.55));
        Grid.SetColumn(labels, 0);
        grid.Children.Add(labels);
        grid.Children.Add(Glyph(WeatherIcons.For(code), 1));

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(Cell(main, -1, size: 14.5));
        if (detail.Length > 0)
            stack.Children.Add(Cell(detail, -1, size: 11, opacity: 0.6, top: 1));
        Grid.SetColumn(stack, 2);
        grid.Children.Add(stack);

        var row = new Border { Padding = new Thickness(0, 6, 0, 6), Child = grid };
        if (!last)
        {
            row.BorderThickness = new Thickness(0, 0, 0, 1);
            row.SetResourceReference(Border.BorderBrushProperty, "StartMenuBorder");
        }
        return row;
    }

    private TextBlock Cell(string text, int col, double size, double opacity = 1.0, double top = 0)
    {
        var tb = new TextBlock
        {
            Text              = text,
            FontSize          = size,
            Opacity           = opacity,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, top, 0, 0),
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "TaskBtnFg");
        if (col >= 0) Grid.SetColumn(tb, col);
        return tb;
    }

    private SymbolIcon Glyph(SymbolRegular symbol, int col)
    {
        var icon = new SymbolIcon { Symbol = symbol, FontSize = 20, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(ForegroundProperty, "TaskBtnFg");
        Grid.SetColumn(icon, col);
        return icon;
    }

    // "60% · 2.0 mm" (rain) or "60% · 3.0 cm" (snow accumulation, shown instead of liquid). Empty when none.
    private string Precip(int chance, double precip, double snow)
    {
        var parts = new List<string>();
        if (chance > 0) parts.Add($"{chance}%");
        if (snow > 0)        parts.Add($"{snow.ToString("0.#", CultureInfo.CurrentCulture)} {_data!.SnowUnit}");
        else if (precip > 0) parts.Add($"{precip.ToString("0.#", CultureInfo.CurrentCulture)} {_data!.PrecipUnit}");
        return string.Join(" · ", parts);
    }

    private static string Join(string sep, params string[] parts)
        => string.Join(sep, parts.Where(p => !string.IsNullOrEmpty(p)));

    private string Temp(double v) => $"{Math.Round(v)}°";

    // ── Positioning + animation (shared with the other flyouts via FlyoutPlacement) ───────
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

    private void AnimateIn()
    {
        double from = _placedAbove ? SlideDistance : -SlideDistance;
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
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
