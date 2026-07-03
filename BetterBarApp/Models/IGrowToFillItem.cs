namespace BetterBarApp.Models;

/// <summary>
/// Implemented by item types that can expand to consume the panel's remaining width.
/// Only one such item per panel is honored (see PanelWindow.BuildItemControls).
/// </summary>
public interface IGrowToFillItem
{
    bool GrowToFill { get; set; }
}
