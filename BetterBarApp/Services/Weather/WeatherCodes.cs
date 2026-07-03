namespace BetterBarApp.Services.Weather;

/// <summary>
/// A coarse weather category derived from a WMO weather-interpretation code. Members are ordered by
/// increasing severity, so the numeric value doubles as a "how bad is it" rank — used to pick the
/// worst condition over a forecast window. The control maps each category to an icon glyph.
/// </summary>
public enum WeatherCondition
{
    Clear,
    MainlyClear,
    PartlyCloudy,
    Overcast,
    Fog,
    Drizzle,
    FreezingDrizzle,
    Rain,
    RainShowers,
    SnowGrains,
    Snow,
    SnowShowers,
    FreezingRain,
    Thunderstorm,
    ThunderstormHail,
}

/// <summary>Maps Open-Meteo WMO <c>weather_code</c> values to a <see cref="WeatherCondition"/> and a label.</summary>
public static class WeatherCodes
{
    public static WeatherCondition Classify(int code) => code switch
    {
        0            => WeatherCondition.Clear,
        1            => WeatherCondition.MainlyClear,
        2            => WeatherCondition.PartlyCloudy,
        3            => WeatherCondition.Overcast,
        45 or 48     => WeatherCondition.Fog,
        51 or 53 or 55 => WeatherCondition.Drizzle,
        56 or 57     => WeatherCondition.FreezingDrizzle,
        61 or 63 or 65 => WeatherCondition.Rain,
        66 or 67     => WeatherCondition.FreezingRain,
        71 or 73 or 75 => WeatherCondition.Snow,
        77           => WeatherCondition.SnowGrains,
        80 or 81 or 82 => WeatherCondition.RainShowers,
        85 or 86     => WeatherCondition.SnowShowers,
        95           => WeatherCondition.Thunderstorm,
        96 or 99     => WeatherCondition.ThunderstormHail,
        _            => WeatherCondition.PartlyCloudy,
    };

    /// <summary>Severity rank (higher = worse). The condition order already encodes this.</summary>
    public static int Severity(int code) => (int)Classify(code);

    /// <summary>Human-readable description of a code, for tooltips and the flyout.</summary>
    public static string Describe(int code) => code switch
    {
        0  => "Clear sky",
        1  => "Mainly clear",
        2  => "Partly cloudy",
        3  => "Overcast",
        45 => "Fog",
        48 => "Depositing rime fog",
        51 => "Light drizzle",
        53 => "Moderate drizzle",
        55 => "Dense drizzle",
        56 => "Light freezing drizzle",
        57 => "Dense freezing drizzle",
        61 => "Slight rain",
        63 => "Moderate rain",
        65 => "Heavy rain",
        66 => "Light freezing rain",
        67 => "Heavy freezing rain",
        71 => "Slight snow",
        73 => "Moderate snow",
        75 => "Heavy snow",
        77 => "Snow grains",
        80 => "Slight rain showers",
        81 => "Moderate rain showers",
        82 => "Violent rain showers",
        85 => "Slight snow showers",
        86 => "Heavy snow showers",
        95 => "Thunderstorm",
        96 => "Thunderstorm with slight hail",
        99 => "Thunderstorm with heavy hail",
        _  => "Unknown",
    };
}
