using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterBarApp.Models;

/// <summary>How a monitor graph scrolls as new samples arrive.</summary>
public enum MonitorScrollMode { PerSample, Smoothed }

/// <summary>
/// A System Monitor: one or more monitor widgets laid out left-to-right (in list order),
/// each separately configurable. Top-level properties control spacing between widgets and
/// the outer margins.
/// </summary>
public partial class SystemMonitorItem : BarItem
{
    public SystemMonitorItem()
    {
        TypeKey     = ItemTypes.SystemMonitor;
        DisplayName = "System Monitor";
    }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private int _spacing        = 8;  // between widgets
    [ObservableProperty] private int _sideMargin     = 6;  // left/right
    [ObservableProperty] private int _verticalMargin = 2;  // top/bottom

    /// <summary>Widgets in left-to-right display order. Needs a setter for JSON round-trip.</summary>
    public List<MonitorWidget> Widgets { get; set; } = new();

    [JsonIgnore]
    public override string Description =>
        Widgets.Count == 0 ? "(no widgets)" : $"{Widgets.Count} widget{(Widgets.Count == 1 ? "" : "s")}";
}

/// <summary>
/// Base for a single monitor widget. Common properties: a fixed width, an optional title
/// (drawn over the top of the visual) and subtitle (over the bottom) — each with their own
/// font/size — and a single text colour shared by both (chosen to stand out from the visual).
/// Title/Subtitle support a "%value%" placeholder that the widget replaces with its current
/// (fixed-width-formatted) value.
/// </summary>
[JsonDerivedType(typeof(CpuMonitorWidget),     typeDiscriminator: "Cpu")]
[JsonDerivedType(typeof(NetworkMonitorWidget), typeDiscriminator: "Network")]
public abstract partial class MonitorWidget : ObservableObject
{
    /// <summary>Display name of the widget type (for the settings list).</summary>
    [JsonIgnore] public abstract string TypeName { get; }

    [ObservableProperty] private int    _width = 90;

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _titleFontFamily = "Segoe UI";
    [ObservableProperty] private int    _titleFontSize = 10;

    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _subtitleFontFamily = "Segoe UI";
    [ObservableProperty] private int    _subtitleFontSize = 9;

    /// <summary>Colour for title and subtitle text. "" = theme default.</summary>
    [ObservableProperty] private string _textColor = "";

    /// <summary>Draw a high-contrast shadow behind the title/subtitle so the graph can't obscure
    /// them (light text gets a dark shadow and vice-versa).</summary>
    [ObservableProperty] private bool _textShadow;

    /// <summary>Shadow blur radius (px) when <see cref="TextShadow"/> is on — larger = softer/bigger.</summary>
    [ObservableProperty] private int _textShadowSize = 4;
}

/// <summary>
/// CPU monitor widget: per-logical-processor usage drawn as overlapping filled line graphs
/// (one fill per thread at a set opacity, so busier CPUs read as a more prominent colour).
/// An optional static (non-scrolling) grid can be drawn behind the fills.
/// </summary>
public partial class CpuMonitorWidget : MonitorWidget
{
    [JsonIgnore] public override string TypeName => "CPU Monitor";

    [ObservableProperty] private MonitorScrollMode _scrollMode = MonitorScrollMode.Smoothed;
    [ObservableProperty] private int    _sampleRateMs   = 1000;  // 250/500/1000/1500/2000
    [ObservableProperty] private int    _timeSpanSec    = 60;    // overall window
    [ObservableProperty] private string _color          = "#FF22A0FF";
    [ObservableProperty] private int    _opacityPercent = 30;    // per-thread fill opacity

    [ObservableProperty] private bool   _showGrid  = false;
    [ObservableProperty] private string _gridColor = "#40FFFFFF"; // static grid (10×4 segments)
}

/// <summary>
/// Network monitor widget: throughput of a SINGLE interface drawn as a filled "total"
/// (send+receive, from the bottom) with separate receive and send lines on top. The vertical
/// scale tracks 110% of the largest total over the last 5 minutes (so a slow link never
/// shows a 10 Gbps grid line). An optional grid draws fixed bandwidth lines (10 Mbps /
/// 100 Mbps / 1 Gbps / 10 Gbps, each individually toggleable) and an optional reference line
/// at the average total over the visible span. Smoothing / time span / sample rate behave
/// exactly like the CPU widget. Title/Subtitle placeholders: %receive%, %send%, %total%.
/// </summary>
public partial class NetworkMonitorWidget : MonitorWidget
{
    [JsonIgnore] public override string TypeName => "Network Monitor";

    /// <summary>The interface to measure (NetworkInterface.Id). Empty = none selected yet.</summary>
    [ObservableProperty] private string _interfaceId = "";

    [ObservableProperty] private MonitorScrollMode _scrollMode = MonitorScrollMode.Smoothed;
    [ObservableProperty] private int _sampleRateMs = 1000;  // 250/500/1000/1500/2000
    [ObservableProperty] private int _timeSpanSec  = 60;    // overall window

    // Total transfer (send+receive): filled from the bottom, no line. Drawn first.
    [ObservableProperty] private string _totalColor          = "#FF22A0FF";
    [ObservableProperty] private int    _totalOpacityPercent = 30;

    // Receive / send: lines only, drawn over the total fill.
    [ObservableProperty] private string _receiveColor          = "#FF36D17A";
    [ObservableProperty] private int    _receiveOpacityPercent = 100;
    [ObservableProperty] private string _sendColor             = "#FFFF8C42";
    [ObservableProperty] private int    _sendOpacityPercent    = 100;

    // Grid: vertical columns (like CPU) plus the four selectable bandwidth lines.
    [ObservableProperty] private bool   _showGrid    = false;
    [ObservableProperty] private string _gridColor   = "#40FFFFFF";
    [ObservableProperty] private bool   _showBand10M  = true;   // 10 Mbps
    [ObservableProperty] private bool   _showBand100M = true;   // 100 Mbps
    [ObservableProperty] private bool   _showBand1G   = true;   // 1 Gbps
    [ObservableProperty] private bool   _showBand10G  = true;   // 10 Gbps

    // Average-over-span reference line (recomputed at most every other sample).
    [ObservableProperty] private bool   _showAverage  = false;
    [ObservableProperty] private string _averageColor = "#A0FFE000";
}
