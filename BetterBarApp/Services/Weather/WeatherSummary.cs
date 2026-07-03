using System.Globalization;

namespace BetterBarApp.Services.Weather;

/// <summary>
/// Natural-language weather summarization: turns raw numbers into friendlier phrasing for tooltips
/// and the flyout. Three families of routine:
///   • <see cref="WindPhrase"/> — speed plus a compass direction it blows <i>from</i>.
///   • <see cref="Range"/> — a human description of a look-ahead window that grows with its length
///     ("for the next few hours", "through Tuesday", "early in the week", …).
///   • <see cref="Precipitation"/> — how the chance/amount of precipitation is spread across a window
///     ("80% chance of rain early, easing to 5% by 7 AM — about 4 mm total").
/// All time references use the location's local clock (the entries already carry local times).
/// </summary>
public static class WeatherSummary
{
    private static readonly string[] Compass16 =
        ["N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"];

    /// <summary>16-point compass abbreviation for a bearing in degrees ("" when unknown).</summary>
    public static string Compass(double degrees)
    {
        if (double.IsNaN(degrees)) return "";
        int idx = (int)Math.Round((((degrees % 360) + 360) % 360) / 22.5) % 16;
        return Compass16[idx];
    }

    /// <summary>e.g. "12 km/h from the NW" (meteorological — the direction the wind comes from).</summary>
    public static string WindPhrase(double speed, double degrees, string unit)
    {
        string dir = Compass(degrees);
        string spd = $"{Math.Round(speed)} {unit}";
        return string.IsNullOrEmpty(dir) ? spd : $"{spd} from the {dir}";
    }

    /// <summary>
    /// A friendly description of the look-ahead window whose wording grows with its length.
    /// <paramref name="start"/>/<paramref name="end"/> bound the window (end exclusive).
    /// </summary>
    public static string Range(DateTime start, DateTime end, bool isHours)
    {
        if (isHours)
        {
            double hrs = Math.Max(1, Math.Round((end - start).TotalHours));
            if (hrs <= 1)  return "over the next hour";
            if (hrs <= 3)  return "for the next few hours";
            if (hrs <= 6)  return "over the next several hours";
            if (hrs <= 12) return "later today";
            return "over the next day";
        }

        int days = Math.Max(1, (int)Math.Round((end - start).TotalDays));
        var lastDay = end.AddDays(-1);   // window end is exclusive
        if (days == 1) return "tomorrow";
        if (days == 2) return "over the next couple of days";

        string weekday = lastDay.ToString("dddd", CultureInfo.CurrentCulture);
        if (days <= 5)
            return (int)lastDay.DayOfWeek is >= 1 and <= 3 ? "early in the week" : $"through {weekday}";
        if (days <= 7) return "through the week";
        return $"over the next {days} days";
    }

    /// <summary>
    /// Describes how precipitation is distributed across an hourly window (assumed time-ordered):
    /// whether it is front-loaded, building, steady, a mid-window peak, or just scattered — plus the
    /// expected accumulation (snow when it is snowing). Empty when there is essentially none.
    /// </summary>
    public static string Precipitation(IReadOnlyList<HourEntry> window, string precipUnit, string snowUnit)
    {
        if (window.Count == 0) return "";

        int maxProb = window.Max(h => h.PrecipProb);
        if (maxProb < 15) return "Little to no precipitation expected.";

        bool snowing = window.Any(h => h.Snowfall > 0);
        string type  = snowing ? "snow" : "rain";
        bool spansDays = window[0].Time.Date != window[^1].Time.Date;

        int n      = window.Count;
        int third  = Math.Max(1, n / 3);
        double early = window.Take(third).Average(h => h.PrecipProb);
        double late  = window.Skip(n - third).Average(h => h.PrecipProb);
        int minProb  = window.Min(h => h.PrecipProb);
        var peak     = window.Aggregate((a, b) => b.PrecipProb > a.PrecipProb ? b : a);

        string lead;
        if (early - late > 25)
            lead = $"{window[0].PrecipProb}% chance of {type} early, easing to "
                 + $"{window[^1].PrecipProb}% by {TimeRef(window[^1].Time, spansDays)}";
        else if (late - early > 25)
            lead = $"{Capitalize(type)} chance climbing to {maxProb}% by {TimeRef(peak.Time, spansDays)}";
        else if (minProb >= 50)
            lead = $"Steady {type} likely, around {(int)Math.Round(window.Average(h => h.PrecipProb))}%";
        else if (maxProb - Math.Min(early, late) > 25)
            lead = $"{Capitalize(type)} most likely around {TimeRef(peak.Time, spansDays)} ({maxProb}%)";
        else
            lead = $"Scattered {type}, up to {maxProb}%";

        string accum = "";
        if (snowing)
        {
            double snow = window.Sum(h => h.Snowfall);
            if (snow > 0) accum = $" — about {Fmt(snow)} {snowUnit} accumulating";
        }
        else
        {
            double rain = window.Sum(h => h.Precip);
            if (rain > 0) accum = $" — about {Fmt(rain)} {precipUnit} total";
        }
        return lead + accum + ".";
    }

    /// <summary>
    /// Daily counterpart of <see cref="Precipitation"/> for multi-day windows (where hourly data no
    /// longer reaches): which days are wet and the total expected over the period.
    /// </summary>
    public static string Precipitation(IReadOnlyList<DayEntry> days, string precipUnit, string snowUnit)
    {
        if (days.Count == 0) return "";

        int maxProb = days.Max(d => d.PrecipProb);
        if (maxProb < 15) return "Little to no precipitation expected.";

        bool snowing = days.Any(d => d.SnowfallSum > 0);
        string type  = snowing ? "snow" : "rain";
        var wettest  = days.Aggregate((a, b) => b.PrecipProb > a.PrecipProb ? b : a);
        string peakDay = wettest.Date.ToString("dddd", CultureInfo.CurrentCulture);
        int wetDays  = days.Count(d => d.PrecipProb >= 40);

        string lead;
        if (wetDays == 0)
            lead = $"Slight chance of {type}, up to {maxProb}%";
        else if (wetDays >= Math.Ceiling(days.Count * 0.6))
            lead = $"{Capitalize(type)} likely most days, peaking {maxProb}% {peakDay}";
        else
            lead = $"{Capitalize(type)} mainly {peakDay} ({maxProb}%)";

        string accum = "";
        if (snowing)
        {
            double snow = days.Sum(d => d.SnowfallSum);
            if (snow > 0) accum = $" — about {Fmt(snow)} {snowUnit} of snow over the period";
        }
        else
        {
            double rain = days.Sum(d => d.PrecipSum);
            if (rain > 0) accum = $" — about {Fmt(rain)} {precipUnit} over the period";
        }
        return lead + accum + ".";
    }

    private static string TimeRef(DateTime t, bool spansDays)
        => spansDays
            ? $"{t.ToString("ddd", CultureInfo.CurrentCulture)} {t.ToString("h tt", CultureInfo.CurrentCulture)}"
            : t.ToString("h tt", CultureInfo.CurrentCulture);

    private static string Fmt(double v) => v.ToString("0.#", CultureInfo.CurrentCulture);

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
}
