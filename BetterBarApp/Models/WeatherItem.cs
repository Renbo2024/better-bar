using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterBarApp.Models;

/// <summary>Unit system for the weather readouts.</summary>
public enum WeatherUnits { Metric, Imperial }

/// <summary>Whether the optional Forecast section looks ahead by hours or by whole days.</summary>
public enum WeatherForecastKind { Hours, Days }

/// <summary>
/// A weather bar item, sourced from Open-Meteo. Shows a title (location name by default, or a custom
/// override) over one or more sections: a mandatory "Current" section and an optional look-ahead
/// "Forecast" section. Clicking opens a flyout with hourly and daily forecasts. Each instance is
/// independently configured (its own location, units, sections, etc.).
/// </summary>
public partial class WeatherItem : BarItem
{
    public WeatherItem()
    {
        TypeKey     = ItemTypes.Weather;
        DisplayName = "Weather";
    }

    // ── Location (from Open-Meteo geocoding) ──
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private bool   _hasLocation;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private string _locationName = "";
    [ObservableProperty] private double _latitude;
    [ObservableProperty] private double _longitude;

    // ── Title (the whole title row can be turned off; "" → the location name) ──
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private bool   _showTitle = true;
    [ObservableProperty] private string _title = "";

    // ── Units ──
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private WeatherUnits _units = WeatherUnits.Metric;

    // ── Optional Forecast section ──
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private bool _showForecast;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private WeatherForecastKind _forecastKind = WeatherForecastKind.Hours;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private int _forecastAmount = 2;

    // ── Hover tooltip (extra detail) ──
    [ObservableProperty] private bool _showTooltip   = true;
    [ObservableProperty] private int  _tooltipDelayMs = 600;

    // ── Appearance ──
    [ObservableProperty] private int    _margin              = 10;
    [ObservableProperty] private int    _iconSize            = 22;
    [ObservableProperty] private int    _sectionSpacing      = 14;
    [ObservableProperty] private string _textColor           = "";   // "" → theme text colour

    // ── Fonts (face + size) for each text element ──
    [ObservableProperty] private string _titleFontFamily       = "Segoe UI";
    [ObservableProperty] private int    _titleFontSize         = 11;
    [ObservableProperty] private string _measurementFontFamily = "Segoe UI";
    [ObservableProperty] private int    _measurementFontSize   = 11;
    [ObservableProperty] private string _labelFontFamily       = "Segoe UI";
    [ObservableProperty] private int    _labelFontSize         = 9;

    // ── How often the bar control re-fetches (minutes) ──
    [ObservableProperty] private int _refreshMinutes = 15;

    /// <summary>Largest look-ahead the Forecast section supports (hours, and days).</summary>
    public const int MaxHours = 24;
    public const int MaxDays  = 7;

    [JsonIgnore] public bool Metric          => Units == WeatherUnits.Metric;
    [JsonIgnore] public int  EffectiveAmount => ForecastKind == WeatherForecastKind.Days
                                                ? Math.Clamp(ForecastAmount, 1, MaxDays)
                                                : Math.Clamp(ForecastAmount, 1, MaxHours);

    /// <summary>The title actually shown on the bar (custom override, else the location name).</summary>
    [JsonIgnore]
    public string EffectiveTitle =>
        !string.IsNullOrWhiteSpace(Title) ? Title
        : HasLocation && !string.IsNullOrWhiteSpace(LocationName) ? LocationName
        : "Weather";

    /// <summary>Label under the Forecast section, e.g. "2H", "Tomorrow", "3-Day".</summary>
    public string ForecastLabel() => ForecastKind == WeatherForecastKind.Hours
        ? $"{EffectiveAmount}H"
        : EffectiveAmount <= 1 ? "Tomorrow" : $"{EffectiveAmount}-Day";

    [JsonIgnore]
    public override string Description
    {
        get
        {
            if (!HasLocation) return "(no location set)";
            var bits = new List<string>
            {
                string.IsNullOrWhiteSpace(LocationName) ? "Location set" : LocationName,
                Metric ? "Metric" : "Imperial",
            };
            if (ShowForecast) bits.Add(ForecastLabel());
            return string.Join("  ·  ", bits);
        }
    }
}
