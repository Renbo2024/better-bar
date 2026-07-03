using CommunityToolkit.Mvvm.ComponentModel;
using BetterBarApp.Services;

namespace BetterBarApp.Models;

/// <summary>
/// A panel: one placement of a <see cref="BarDefinition"/> on a specific monitor and
/// edge. It carries no content of its own — the definition supplies height and items;
/// the panel only says where to render it and whether it's currently shown.
/// </summary>
public partial class PanelConfig : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();

    // Which bar definition this panel renders.
    [ObservableProperty] private Guid _definitionId;

    [ObservableProperty] private PanelPosition _position = PanelPosition.Bottom;
    [ObservableProperty] private bool _isEnabled;

    // Synthetic monitor number: 0 = primary (always present); 1..N = layout-ordered
    // screens (see ScreenService). Bound to numbers, not device names, so it survives
    // device-name churn; a panel whose number isn't present just doesn't show.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonitorDisplay))]
    private int _monitorNumber;

    public string MonitorDisplay => MonitorNumber == 0 ? "Primary" : $"Screen {MonitorNumber}";
}
