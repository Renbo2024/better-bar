using System;
using BetterBarApp.Models;

namespace BetterBarApp.Pages;

/// <summary>Maps a bar item to the settings page that edits it.</summary>
public static class ItemPages
{
    public static Type PageTypeFor(BarItem item) => item switch
    {
        StartButtonItem => typeof(StartButtonPage),
        LauncherItem    => typeof(LauncherItemPage),
        TaskButtonsItem => typeof(TaskButtonsPage),
        SeparatorItem   => typeof(SeparatorPage),
        ClockItem       => typeof(ClockPage),
        SystemMonitorItem => typeof(SystemMonitorPage),
        AudioControlItem  => typeof(AudioControlPage),
        SystemTrayItem    => typeof(SystemTrayPage),
        PowerItem         => typeof(PowerPage),
        WeatherItem       => typeof(WeatherPage),
        _               => typeof(BarsPage),
    };
}
