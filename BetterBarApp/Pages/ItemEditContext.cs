using BetterBarApp.Models;

namespace BetterBarApp.Pages;

/// <summary>Navigation parameter for an item-settings page: which item, and the bar
/// definition it belongs to (so the page can save + refresh live panels).</summary>
public sealed record ItemEditContext(BarDefinition Definition, BarItem Item);
