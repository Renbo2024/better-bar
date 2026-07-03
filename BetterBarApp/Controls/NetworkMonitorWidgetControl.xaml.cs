using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BetterBarApp.Models;
using BetterBarApp.Services;

namespace BetterBarApp.Controls;

/// <summary>
/// One Network monitor widget: the scrolling throughput graph fills the control, with an
/// optional title over the top and subtitle over the bottom. Title/subtitle support %receive%,
/// %send% and %total% (the live rates, formatted in bits/sec). Setting changes are applied
/// in place — the graph keeps its samples and simply redraws — so edits are seen immediately.
/// </summary>
public partial class NetworkMonitorWidgetControl : UserControl
{
    private static readonly Color DefaultTotal    = Color.FromRgb(0x22, 0xA0, 0xFF);
    private static readonly Color DefaultReceive  = Color.FromRgb(0x36, 0xD1, 0x7A);
    private static readonly Color DefaultSend     = Color.FromRgb(0xFF, 0x8C, 0x42);
    private static readonly Color DefaultGrid     = Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF);
    private static readonly Color DefaultAverage  = Color.FromArgb(0xA0, 0xFF, 0xE0, 0x00);

    private readonly NetworkMonitorWidget _widget;
    private readonly NetworkSampler _sampler = new();
    private readonly DispatcherTimer _timer = new();

    // Per-value formatters that hold their unit steady (min 5s between Mbps/Gbps switches).
    private readonly RateFormatter _fmtReceive = new();
    private readonly RateFormatter _fmtSend    = new();
    private readonly RateFormatter _fmtTotal   = new();

    public NetworkMonitorWidgetControl(NetworkMonitorWidget widget)
    {
        _widget = widget;
        InitializeComponent();
        _timer.Tick += (_, _) => Tick();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyConfig();
        _widget.PropertyChanged += OnWidgetChanged;
        _timer.Start();
        Tick();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _widget.PropertyChanged -= OnWidgetChanged;
        _timer.Stop();
        Graph.Stop();
    }

    private void OnWidgetChanged(object? sender, PropertyChangedEventArgs e)
    {
        ApplyConfig();
        UpdateText();
    }

    private void ApplyConfig()
    {
        ApplyWidth(Math.Max(8, _widget.Width));
        _sampler.InterfaceId = _widget.InterfaceId;

        ConfigureLine(TitleText,    _widget.Title,    _widget.TitleFontFamily,    _widget.TitleFontSize,    _widget.TextColor);
        ConfigureLine(SubtitleText, _widget.Subtitle, _widget.SubtitleFontFamily, _widget.SubtitleFontSize, _widget.TextColor);

        int visible = Math.Max(2, (int)Math.Round(_widget.TimeSpanSec * 1000.0 / Math.Max(50, _widget.SampleRateMs)));
        Graph.Configure(
            ParseColor(_widget.TotalColor) ?? DefaultTotal,
            _widget.TotalOpacityPercent / 100.0,
            ParseColor(_widget.ReceiveColor) ?? DefaultReceive,
            _widget.ReceiveOpacityPercent / 100.0,
            ParseColor(_widget.SendColor)    ?? DefaultSend,
            _widget.SendOpacityPercent / 100.0,
            _widget.ScrollMode,
            _widget.SampleRateMs,
            visible,
            _widget.ShowGrid,
            ParseColor(_widget.GridColor) ?? DefaultGrid,
            _widget.ShowBand10M, _widget.ShowBand100M, _widget.ShowBand1G, _widget.ShowBand10G,
            _widget.ShowAverage,
            ParseColor(_widget.AverageColor) ?? DefaultAverage);

        var interval = TimeSpan.FromMilliseconds(Math.Max(50, _widget.SampleRateMs));
        if (_timer.Interval != interval) _timer.Interval = interval;
    }

    // Set our Width and, if it actually changed, make the owning BarItemsPanel re-measure so the
    // System Monitor item grows/shrinks its bar footprint (see BarItemsPanel.InvalidateForChild).
    private void ApplyWidth(double width)
    {
        bool changed = double.IsNaN(Width) || Math.Abs(Width - width) > 0.5;
        Width = width;
        if (changed) BarItemsPanel.InvalidateForChild(this);
    }

    private void Tick()
    {
        var (rcv, send) = _sampler.Sample();
        Graph.AddSample(rcv * 8, send * 8);   // bytes/sec → bits/sec
        UpdateText();
    }

    private void UpdateText()
    {
        if (TitleText.Visibility    == Visibility.Visible) TitleText.Text    = Format(_widget.Title);
        if (SubtitleText.Visibility == Visibility.Visible) SubtitleText.Text = Format(_widget.Subtitle);
    }

    // Replace %receive% / %send% / %total% with the live rates (bits/sec), each unit held steady.
    private string Format(string template)
    {
        var now = DateTime.UtcNow;
        double receive = _sampler.LastReceiveBytesPerSec * 8;
        double send    = _sampler.LastSendBytesPerSec    * 8;
        return template
            .Replace("%receive%", _fmtReceive.Format(receive, now))
            .Replace("%send%",    _fmtSend.Format(send, now))
            .Replace("%total%",   _fmtTotal.Format(receive + send, now));
    }

    private void ConfigureLine(OutlinedTextBlock tb, string text, string family, int size, string color)
    {
        if (string.IsNullOrEmpty(text)) { tb.Visibility = Visibility.Collapsed; return; }
        tb.Visibility = Visibility.Visible;
        if (!string.IsNullOrWhiteSpace(family)) tb.FontFamily = new FontFamily(family);
        tb.FontSize = Math.Max(1, size);
        MonitorText.ApplyLine(tb, color, _widget.TextShadow, _widget.TextShadowSize,
                              (this.TryFindResource("TaskBtnFg") as SolidColorBrush)?.Color);
    }

    private static Color? ParseColor(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        try { return (Color)ColorConverter.ConvertFromString(s)!; }
        catch { return null; }
    }
}
