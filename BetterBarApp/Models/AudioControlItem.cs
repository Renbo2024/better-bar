using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterBarApp.Models;

/// <summary>
/// Audio Control: a Speaker and a Microphone icon button. Each opens a flyout (device picker +
/// level slider) and shows a level meter beneath it — a centre dot that extends left for the left
/// channel and right for the right channel, up to the icon's width.
/// </summary>
public partial class AudioControlItem : BarItem
{
    public AudioControlItem()
    {
        TypeKey     = ItemTypes.AudioControl;
        DisplayName = "Audio Control";
    }

    [ObservableProperty] private bool _showSpeaker    = true;
    [ObservableProperty] private bool _showMicrophone = true;

    [ObservableProperty] private int _iconSize         = 25;  // glyph size
    [ObservableProperty] private int _horizontalMargin = 6;   // space to neighbouring bar items
    [ObservableProperty] private int _iconSpacing      = 6;   // space between the two icon buttons

    [ObservableProperty] private int    _meterThickness = 3;  // level-meter bar thickness
    [ObservableProperty] private string _meterColor     = ""; // "" = theme accent
    [ObservableProperty] private int    _iconMeterGap   = 2;  // space between an icon and its meter

    // Meter response (it's an activity indicator, not a precise meter):
    [ObservableProperty] private int  _meterSmoothing = 50;   // 0..100% — temporal glide / jitter damping
    [ObservableProperty] private bool _meterAutoScale = true; // adapt the meter range to recent peaks (L/R share one scale)
    [ObservableProperty] private int  _meterScaleSpeed = 30;  // 0..100% — how fast the range re-adapts after a peak

    [JsonIgnore]
    public override string Description
    {
        get
        {
            var parts = new List<string>();
            if (ShowSpeaker)    parts.Add("speaker");
            if (ShowMicrophone) parts.Add("microphone");
            return parts.Count == 0 ? "(nothing shown)" : string.Join(" + ", parts);
        }
    }
}
