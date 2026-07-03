using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BetterBarApp.Models;
using BetterBarApp.Services;

namespace BetterBarApp.Controls;

/// <summary>
/// One CPU monitor widget: the scrolling per-thread graph fills the control, with an optional
/// title painted over the top and subtitle over the bottom. Title/subtitle support %value%
/// (current overall CPU%, fixed-width so the text doesn't jitter). Setting changes are applied
/// in place — the graph keeps its samples and simply redraws — so edits are seen immediately.
/// </summary>
public partial class CpuMonitorWidgetControl : UserControl
{
    private static readonly Color DefaultColor    = Color.FromRgb(0x22, 0xA0, 0xFF);
    private static readonly Color DefaultGridColor = Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF);

    private readonly CpuMonitorWidget _widget;
    private readonly CpuSampler _sampler = new();
    private readonly DispatcherTimer _timer = new();

    public CpuMonitorWidgetControl(CpuMonitorWidget widget)
    {
        _widget = widget;
        InitializeComponent();
        _timer.Tick += (_, _) => Tick();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Graph.SetThreadCount(_sampler.ThreadCount);
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

    // Live edit: reconfigure visuals from the existing samples (no restart) and refresh text.
    private void OnWidgetChanged(object? sender, PropertyChangedEventArgs e)
    {
        ApplyConfig();
        UpdateText();
    }

    private void ApplyConfig()
    {
        ApplyWidth(Math.Max(8, _widget.Width));

        ConfigureLine(TitleText,    _widget.Title,    _widget.TitleFontFamily,    _widget.TitleFontSize,    _widget.TextColor);
        ConfigureLine(SubtitleText, _widget.Subtitle, _widget.SubtitleFontFamily, _widget.SubtitleFontSize, _widget.TextColor);

        int visible = Math.Max(2, (int)Math.Round(_widget.TimeSpanSec * 1000.0 / Math.Max(50, _widget.SampleRateMs)));
        Graph.SetThreadCount(_sampler.ThreadCount);
        Graph.Configure(
            ParseColor(_widget.Color) ?? DefaultColor,
            _widget.OpacityPercent / 100.0,
            _widget.ScrollMode,
            _widget.SampleRateMs,
            visible,
            _widget.ShowGrid,
            ParseColor(_widget.GridColor) ?? DefaultGridColor);

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
        Graph.AddSample(_sampler.Sample());
        UpdateText();
    }

    private void UpdateText()
    {
        if (TitleText.Visibility    == Visibility.Visible) TitleText.Text    = Format(_widget.Title);
        if (SubtitleText.Visibility == Visibility.Visible) SubtitleText.Text = Format(_widget.Subtitle);
    }

    // Replace %value% with the overall CPU% formatted to a fixed width (e.g. "  5%", " 42%", "100%").
    private string Format(string template)
    {
        int pct = (int)Math.Round(_sampler.Overall() * 100);
        return template.Replace("%value%", $"{pct,3}%");
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
