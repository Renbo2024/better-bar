using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace BetterBarApp.Services.Weather;

/// <summary>A geocoding hit from Open-Meteo's location search.</summary>
public sealed record GeoResult(string Name, double Latitude, double Longitude, string? Admin1, string? Country)
{
    /// <summary>"City, Region, Country" with the empty parts dropped.</summary>
    public string Display => string.Join(", ", new[] { Name, Admin1, Country }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

public sealed record CurrentWeather(DateTime Time, double Temp, int Humidity, int Code, double Apparent, double Wind, double WindDir);
public sealed record HourEntry(DateTime Time, double Temp, int Humidity, int Code, double Precip, int PrecipProb, double Snowfall, double Wind, double WindDir);
public sealed record DayEntry(DateTime Date, int Code, double TempMax, double TempMin, int PrecipProb, double PrecipSum, double SnowfallSum, double WindMax, double WindDir, double UvMax);

/// <summary>A complete forecast snapshot for one location, in the requested unit system.</summary>
public sealed class WeatherData
{
    public required bool   Metric     { get; init; }
    public required string WindUnit   { get; init; }
    public required string PrecipUnit { get; init; }   // rain/precip total: mm or in
    public required string SnowUnit   { get; init; }   // snow accumulation: cm or in
    public required CurrentWeather Current { get; init; }
    public required IReadOnlyList<HourEntry> Hourly { get; init; }
    public required IReadOnlyList<DayEntry>  Daily  { get; init; }
    public DateTime FetchedUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Talks to the free Open-Meteo APIs: geocoding (location search) and forecast. Forecasts are cached
/// briefly per location+units so the many weather items on a bar share one network round-trip and a
/// refresh tick doesn't hammer the API. All times come back in the location's own zone
/// (<c>timezone=auto</c>), so windowing is done relative to <see cref="CurrentWeather.Time"/> with no
/// extra zone math. Requests the full range in one call — 14 days of daily plus 48 hours of hourly
/// (via forecast_days + forecast_hours) — so one fetch serves every item at a spot.
/// </summary>
public static class WeatherService
{
    private const string GeoUrl  = "https://geocoding-api.open-meteo.com/v1/search";
    private const string FcstUrl = "https://api.open-meteo.com/v1/forecast";

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly TimeSpan   _ttl  = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<string, WeatherData> _cache = new();

    static WeatherService()
    {
        // A UA is polite and avoids the occasional generic-client block.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("BetterBar/1.0 (+https://github.com)");
    }

    /// <summary>Searches place names. Returns an empty list on error or too-short queries.</summary>
    public static async Task<IReadOnlyList<GeoResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        query = query?.Trim() ?? "";
        if (query.Length < 2) return [];
        try
        {
            var url = $"{GeoUrl}?name={Uri.EscapeDataString(query)}&count=10&language=en&format=json";
            using var doc = await GetJsonAsync(url, ct);
            var list = new List<GeoResult>();
            if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
                foreach (var r in results.EnumerateArray())
                    list.Add(new GeoResult(Str(r, "name") ?? "", Dbl(r, "latitude"), Dbl(r, "longitude"),
                                           Str(r, "admin1"), Str(r, "country")));
            return list;
        }
        catch { return []; }
    }

    /// <summary>
    /// Returns a forecast for the coordinates, served from the short-lived cache when fresh. On a
    /// network error any previously cached snapshot is returned (so the bar keeps showing the last
    /// reading); only a cold failure yields null.
    /// </summary>
    public static async Task<WeatherData?> GetAsync(double lat, double lon, bool metric, bool force = false, CancellationToken ct = default)
    {
        var key = $"{lat:F3},{lon:F3},{metric}";
        if (!force && _cache.TryGetValue(key, out var fresh) && DateTime.UtcNow - fresh.FetchedUtc < _ttl)
            return fresh;
        try
        {
            var data = await FetchAsync(lat, lon, metric, ct);
            if (data != null) _cache[key] = data;
            return data ?? (_cache.TryGetValue(key, out var stale) ? stale : null);
        }
        catch
        {
            return _cache.TryGetValue(key, out var stale) ? stale : null;
        }
    }

    private static async Task<WeatherData?> FetchAsync(double lat, double lon, bool metric, CancellationToken ct)
    {
        var url = $"{FcstUrl}?latitude={Num(lat)}&longitude={Num(lon)}"
                + "&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m,wind_direction_10m"
                + "&hourly=temperature_2m,relative_humidity_2m,weather_code,precipitation,precipitation_probability,snowfall,wind_speed_10m,wind_direction_10m"
                + "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,precipitation_sum,snowfall_sum,wind_speed_10m_max,wind_direction_10m_dominant,uv_index_max"
                // Daily up to 14 days; hourly capped at 48 hours (forecast_hours overrides the hourly horizon).
                + "&timezone=auto&forecast_days=14&forecast_hours=48"
                + $"&temperature_unit={(metric ? "celsius" : "fahrenheit")}"
                + $"&wind_speed_unit={(metric ? "kmh" : "mph")}"
                + $"&precipitation_unit={(metric ? "mm" : "inch")}";

        using var doc = await GetJsonAsync(url, ct);
        var root = doc.RootElement;

        var cur = root.GetProperty("current");
        var current = new CurrentWeather(
            ParseTime(Str(cur, "time")),
            Dbl(cur, "temperature_2m"),
            (int)Math.Round(Dbl(cur, "relative_humidity_2m")),
            (int)Dbl(cur, "weather_code"),
            Dbl(cur, "apparent_temperature"),
            Dbl(cur, "wind_speed_10m"),
            Dbl(cur, "wind_direction_10m"));

        var hourly = new List<HourEntry>();
        if (root.TryGetProperty("hourly", out var h) && h.TryGetProperty("time", out var ht))
        {
            var temp = h.GetProperty("temperature_2m");
            var hum  = h.GetProperty("relative_humidity_2m");
            var code = h.GetProperty("weather_code");
            h.TryGetProperty("precipitation", out var pcp);
            h.TryGetProperty("precipitation_probability", out var pprob);
            h.TryGetProperty("snowfall", out var snow);
            h.TryGetProperty("wind_speed_10m", out var wspd);
            h.TryGetProperty("wind_direction_10m", out var wdir);
            for (int i = 0; i < ht.GetArrayLength(); i++)
                hourly.Add(new HourEntry(ParseTime(ht[i].GetString()),
                    At(temp, i), (int)Math.Round(At(hum, i)), (int)At(code, i),
                    At(pcp, i), (int)At(pprob, i), At(snow, i), At(wspd, i), At(wdir, i)));
        }

        var daily = new List<DayEntry>();
        if (root.TryGetProperty("daily", out var d) && d.TryGetProperty("time", out var dt))
        {
            var code = d.GetProperty("weather_code");
            var tmax = d.GetProperty("temperature_2m_max");
            var tmin = d.GetProperty("temperature_2m_min");
            d.TryGetProperty("precipitation_probability_max", out var pp);
            d.TryGetProperty("precipitation_sum", out var psum);
            d.TryGetProperty("snowfall_sum", out var ssum);
            d.TryGetProperty("wind_speed_10m_max", out var wmax);
            d.TryGetProperty("wind_direction_10m_dominant", out var wdir);
            d.TryGetProperty("uv_index_max", out var uv);
            for (int i = 0; i < dt.GetArrayLength(); i++)
                daily.Add(new DayEntry(ParseTime(dt[i].GetString()), (int)At(code, i),
                    At(tmax, i), At(tmin, i), (int)At(pp, i), At(psum, i), At(ssum, i),
                    At(wmax, i), At(wdir, i), At(uv, i)));
        }

        return new WeatherData
        {
            Metric     = metric,
            WindUnit   = metric ? "km/h" : "mph",
            PrecipUnit = metric ? "mm" : "in",
            SnowUnit   = metric ? "cm" : "in",
            Current    = current,
            Hourly     = hourly,
            Daily      = daily,
        };
    }

    // ── JSON helpers (tolerant of missing/null fields) ──────────────────────────
    private static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var s = await resp.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(s, cancellationToken: ct);
    }

    private static string Num(double v) => v.ToString(CultureInfo.InvariantCulture);

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double Dbl(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static double At(JsonElement arr, int i)
        => arr.ValueKind == JsonValueKind.Array && i < arr.GetArrayLength() && arr[i].ValueKind == JsonValueKind.Number
            ? arr[i].GetDouble() : 0;

    // Open-Meteo returns local (location-zone) timestamps without an offset, e.g. "2026-06-13T15:00".
    private static DateTime ParseTime(string? s)
        => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : DateTime.MinValue;
}
