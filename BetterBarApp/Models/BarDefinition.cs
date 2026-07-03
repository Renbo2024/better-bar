using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterBarApp.Models;

/// <summary>
/// A reusable bar layout: its height and its ordered items. A definition does NOT
/// say where on screen it appears — one definition can drive many <see cref="PanelConfig"/>
/// placements (different monitors / edges), and editing it updates them all.
/// </summary>
public partial class BarDefinition : ObservableObject
{
    [ObservableProperty] private Guid   _id = Guid.NewGuid();
    [ObservableProperty] private string _name = "Bar";
    [ObservableProperty] private int    _heightPx = 40;

    // Ordered items. The collection reference is fixed; only its contents change,
    // so it isn't an [ObservableProperty].
    public ObservableCollection<BarItem> Items { get; } = [];
}
