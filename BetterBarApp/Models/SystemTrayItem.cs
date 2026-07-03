using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterBarApp.Models;

/// <summary>
/// System Tray: shows the Windows notification-area icons. The clock is never included (it's a
/// separate taskbar element, handled by the Clock item) and the sound/volume icon is excluded by
/// default (handled by the Audio Control item). Icons are laid out exactly like the Launcher —
/// same rows, spacing, and margin logic.
/// </summary>
public partial class SystemTrayItem : BarItem
{
    public SystemTrayItem()
    {
        TypeKey     = ItemTypes.SystemTray;
        DisplayName = "System Tray";
    }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private int  _rows         = 1;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private int  _iconSpacing  = 4;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private int  _iconMargin   = 4;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private bool _excludeSound      = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private bool _excludeMicrophone = true;

    [JsonIgnore]
    public override string Description =>
        $"{Rows} row{(Rows == 1 ? "" : "s")}  ·  {IconSpacing}px spacing  ·  {IconMargin}px margin"
        + (ExcludeSound ? "  ·  no sound icon" : "")
        + (ExcludeMicrophone ? "  ·  no mic icon" : "");
}
