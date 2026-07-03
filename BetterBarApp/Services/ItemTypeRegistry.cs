using BetterBarApp.Models;

namespace BetterBarApp.Services;

public record ItemTypeInfo(string Key, string DisplayName, Func<BarItem> Factory);

public static class ItemTypeRegistry
{
    private static readonly List<ItemTypeInfo> _types =
    [
        new(ItemTypes.Launcher,    "Launcher",     () => new LauncherItem()),
        new(ItemTypes.TaskButtons, "Task Buttons", () => new TaskButtonsItem()),
        new(ItemTypes.Separator,   "Separator",    () => new SeparatorItem()),
        new(ItemTypes.StartButton, "Start Button", () => new StartButtonItem()),
        new(ItemTypes.Clock,       "Clock",        () => new ClockItem()),
        new(ItemTypes.SystemMonitor, "System Monitor", () => new SystemMonitorItem()),
        new(ItemTypes.AudioControl,  "Audio Control",  () => new AudioControlItem()),
        new(ItemTypes.SystemTray,    "System Tray",    () => new SystemTrayItem()),
        new(ItemTypes.Power,         "Power",          () => new PowerItem()),
        new(ItemTypes.Weather,       "Weather",        () => new WeatherItem()),
    ];

    public static IReadOnlyList<ItemTypeInfo> Types => _types;
}
