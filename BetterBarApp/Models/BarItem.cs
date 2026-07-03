using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterBarApp.Models;

[JsonDerivedType(typeof(LauncherItem),    typeDiscriminator: "Launcher")]
[JsonDerivedType(typeof(TaskButtonsItem), typeDiscriminator: "TaskButtons")]
[JsonDerivedType(typeof(SeparatorItem),   typeDiscriminator: "Separator")]
[JsonDerivedType(typeof(StartButtonItem), typeDiscriminator: "StartButton")]
[JsonDerivedType(typeof(ClockItem),       typeDiscriminator: "Clock")]
[JsonDerivedType(typeof(SystemMonitorItem), typeDiscriminator: "SystemMonitor")]
[JsonDerivedType(typeof(AudioControlItem), typeDiscriminator: "AudioControl")]
[JsonDerivedType(typeof(SystemTrayItem), typeDiscriminator: "SystemTray")]
[JsonDerivedType(typeof(PowerItem), typeDiscriminator: "Power")]
[JsonDerivedType(typeof(WeatherItem), typeDiscriminator: "Weather")]
public abstract partial class BarItem : ObservableObject
{
    [ObservableProperty] private string _typeKey     = string.Empty;
    [ObservableProperty] private string _displayName = "New Item";

    /// <summary>
    /// One-line summary of the item's current settings, shown in column 2 of the
    /// panel editor's item list. Every item type must implement this — it should
    /// describe the configuration as concisely as possible.
    /// </summary>
    [JsonIgnore]
    public abstract string Description { get; }

    /// <summary>
    /// Internal sizing hint (not persisted, not user-facing): when the panel's items
    /// overflow the available width, shrinkable items are scaled down proportionally to
    /// make everything fit. Most items are fixed-width; only Task Buttons shrinks.
    /// </summary>
    [JsonIgnore]
    public virtual bool Shrinkable => false;
}
