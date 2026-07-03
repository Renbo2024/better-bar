using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterBarApp.Models;

/// <summary>
/// Power: up to four action buttons — Power (shut down), Hibernate, Sleep, Log Off — each with a
/// commonly-understood icon and an optional text label. Hibernate is only shown when the system
/// supports it. Icon size, spacing, outer margin, and label font/size are configurable, plus an
/// optional confirmation prompt before an action runs.
/// </summary>
public partial class PowerItem : BarItem
{
    public PowerItem()
    {
        TypeKey     = ItemTypes.Power;
        DisplayName = "Power";
    }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private bool _showPower     = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private bool _showReboot    = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private bool _showHibernate = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private bool _showSleep     = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private bool _showLogOff    = true;

    [ObservableProperty] private bool   _showLabels      = false;   // all-or-nothing text labels
    [ObservableProperty] private int    _iconSize        = 20;
    [ObservableProperty] private int    _iconSpacing     = 8;
    [ObservableProperty] private int    _outerMargin     = 6;
    [ObservableProperty] private string _labelFontFamily = "Segoe UI";
    [ObservableProperty] private int    _labelFontSize   = 10;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private bool _confirmAction = true;  // ask before acting

    [JsonIgnore]
    public override string Description
    {
        get
        {
            var parts = new List<string>();
            if (ShowPower)     parts.Add("Power");
            if (ShowReboot)    parts.Add("Reboot");
            if (ShowHibernate) parts.Add("Hibernate");
            if (ShowSleep)     parts.Add("Sleep");
            if (ShowLogOff)    parts.Add("Log Off");
            string list = parts.Count == 0 ? "(nothing shown)" : string.Join(", ", parts);
            return list + (ConfirmAction ? "  ·  confirm before acting" : "");
        }
    }
}
