using BetterBarApp.Services.Weather;
using Wpf.Ui.Controls;

namespace BetterBarApp.Controls;

/// <summary>Maps a weather condition to a Fluent (WPF-UI) weather glyph. Day variants are used
/// throughout — the bar shows a single representative icon, not a time-of-day-accurate one.</summary>
public static class WeatherIcons
{
    public static SymbolRegular For(int weatherCode) => For(WeatherCodes.Classify(weatherCode));

    public static SymbolRegular For(WeatherCondition c) => c switch
    {
        WeatherCondition.Clear            => SymbolRegular.WeatherSunny24,
        WeatherCondition.MainlyClear      => SymbolRegular.WeatherSunnyHigh24,
        WeatherCondition.PartlyCloudy     => SymbolRegular.WeatherPartlyCloudyDay24,
        WeatherCondition.Overcast         => SymbolRegular.WeatherCloudy24,
        WeatherCondition.Fog              => SymbolRegular.WeatherFog24,
        WeatherCondition.Drizzle          => SymbolRegular.WeatherDrizzle24,
        WeatherCondition.FreezingDrizzle  => SymbolRegular.WeatherRainSnow24,
        WeatherCondition.Rain             => SymbolRegular.WeatherRain24,
        WeatherCondition.RainShowers      => SymbolRegular.WeatherRainShowersDay24,
        WeatherCondition.SnowGrains       => SymbolRegular.WeatherSnowflake24,
        WeatherCondition.Snow             => SymbolRegular.WeatherSnow24,
        WeatherCondition.SnowShowers      => SymbolRegular.WeatherSnowShowerDay24,
        WeatherCondition.FreezingRain     => SymbolRegular.WeatherRainSnow24,
        WeatherCondition.Thunderstorm     => SymbolRegular.WeatherThunderstorm24,
        WeatherCondition.ThunderstormHail => SymbolRegular.WeatherThunderstorm24,
        _                                 => SymbolRegular.WeatherPartlyCloudyDay24,
    };
}
