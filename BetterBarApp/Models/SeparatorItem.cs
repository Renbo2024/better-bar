using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterBarApp.Models;

public partial class SeparatorItem : BarItem, IGrowToFillItem
{
    public SeparatorItem()
    {
        TypeKey     = ItemTypes.Separator;
        DisplayName = "Separator";
    }

    // Space (px) between the separator line and the items on either side.
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private int  _margin  = 6;
    // Whether the separator line is drawn (false = invisible spacer).
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private bool _visible = true;
    // Whether this separator expands to fill the panel's remaining width.
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private bool _growToFill;

    [JsonIgnore]
    public override string Description
    {
        get
        {
            var parts = new List<string>
            {
                Visible ? "visible" : "invisible",
                $"{Margin}px margin",
            };
            if (GrowToFill) parts.Add("grow to fill");
            return string.Join("  ·  ", parts);
        }
    }
}
