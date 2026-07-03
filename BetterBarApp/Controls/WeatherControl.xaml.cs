using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BetterBarApp.Models;
using BetterBarApp.Services.Weather;
using BetterBarApp.Windows;
using Wpf.Ui.Controls;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using TextBlock = System.Windows.Controls.TextBlock;

namespace BetterBarApp.Controls;

/// <summary>
/// Renders a <see cref="WeatherItem"/>: a title over a "Current" section (icon + temperature and
/// humidity) and an optional "Forecast" section (icon of the worst condition in the chosen window).
/// Fetches from Open-Meteo on load and on a refresh timer; hovering a section shows extra detail and
/// clicking opens the hourly/daily flyout.
/// </summary>
public partial class WeatherControl : UserControl
{
    private readonly WeatherItem _item;
    private readonly DispatcherTimer _timer = new();

    private CancellationTokenSource? _cts;
    private WeatherData? _data;

    // Section widgets we refresh in place once data arrives.
    private SymbolIcon? _curIcon, _fcIcon;
    private TextBlock?  _curTemp, _curHum, _fcTemp, _fcHum;

    public WeatherControl(WeatherItem item)
    {
        _item = item;
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildLayout();
        _timer.Interval = TimeSpan.FromMinutes(Math.Max(1, _item.RefreshMinutes));
        _timer.Tick += (_, _) => Load();
        _timer.Start();
        Load();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _cts?.Cancel();
        _cts = null;
    }

    private Brush TextBrush =>
        ParseBrush(_item.TextColor) ?? (TryFindResource("TaskBtnFg") as Brush) ?? Brushes.White;

    // ── Static layout (built once; values filled by RenderData) ─────────────────
    private void BuildLayout()
    {
        Root.Margin = new Thickness(_item.Margin, 0, _item.Margin, 0);

        TitleText.Visibility = _item.ShowTitle ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Text       = _item.EffectiveTitle;
        TitleText.FontSize   = Math.Max(1, _item.TitleFontSize);
        TitleText.FontWeight = FontWeights.SemiBold;
        TitleText.Foreground = TextBrush;
        TitleText.Margin     = new Thickness(0, 0, 0, 2);
        ApplyFont(TitleText, _item.TitleFontFamily);

        Sections.Children.Clear();
        Sections.Children.Add(BuildSection("Current", out _curIcon, out _curTemp, out _curHum,
                                           () => CurrentTooltip()));

        if (_item.ShowForecast)
        {
            var sec = BuildSection(_item.ForecastLabel(), out _fcIcon, out _fcTemp, out _fcHum,
                                   () => ForecastTooltip());
            sec.Margin = new Thickness(_item.SectionSpacing, 0, 0, 0);
            Sections.Children.Add(sec);
        }
    }

    // One section: [icon | temp/humidity] with a label beneath. Returns the container.
    private FrameworkElement BuildSection(string label, out SymbolIcon icon, out TextBlock temp, out TextBlock hum,
                                          Func<string?> tooltip)
    {
        // MinWidth/MinHeight pin the icon's footprint to its size so the panel reserves the right
        // space even before the Fluent glyph font is realized (otherwise it under-measures → clips).
        double sz = Math.Max(8, _item.IconSize);
        icon = new SymbolIcon { Symbol = SymbolRegular.WeatherPartlyCloudyDay24, FontSize = sz,
                                MinWidth = sz, MinHeight = sz, Foreground = TextBrush };

        temp = NewMeasure();
        hum  = NewMeasure();
        var readouts = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center,
                                        Margin = new Thickness(4, 0, 0, 0) };
        readouts.Children.Add(temp);
        readouts.Children.Add(hum);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(icon);
        row.Children.Add(readouts);

        var caption = new TextBlock
        {
            Text                = label,
            FontSize            = Math.Max(1, _item.LabelFontSize),
            Foreground          = TextBrush,
            Opacity             = 0.75,
            TextAlignment       = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin              = new Thickness(0, 1, 0, 0),
        };
        ApplyFont(caption, _item.LabelFontFamily);

        var section = new StackPanel { Orientation = Orientation.Vertical };
        section.Children.Add(row);
        section.Children.Add(caption);

