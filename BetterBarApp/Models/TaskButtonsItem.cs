using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterBarApp.Models;

public partial class TaskButtonsItem : BarItem, IGrowToFillItem
{
    public TaskButtonsItem()
    {
        TypeKey     = ItemTypes.TaskButtons;
        DisplayName = "Task Buttons";
    }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private int  _rows           = 1;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private int  _maxButtonWidth = 120;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private bool _showAllMonitors;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private bool _growToFill;

    // Windows 11-style accent bar drawn along the bottom of each button.
    [ObservableProperty] private int    _accentThickness = 3;
    [ObservableProperty] private string _accentColor           = "";  // selected/focused bar; "" = theme accent
    [ObservableProperty] private string _unselectedAccentColor = "";  // running (unfocused) bar; "" = same as AccentColor
    [ObservableProperty] private int    _selectedPillPercent   = 100;  // bar width % when focused
    [ObservableProperty] private int    _unselectedPillPercent = 100;  // bar width % when running

    // Empty space kept around each button's text label.
    [ObservableProperty] private int _textMargin = 4;

    // Horizontal gap (px) between adjacent button columns. Vertical (row) spacing is fixed.
    [ObservableProperty] private int _horizontalSpacing = 3;

    // Hover tooltip (window title) on each button.
    [ObservableProperty] private bool _showTooltips   = true;
    [ObservableProperty] private int  _tooltipDelayMs = 700;

    // Comma-separated list of text to match (Title or app); matched buttons sort first, in list
    // order (earlier = higher priority). Unmatched buttons keep their natural order.
    [ObservableProperty] private string _priorityOrder = "";

    // Task buttons are the one item that shrinks below content size to fit the panel.
    [JsonIgnore] public override bool Shrinkable => true;

    [JsonIgnore]
    public override string Description
    {
        get
        {
            var parts = new List<string>
            {
                $"{Rows} row{(Rows == 1 ? "" : "s")}",
                $"max {MaxButtonWidth}px",
                ShowAllMonitors ? "all monitors" : "this monitor",
            };
            if (GrowToFill) parts.Add("grow to fill");
            return string.Join("  ·  ", parts);
        }
    }
}