        HoverTip.Attach(section, tooltip, _item.ShowTooltip, _item.TooltipDelayMs);
        return section;
    }

    private TextBlock NewMeasure()
    {
        var tb = new TextBlock
        {
            Text       = "--",
            FontSize   = Math.Max(1, _item.MeasurementFontSize),
            Foreground = TextBrush,
            LineHeight = Math.Max(1, _item.MeasurementFontSize) + 2,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };
        ApplyFont(tb, _item.MeasurementFontFamily);
        return tb;
    }

    private static void ApplyFont(TextBlock tb, string family)
    {
        if (!string.IsNullOrWhiteSpace(family)) tb.FontFamily = new FontFamily(family);
    }

    // ── Data load / render ───────────────────────────────────────────────────────
    private async void Load()
    {
        if (!_item.HasLocation) { RenderData(); return; }
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        try
        {
            var data = await WeatherService.GetAsync(_item.Latitude, _item.Longitude, _item.Metric, ct: token);
            if (token.IsCancellationRequested) return;
            _data = data;
            RenderData();
        }
        catch { /* leave the last reading in place */ }
    }

    private void RenderData()
    {
        TitleText.Text = _item.EffectiveTitle;

        if (_data == null)
        {
            SetSection(_curIcon, _curTemp, _curHum, SymbolRegular.WeatherPartlyCloudyDay24, "--", "--");
            SetSection(_fcIcon, _fcTemp, _fcHum, SymbolRegular.WeatherPartlyCloudyDay24, "--", "--");
            return;
        }

        var c = _data.Current;
        SetSection(_curIcon, _curTemp, _curHum, WeatherIcons.For(c.Code), Temp(c.Temp), $"{c.Humidity}%");

        if (_item.ShowForecast && _fcIcon != null)
        {
            var f = SummarizeForecast(_data);
            SetSection(_fcIcon, _fcTemp, _fcHum, WeatherIcons.For(f.WorstCode), Temp(f.Temp), $"{f.Humidity}%");
        }

        // Data arrived asynchronously and changed our content width; the parent BarItemsPanel caches
        // the finite width it measured us at, so without this the item stays sized for the "--"
        // placeholders and the icons clip until a rebuild (see BarItemsPanel.InvalidateForChild).
        BarItemsPanel.InvalidateForChild(this);
        // And once more after this render completes — by then the Fluent glyph font is realized, so a
        // re-measure picks up the icons' true size (the residual "clipped until you open config" case).
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => BarItemsPanel.InvalidateForChild(this)));
    }

    private static void SetSection(SymbolIcon? icon, TextBlock? temp, TextBlock? hum,
                                   SymbolRegular symbol, string t, string h)
    {
        if (icon != null) icon.Symbol = symbol;
        if (temp != null) temp.Text   = t;
        if (hum  != null) hum.Text    = h;
    }

    // The forecast look-ahead window: its bounds, whether it's hours- or days-based, and the hourly
    // and daily entries inside it. Hours mode is driven by the hourly series (within the 48h horizon);
    // Days mode by the daily series (hourly no longer spans multi-day windows).
    private (DateTime Start, DateTime End, bool IsHours, List<HourEntry> Hours, List<DayEntry> Days) ForecastWindow(WeatherData data)
    {
        var now = data.Current.Time;
        bool isHours = _item.ForecastKind == WeatherForecastKind.Hours;
        DateTime start, end;
        if (isHours) { start = now; end = now.AddHours(_item.EffectiveAmount); }
        else { var tomorrow = now.Date.AddDays(1); start = tomorrow; end = tomorrow.AddDays(_item.EffectiveAmount); }
        var hours = data.Hourly.Where(h => h.Time >= start && h.Time < end).ToList();
        var days  = data.Daily.Where(d => d.Date >= start.Date && d.Date < end.Date).ToList();
        return (start, end, isHours, hours, days);
    }

    // ── Forecast summary: worst condition + representative temp/humidity over the window ──
    private (int WorstCode, double Temp, int Humidity, int PrecipProb) SummarizeForecast(WeatherData data)
    {
        var (_, _, isHours, hours, days) = ForecastWindow(data);
        int worst; double temp; int hum, precip;
        if (isHours && hours.Count > 0)
        {
            worst  = hours.OrderByDescending(h => WeatherCodes.Severity(h.Code)).First().Code;
            temp   = hours.Max(h => h.Temp);
            hum    = (int)Math.Round(hours.Average(h => h.Humidity));
            precip = hours.Max(h => h.PrecipProb);
        }
        else
        {
            if (days.Count == 0) days = data.Daily.Take(1).ToList();
            worst  = days.Count > 0 ? days.OrderByDescending(d => WeatherCodes.Severity(d.Code)).First().Code : data.Current.Code;
            temp   = days.Count > 0 ? days.Max(d => d.TempMax) : data.Current.Temp;
            // Daily humidity isn't a forecast field; approximate from any hourly we have, else current.
            hum    = hours.Count > 0 ? (int)Math.Round(hours.Average(h => h.Humidity)) : data.Current.Humidity;
            precip = days.Count > 0 ? days.Max(d => d.PrecipProb) : 0;
        }
        return (worst, temp, hum, precip);
    }

    // ── Tooltips ──────────────────────────────────────────────────────────────────
    private string? CurrentTooltip()
    {
        if (_data == null) return _item.HasLocation ? "Loading weather…" : "No location set";
        var c = _data.Current;
        return string.Join("\n",
            _item.EffectiveTitle,
            WeatherCodes.Describe(c.Code),
            $"Temperature: {Temp(c.Temp)}  (feels like {Temp(c.Apparent)})",
            $"Humidity: {c.Humidity}%",
            $"Wind: {WeatherSummary.WindPhrase(c.Wind, c.WindDir, _data.WindUnit)}");
    }

    private string? ForecastTooltip()
    {
        if (_data == null) return _item.ForecastLabel();
        var f = SummarizeForecast(_data);
        var (start, end, isHours, hours, days) = ForecastWindow(_data);
        var lines = new List<string>
        {
            _item.ForecastLabel(),
            $"Worst expected: {WeatherCodes.Describe(f.WorstCode)}",
            $"High: {Temp(f.Temp)}   ·   Humidity ~{f.Humidity}%",
        };
        // How the precipitation is spread over the window, prefixed with a friendly range phrase.
        // Hours mode reads the hourly spread; Days mode the daily one (hourly tops out at 48h).
        string precip = isHours
            ? WeatherSummary.Precipitation(hours, _data.PrecipUnit, _data.SnowUnit)
            : WeatherSummary.Precipitation(days, _data.PrecipUnit, _data.SnowUnit);
        if (precip.Length > 0)
        {
            string range = WeatherSummary.Range(start, end, isHours);
            lines.Add($"{char.ToUpper(range[0]) + range[1..]}: {precip}");
        }
        return string.Join("\n", lines);
    }

    private string Temp(double v) => $"{Math.Round(v)}°";

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_item.HasLocation) return;
        HoverTip.Dismiss();   // don't leave a hover tip floating over the flyout
        try { WeatherFlyoutWindow.ShowFor(_item, _data, this); } catch { }
    }

    private static Brush? ParseBrush(string color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;
        try { return (Brush)new BrushConverter().ConvertFromString(color)!; }
        catch { return null; }
    }
}
